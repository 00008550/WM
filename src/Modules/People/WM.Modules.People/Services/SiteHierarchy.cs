using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Data;
using WM.SharedKernel.Security;

namespace WM.Modules.People.Services;

/// <summary>
/// Walks the site tree. Sites are few and change rarely, so loading the whole
/// (id, parentId) set once per request is cheaper and simpler than a recursive CTE,
/// and it cannot loop forever on a cyclic parent reference.
/// </summary>
public sealed class SiteHierarchy(PeopleDbContext db) : ISiteHierarchy
{
    public async Task<HashSet<Guid>> ExpandWithDescendantsAsync(
        IEnumerable<Guid> siteIds, CancellationToken ct = default)
    {
        var roots = siteIds as ICollection<Guid> ?? siteIds.ToList();
        var result = new HashSet<Guid>(roots);
        if (result.Count == 0)
            return result;

        var edges = await db.Sites.AsNoTracking()
            .Select(s => new { s.Id, s.ParentId })
            .ToListAsync(ct);

        var childrenByParent = edges
            .Where(e => e.ParentId.HasValue)
            .GroupBy(e => e.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList());

        var queue = new Queue<Guid>(result);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
                continue;
            foreach (var child in children)
            {
                // The Add guard also stops a cyclic parent chain from looping.
                if (result.Add(child))
                    queue.Enqueue(child);
            }
        }

        return result;
    }
}
