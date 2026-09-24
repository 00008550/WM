using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.Modules.Identity.Endpoints;
using WM.Modules.Identity.Services;
using WM.SharedKernel.Modules;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("Default"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "identity")));

        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName));
        services.AddScoped<TokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<UserManagementService>();
        services.AddScoped<SecurityGroupService>();
        services.AddScoped<IDataScopeResolver, DataScopeResolver>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                  ?? throw new InvalidOperationException(
                      "Jwt configuration section is missing. Set Jwt__Issuer, Jwt__Audience and "
                      + "Jwt__SigningKey before starting this host.");

        // Fail closed, here, before anything else is composed. Two properties are worth stating
        // because both were paid for:
        //
        //  * It throws during RegisterServices, not from a startup validator, so the process dies
        //    before Program.cs opens a connection to migrate. A container missing its key exits in
        //    milliseconds with this message rather than after a database timeout.
        //  * "Outside Development" is read from configuration rather than IHostEnvironment because
        //    IModule.RegisterServices is handed only the configuration — and the host resolves its
        //    own environment from the very same key. Absent means Production, matching the host's
        //    default, so an unusual hosting shape fails closed rather than open.
        var isDevelopment = string.Equals(
            configuration[HostDefaults.EnvironmentKey] ?? Environments.Production,
            Environments.Development,
            StringComparison.OrdinalIgnoreCase);

        if (JwtOptions.DescribeSigningKeyFault(jwt.SigningKey, isDevelopment) is { } fault)
            throw new InvalidOperationException(fault);

        // Lockout thresholds, bound the same way and checked in the same place. Absent means the
        // values that used to be consts in AuthService, so an install that configures nothing is
        // unchanged by this. A configured-but-nonsensical value is refused here rather than
        // discovered by an administrator who can no longer sign in.
        services.AddOptions<AccountLockoutOptions>()
            .Bind(configuration.GetSection(AccountLockoutOptions.SectionName));

        var lockout = configuration.GetSection(AccountLockoutOptions.SectionName).Get<AccountLockoutOptions>()
                      ?? new AccountLockoutOptions();

        if (AccountLockoutOptions.DescribeFault(lockout) is { } lockoutFault)
            throw new InvalidOperationException(lockoutFault);

        // Plan 011 P9: the password-less development sign-in. Set outside Development, the host
        // does not start — the environment gate at mapping would keep the route closed anyway, but
        // a setting that means "open a door" must not sit quietly in a production config.
        var devSignIn = configuration.GetSection(DevSignInOptions.SectionName).Get<DevSignInOptions>()
                        ?? new DevSignInOptions();

        if (DevSignInOptions.DescribeFault(devSignIn, isDevelopment) is { } devSignInFault)
            throw new InvalidOperationException(devSignInFault);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                // Allow SignalR websocket connections to authenticate via query string.
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = accessToken;
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization(o =>
        {
            // Default deny. Without a fallback policy an endpoint mapped without
            // RequireAuthorization is anonymous — the fail-open ARCHITECTURE.md §14 decision 5
            // rejects, and PHASE-AUDIT.md A2. The exceptions are now deliberate and visible:
            // /api/auth/login, /refresh, /logout and /health carry AllowAnonymous, which the
            // authorization middleware honours ahead of this policy.
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

            foreach (var permission in WmPermissions.All)
                o.AddPolicy(permission, p => p.RequireClaim(WmPermissions.ClaimType, permission));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        // A host that pushes live data replaces this with something that re-groups open
        // sockets. TryAdd so the module stands up on its own (worker, tests) without one.
        services.TryAddSingleton<IScopeChangeNotifier, NullScopeChangeNotifier>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        UserEndpoints.Map(endpoints);
        SecurityGroupEndpoints.Map(endpoints);

        // Both gates, read from the composed host rather than from anything the request carries.
        var services = endpoints.ServiceProvider;
        var devSignIn = services.GetRequiredService<IConfiguration>()
                            .GetSection(DevSignInOptions.SectionName).Get<DevSignInOptions>()
                        ?? new DevSignInOptions();
        if (DevSignInOptions.ShouldMap(devSignIn, services.GetRequiredService<IHostEnvironment>().IsDevelopment()))
            AuthEndpoints.MapDevSignIn(endpoints);
    }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private IReadOnlySet<string>? _permissions;

    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => WmClaims.UserIdOf(Principal);

    public string? UserName => Principal?.Identity?.Name;

    public Guid? EmployeeId =>
        Guid.TryParse(Principal?.FindFirst(WmClaims.EmployeeId)?.Value, out var id) ? id : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public IReadOnlySet<string> Permissions => _permissions ??=
        Principal?.FindAll(WmPermissions.ClaimType).Select(c => c.Value).ToHashSet() ?? [];

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}
