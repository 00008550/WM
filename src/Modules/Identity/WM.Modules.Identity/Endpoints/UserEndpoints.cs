using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WM.Modules.Identity.Services;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Endpoints;

public sealed record ResetPasswordRequest(string NewPassword);

internal static class UserEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/api/users").WithTags("Users")
            .RequireAuthorization(WmPermissions.UsersManage);

        users.MapGet("/", async (UserManagementService svc, string? search, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            Results.Ok(await svc.ListAsync(search, page, pageSize, ct)));

        users.MapGet("/roles", async (UserManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListRolesAsync(ct)));

        users.MapPost("/", async (CreateUserRequest request, UserManagementService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateAsync(request, ct);
            return result.Succeeded
                ? Results.Created($"/api/users/{result.UserId}", new { id = result.UserId })
                : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        users.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, UserManagementService svc, CancellationToken ct) =>
        {
            var result = await svc.UpdateAsync(id, request, ct);
            return result.Succeeded
                ? Results.NoContent()
                : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        users.MapPost("/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest request, UserManagementService svc, CancellationToken ct) =>
        {
            var result = await svc.ResetPasswordAsync(id, request.NewPassword, ct);
            return result.Succeeded
                ? Results.NoContent()
                : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });
    }
}
