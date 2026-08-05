using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WM.Api.Realtime;
using WM.SharedKernel.Security;

namespace WM.Api.Infrastructure;

/// <summary>
/// The transports this host owns directly — everything a module does not map. They live here
/// rather than inline in Program.cs so a test can enumerate the real composition instead of a
/// copy of it: these are exactly the transports "count routes, not transports" missed once.
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
        // balancers, orchestrator probes, uptime monitors — hold no credentials. AllowAnonymous
        // is load-bearing: without it the fallback policy in IdentityModule.RegisterServices
        // returns 401 here.
        //
        // Predicate false keeps it liveness now that readiness checks exist: it runs none of them
        // and answers "Healthy" whenever the process can answer at all. That is the correct
        // meaning for the thing an orchestrator restarts on — a database outage should not make
        // every replica get killed and rescheduled.
        endpoints.MapHealthChecks(
                WmHealthChecks.LivenessPath,
                new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        // Readiness: connected and migrated, plus a build identity when one is configured. It is
        // anonymous for the same reason liveness is — the external smoke check that asks it runs
        // from GitHub against the public URL and holds no credentials — and it is the fifth entry
        // in EndpointAuthorizationInventoryTests' anonymous surface, so adding it was a decision
        // with a reviewer attached rather than a drift.
        endpoints.MapHealthChecks(
                WmHealthChecks.ReadinessPath,
                new HealthCheckOptions
                {
                    Predicate = registration => registration.Tags.Contains(WmHealthChecks.ReadinessTag),
                    ResponseWriter = WmHealthChecks.WriteReadinessAsync,
                })
            .AllowAnonymous();

        return endpoints;
    }
}
