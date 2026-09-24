using WM.SharedKernel.Time;

namespace WM.Modules.People.Contracts;

/// <summary>
/// Public contract (plan 008 P2): <i>"in which zone is this employee's day measured?"</i> Keyed by
/// <b>site</b>, because a consumer already holds the employee's home site on the
/// <see cref="EmployeeSummary"/> it obtained through <see cref="IEmployeeDirectory"/> — which is
/// also where the caller's data scope was applied. A zone discloses nothing about a person, so this
/// contract is not scoped itself.
///
/// <para>
/// <b>The home site's zone owns the day</b> (user decision 2026-09-24, open question 2 (a)): never
/// the zone of wherever a phone happened to punch. Travel is a later, explicit exception.
/// </para>
/// </summary>
public interface ISiteTimeZones
{
    /// <summary>
    /// Resolved as site → nearest ancestor site with a zone → installation default. An unknown
    /// site id resolves to the installation default, reported as such.
    /// </summary>
    Task<ResolvedZone> ForSiteAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>The same resolution for several sites against one read of the tree.</summary>
    Task<IReadOnlyDictionary<Guid, ResolvedZone>> ForSitesAsync(
        IEnumerable<Guid> siteIds, CancellationToken ct = default);
}

/// <summary>Which of the three links of the chain answered.</summary>
public enum ZoneSource
{
    /// <summary>The site itself has a zone set.</summary>
    Site,
    /// <summary>The site has none; an ancestor site's zone was inherited.</summary>
    AncestorSite,
    /// <summary>Nothing in the chain has one; the installation default applies.</summary>
    Installation,
}

/// <param name="Zone">The zone the day is measured in.</param>
/// <param name="Source">Which link of the chain supplied it.</param>
/// <param name="FromSiteId">The site whose zone answered; null when <paramref name="Source"/> is
/// <see cref="ZoneSource.Installation"/>.</param>
public sealed record ResolvedZone(ZoneId Zone, ZoneSource Source, Guid? FromSiteId);
