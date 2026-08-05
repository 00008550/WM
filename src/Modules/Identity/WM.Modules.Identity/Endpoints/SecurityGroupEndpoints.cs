using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WM.Modules.Identity.Services;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Endpoints;

public sealed record SetUserGroupsRequest(Guid[] GroupIds);

internal static class SecurityGroupEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var groups = endpoints.MapGroup("/api/security-groups").WithTags("Security groups")
            .RequireAuthorization(WmPermissions.RolesManage);

        groups.MapGet("/", async (SecurityGroupService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        groups.MapPost("/", async (SecurityGroupUpsertRequest request, SecurityGroupService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateAsync(request, ct);
            return result.Succeeded
                ? Results.Created($"/api/security-groups/{result.Id}", new { id = result.Id })
                : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        groups.MapPut("/{id:guid}", async (Guid id, SecurityGroupUpsertRequest request, SecurityGroupService svc, CancellationToken ct) =>
        {
            var result = await svc.UpdateAsync(id, request, ct);
            return result.Succeeded
                ? Results.NoContent()
                : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        groups.MapDelete("/{id:guid}", async (Guid id, SecurityGroupService svc, CancellationToken ct) =>
        {
            var result = await svc.DeleteAsync(id, ct);
            return result.Succeeded
                ? Results.NoContent()
                : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        // Group membership lives under the user it belongs to.
        var users = endpoints.MapGroup("/api/users").WithTags("Users")
            .RequireAuthorization(WmPermissions.UsersManage);

        users.MapGet("/{id:guid}/security-groups", async (Guid id, SecurityGroupService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetUserGroupIdsAsync(id, ct)));

        users.MapPut("/{id:guid}/security-groups", async (
            Guid id, SetUserGroupsRequest request, SecurityGroupService svc, CancellationToken ct) =>
        {
            var result = await svc.SetUserGroupsAsync(id, request.GroupIds, ct);
            return result.Succeeded
                ? Results.NoContent()
                : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        // Access diagnostics — "why can't this user see this employee?", answered from the
        // resolved scope. A WM addition, not a port: legacy ships no tool that explains an access
        // decision. (This comment used to cite legacy's DataAccessScopeDiagnostics as the
        // precedent. Measured 2026-08-05: it counts DataContext create/dispose to find connection
        // leaks and has nothing to do with authorization — TLW-AUTHORIZATION-MODEL.md §11 C2.)
        endpoints.MapGet("/api/access-diagnostics/{userId:guid}", async (
            Guid userId, IDataScopeResolver resolver, CancellationToken ct) =>
        {
            var scope = await resolver.GetScopeForUserAsync(userId, ct);
            return Results.Ok(new
            {
                userId,
                scope = scope.Kind.ToString(),
                explanation = Explain(scope),
                siteIds = scope.SiteIds,
                departmentIds = scope.DepartmentIds,
                selfEmployeeId = scope.SelfEmployeeId,
            });
        }).RequireAuthorization(WmPermissions.UsersManage).WithTags("Security groups");
    }

    private static string Explain(EffectiveDataScope scope) => scope.Kind switch
    {
        DataScopeKind.All => "Sees every employee (a group grants unrestricted scope).",
        DataScopeKind.Sites => $"Sees employees at {scope.SiteIds.Count} site(s), including child sites where enabled.",
        DataScopeKind.Departments => $"Sees employees in {scope.DepartmentIds.Count} department(s).",
        DataScopeKind.Self => "Sees only their own employee record (self-service).",
        _ => "Sees no employee data — the account is not linked to an employee and belongs to no security group.",
    };
}
