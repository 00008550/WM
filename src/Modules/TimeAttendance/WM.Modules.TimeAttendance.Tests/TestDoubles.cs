using WM.Modules.People.Contracts;
using WM.SharedKernel.Events;
using WM.SharedKernel.Time;

namespace WM.Modules.TimeAttendance.Tests;

/// <summary>A directory holding at most one employee, who is always "employed" for list queries.</summary>
/// <param name="outOfScope">The employee exists but lies outside the caller's data scope: every
/// scoped lookup misses them, and only <see cref="FindSelfAsync"/> finds them.</param>
internal sealed class StubDirectory(EmployeeSummary? employee, bool outOfScope = false) : IEmployeeDirectory
{
    private IReadOnlyList<EmployeeSummary> All => employee is null ? [] : [employee];
    private EmployeeSummary? Scoped => outOfScope ? null : employee;

    public Task<EmployeeSummary?> FindByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(Scoped);
    public Task<EmployeeSummary?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Scoped);
    public Task<EmployeeSummary?> FindSelfAsync(Guid ownEmployeeId, CancellationToken ct = default) =>
        Task.FromResult(employee?.Id == ownEmployeeId ? employee : null);
    public Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnAsync(DateOnly on, CancellationToken ct = default) =>
        Task.FromResult(All);
    public Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnUnscopedAsync(DateOnly on, CancellationToken ct = default) =>
        Task.FromResult(All);
    public Task<IReadOnlyList<EmployeeSummary>> ListEmployedAtLocalTodayAsync(CancellationToken ct = default) =>
        Task.FromResult(All);
    public Task<IReadOnlyList<EmployeeSummary>> ListEmployedAtLocalTodayUnscopedAsync(CancellationToken ct = default) =>
        Task.FromResult(All);
}

/// <summary>
/// Every site resolves to <see cref="Zone"/> — which a test may change, to stand in for an
/// administrator editing the site's zone after punches were recorded.
/// </summary>
internal sealed class FixedZones(ZoneId zone) : ISiteTimeZones
{
    public ZoneId Zone { get; set; } = zone;

    public Task<ResolvedZone> ForSiteAsync(Guid siteId, CancellationToken ct = default) =>
        Task.FromResult(new ResolvedZone(Zone, ZoneSource.Site, siteId));

    public Task<IReadOnlyDictionary<Guid, ResolvedZone>> ForSitesAsync(
        IEnumerable<Guid> siteIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ResolvedZone>>(
            siteIds.Distinct().ToDictionary(id => id, id => new ResolvedZone(Zone, ZoneSource.Site, id)));
}

/// <summary>A clock stopped at one instant.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class NoopEventStream : IEventStreamProducer
{
    public Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct = default)
        where TEvent : class => Task.CompletedTask;
}
