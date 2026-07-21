namespace WM.SharedKernel.Security;

/// <summary>
/// Site tree queries needed by scope resolution.
///
/// The contract lives in SharedKernel and is implemented by People, so Identity can
/// resolve a site-based scope without taking a project reference on another module.
/// </summary>
public interface ISiteHierarchy
{
    /// <summary>
    /// The given sites plus every descendant — a manager scoped to a region
    /// should see the sites beneath it, not just the region node itself.
    /// </summary>
    Task<HashSet<Guid>> ExpandWithDescendantsAsync(IEnumerable<Guid> siteIds, CancellationToken ct = default);
}
