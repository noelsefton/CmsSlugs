using EPiServer.Core;

namespace CmsSlugs.Optimizely;

/// <summary>
/// Consumer-implemented bridge from Optimizely content to <see cref="SlugEntry"/> values.
/// Reuse the solution's existing slug-building logic here.
/// </summary>
public interface ISlugSource
{
    /// <summary>All slugs, for a full rebuild. Stream it — the index can be large.</summary>
    IEnumerable<SlugEntry> GetAll();

    /// <summary>
    /// Slugs for one changed content (incremental updates). An empty result means the content is
    /// not routable (e.g. unpublished), so it should stay removed from the index.
    /// </summary>
    IEnumerable<SlugEntry> GetForContent(IContent content);
}
