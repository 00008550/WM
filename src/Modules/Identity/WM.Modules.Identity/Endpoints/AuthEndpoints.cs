using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WM.Modules.Identity.Services;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Endpoints;

public sealed record LoginRequest(string UserName, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    string DisplayName,
    string[] Permissions);

internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request.UserName, request.Password, ct);
            return result.Succeeded
                ? Results.Ok(ToResponse(result))
                : Results.Problem(result.Error, statusCode: StatusCodes.Status401Unauthorized);
        }).AllowAnonymous();

        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(request.RefreshToken, ct);
            return result.Succeeded
                ? Results.Ok(ToResponse(result))
                : Results.Problem(result.Error, statusCode: StatusCodes.Status401Unauthorized);
        }).AllowAnonymous();

        group.MapPost("/logout", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
        {
            await auth.LogoutAsync(request.RefreshToken, ct);
            return Results.NoContent();
        }).AllowAnonymous();

        group.MapGet("/me", (ICurrentUser user) => Results.Ok(new
        {
            user.UserId,
            user.UserName,
            Permissions = user.Permissions.Order().ToArray(),
        })).RequireAuthorization();
    }

    private static AuthResponse ToResponse(AuthResult result) => new(
        result.Tokens!.AccessToken,
        result.Tokens.AccessTokenExpiresAt,
        result.Tokens.RefreshToken,
        result.User!.DisplayName,
        AuthService.PermissionsOf(result.User).Order().ToArray());
}
