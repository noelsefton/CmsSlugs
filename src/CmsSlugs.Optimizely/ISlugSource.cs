using System.Collections.Generic;
using EPiServer.Core;

namespace CmsSlugs.Optimizely;

/// <summary>
/// Consumer-implemented bridge from Optimizely content to <see cref="SlugEntry"/> values.
/// Reuse the solution's existing slug-building logic here.
///
/// A full rebuild is always fanned across threads by <see cref="SlugIndexBuilder"/>: the source
/// only says <em>what</em> to index (<see cref="GetContentLinks"/>, cheap) and <em>how to read one
/// item</em> (<see cref="Resolve"/>, called concurrently). Keep this implementation free of any
/// threading code — the package owns the parallelism.
/// </summary>
public interface ISlugSource
{
    /// <summary>Cheap listing of the content references to index (no heavy per-item loading).</summary>
    IEnumerable<ContentReference> GetContentLinks();

    /// <summary>Resolve one content reference to its slug entries. Called concurrently.</summary>
    IEnumerable<SlugEntry> Resolve(ContentReference contentLink);

    /// <summary>
    /// Slugs for one changed content (incremental updates). An empty result means the content is
    /// not routable (e.g. unpublished), so it should stay removed from the index.
    /// </summary>
    IEnumerable<SlugEntry> GetForContent(IContent content);
}
