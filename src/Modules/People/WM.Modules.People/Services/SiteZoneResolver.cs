using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Contracts;
using WM.Modules.People.Data;
using WM.SharedKernel.Time;

namespace WM.Modules.People.Services;

/// <summary>
/// <see cref="ISiteTimeZones"/> over People's own tables. Like <see cref="SiteHierarchy"/> it loads
/// the whole (id, parent, zone) set once per call — sites are few — and walks it in memory, where a
/// cyclic <c>ParentId</c> chain is stopped by a visited set rather than overflowing a stack.
/// </summary>
internal sealed class SiteZoneResolver(PeopleDbContext db, InstallationZone installation) : ISiteTimeZones
{
    public async Task<ResolvedZone> ForSiteAsync(Guid siteId, CancellationToken ct = default) =>
        (await ForSitesAsync([siteId], ct))[siteId];

    public async Task<IReadOnlyDictionary<Guid, ResolvedZone>> ForSitesAsync(
        IEnumerable<Guid> siteIds, CancellationToken ct = default)
    {
        var tree = await db.Sites.AsNoTracking()
            .Select(s => new SiteNode(s.Id, s.ParentId, s.TimeZone))
            .ToDictionaryAsync(s => s.Id, ct);

        var result = new Dictionary<Guid, ResolvedZone>();
        foreach (var id in siteIds)
            result.TryAdd(id, Resolve(id, tree, installation));
        return result;
    }

    internal sealed record SiteNode(Guid Id, Guid? ParentId, string? TimeZone);

    /// <summary>
    /// The chain, as a pure function so it is testable without a database. A stored id that no
    /// longer parses (writes are validated and 008 P2's migration nulled unparseable rows, so this
    /// takes a hand edit or a tzdata removal) is treated as unset and the walk continues — one bad
    /// row must not take every timesheet down with it.
    /// </summary>
    internal static ResolvedZone Resolve(
        Guid siteId, IReadOnlyDictionary<Guid, SiteNode> tree, InstallationZone installation)
    {
        var visited = new HashSet<Guid>();
        Guid? current = siteId;
        while (current is { } id && visited.Add(id) && tree.TryGetValue(id, out var node))
        {
            if (ZoneId.TryParse(node.TimeZone, out var zone))
                return new ResolvedZone(zone, id == siteId ? ZoneSource.Site : ZoneSource.AncestorSite, id);
            current = node.ParentId;
        }

        return new ResolvedZone(installation.Zone, ZoneSource.Installation, null);
    }
}
