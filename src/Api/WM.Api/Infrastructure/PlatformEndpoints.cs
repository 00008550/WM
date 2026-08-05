using WM.Api.Realtime;
using WM.SharedKernel.Security;

namespace WM.Api.Infrastructure;

/// <summary>
/// The transports this host owns directly — everything a module does not map. They live here
/// rather than inline in Program.cs so a test can enumerate the real composition instead of a
/// copy of it: these two are exactly the transports "count routes, not transports" missed once.
/// </summary>
public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The hub is a transport like any endpoint, and it gets a permission policy like any
        // endpoint. The [Authorize] attribute on the hub class says the same thing; both are kept
        // because the audit found this leak by reading the two places independently and finding
        // neither.
        endpoints.MapHub<AttendanceHub>("/hubs/attendance")
            .RequireAuthorization(WmPermissions.AttendanceView);

        // Liveness. It answers before anyone can sign in, because the things that ask — load
        // balancers, orchestrator probes, uptime monitors — hold no credentials. It is the only
        // deliberately public transport in the host, and AllowAnonymous is now load-bearing:
        // without it the fallback policy in IdentityModule.RegisterServices returns 401 here.
        endpoints.MapHealthChecks("/health").AllowAnonymous();

        return endpoints;
    }
}
