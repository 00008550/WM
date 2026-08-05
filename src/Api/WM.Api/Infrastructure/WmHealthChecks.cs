using System.Buffers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WM.Modules.Identity.Data;
using WM.Modules.People.Data;
using WM.Modules.TimeAttendance.Data;

namespace WM.Api.Infrastructure;

/// <summary>
/// Liveness and readiness, deliberately separated.
///
/// <para>
/// <c>/health</c> ran with no checks registered at all, so it returned <c>Healthy</c> with the
/// database on fire — and the container's <c>HEALTHCHECK</c> pointed at it, so Docker reported a
/// broken API as healthy and anything waiting on <c>service_healthy</c> was really only waiting
/// for the process to exist. Liveness keeps that meaning on purpose (it answers "this process is
/// still answering", which is what an orchestrator restarts on); readiness is the new endpoint
/// that can fail.
/// </para>
///
/// <para>
/// Readiness lives under <c>/api/</c> because that is the only prefix the portal's nginx proxies
/// (<c>frontend/portal/nginx.conf</c> forwards <c>/api/</c> and <c>/hubs/</c> and nothing else).
/// Putting it there means an external smoke check reaches it exactly as a visitor would, with no
/// nginx rule and no second origin.
/// </para>
/// </summary>
public static class WmHealthChecks
{
    /// <summary>Liveness: the process is up. It cannot fail, and that is the whole point.</summary>
    public const string LivenessPath = "/health";

    /// <summary>Readiness: connected and migrated. Under the prefix nginx already proxies.</summary>
    public const string ReadinessPath = "/api/health/ready";

    /// <summary>Marks a registration as belonging to readiness rather than liveness.</summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// A build identity for the running image — the commit SHA, for a deployment that sets one.
    /// <b>Empty by default</b>: readiness is anonymous, so a customer install discloses nothing
    /// about its build unless it chooses to. The demo sets it, because a smoke check that cannot
    /// tell which build answered cannot tell a silently-failed image pull from a good deploy.
    /// </summary>
    public const string BuildIdentityKey = "Build:Id";

    public static IServiceCollection AddWmHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessCheck<IdentityDbContext>>(
                "database:identity", tags: [ReadinessTag])
            .AddCheck<DatabaseReadinessCheck<PeopleDbContext>>(
                "database:people", tags: [ReadinessTag])
            .AddCheck<DatabaseReadinessCheck<TimeAttendanceDbContext>>(
                "database:time-attendance", tags: [ReadinessTag]);

        return services;
    }

    /// <summary>
    /// The readiness body. Written by hand rather than by serializing <see cref="HealthReport"/>
    /// so that what leaves the host is a closed list: overall status, per-check status and counts,
    /// and the build identity when there is one. In particular
    /// <see cref="HealthReportEntry.Exception"/> is never written — this endpoint is anonymous,
    /// and an Npgsql failure message carries the host, database and user name.
    /// </summary>
    public static async Task WriteReadinessAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var build = context.RequestServices.GetRequiredService<IConfiguration>()[BuildIdentityKey];

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("status", report.Status.ToString());

            // Omitted, not written as null or empty: "this install told us nothing" and "this
            // install says it is build ''" must not look the same to a smoke check.
            if (!string.IsNullOrWhiteSpace(build))
                json.WriteString("build", build.Trim());

            json.WriteStartObject("checks");
            foreach (var (name, entry) in report.Entries)
            {
                json.WriteStartObject(name);
                json.WriteString("status", entry.Status.ToString());
                if (!string.IsNullOrEmpty(entry.Description))
                    json.WriteString("detail", entry.Description);
                foreach (var (key, value) in entry.Data)
                {
                    if (value is int count)
                        json.WriteNumber(key, count);
                    else
                        json.WriteString(key, value?.ToString());
                }
                json.WriteEndObject();
            }
            json.WriteEndObject();

            json.WriteEndObject();
        }

        await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted);
    }
}

/// <summary>
/// Ready means <em>connected and migrated</em>, not "the process started".
///
/// The API applies its own migrations at startup, so a host that is up but still migrating cannot
/// serve yet — and everything that waits on readiness (a compose <c>depends_on: service_healthy</c>,
/// the demo seeder, the scheduled smoke check) needs that distinction to be real rather than
/// nominal.
/// </summary>
internal sealed class DatabaseReadinessCheck<TContext>(TContext db) : IHealthCheck
    where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Every string returned from here ends up verbatim in an anonymous response, so every one
        // of them is ours. None is derived from an exception.
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("cannot reach the database");

            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Count();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
            var counts = new Dictionary<string, object>
            {
                ["applied"] = applied,
                ["pending"] = pending,
            };

            return pending == 0
                ? HealthCheckResult.Healthy("migrated", data: counts)
                : HealthCheckResult.Unhealthy("migrations are still pending", data: counts);
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("cannot read the migration history");
        }
    }
}
