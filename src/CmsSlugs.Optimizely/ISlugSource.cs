using System.Collections.Generic;
using EPiServer.Core;

namespace CmsSlugs.Optimizely;

/// <summary>
/// Consumer-implemented bridge from Optimizely content to <see cref="SlugEntry"/> values.
/// Reuse the solution's existing slug-building logic here.
///
/// A full rebuild is fanned across threads by <see cref="SlugIndexBuilder"/> when the source reports
/// <see cref="SupportsConcurrentResolve"/> = true: it says <em>what</em> to index
/// (<see cref="GetContentLinks"/>, cheap) and <em>how to read one item</em> (<see cref="Resolve"/>).
/// Keep this implementation free of any threading code — the package owns the parallelism. Before a
/// parallel pass it calls <see cref="WarmUp"/> once, single-threaded, so the source can prime any
/// lazily-built shared caches its <see cref="Resolve"/> touches; a source that still cannot be read
/// concurrently at all opts out via <see cref="SupportsConcurrentResolve"/>.
/// </summary>
public interface ISlugSource
{
    /// <summary>Cheap listing of the content references to index (no heavy per-item loading).</summary>
    IEnumerable<ContentReference> GetContentLinks();

    /// <summary>
    /// Resolve one content reference to its slug entries. Called concurrently when
    /// <see cref="SupportsConcurrentResolve"/> is true (after <see cref="WarmUp"/>); otherwise serially.
    /// </summary>
    IEnumerable<SlugEntry> Resolve(ContentReference contentLink);

    /// <summary>
    /// Whether <see cref="Resolve"/> is safe to call from multiple threads at once, given that
    /// <see cref="WarmUp"/> has primed any shared caches first. Return true for the parallel
    /// speed-up (the normal case — Optimizely content loading is thread-safe once its lazily-built
    /// caches are warm). Return false only for a read path that can never be made concurrency-safe;
    /// the package then scans the whole rebuild serially regardless of MaxDegreeOfParallelism, so no
    /// configuration value can corrupt the source.
    /// </summary>
    bool SupportsConcurrentResolve { get; }

    /// <summary>
    /// Called once, single-threaded, by <see cref="SlugIndexBuilder"/> before a parallel read pass.
    /// Prime the lazily-populated shared caches that <see cref="Resolve"/> relies on (content-type
    /// model, provider metadata, etc.) so the concurrent calls that follow only read them — reading
    /// a cold cache from many threads at once is what corrupts a non-concurrent collection. Safe to
    /// no-op if the source has nothing to warm.
    /// </summary>
    void WarmUp();

    /// <summary>
    /// Slugs for one changed content (incremental updates). An empty result means the content is
    /// not routable (e.g. unpublished), so it should stay removed from the index.
    /// </summary>
    IEnumerable<SlugEntry> GetForContent(IContent content);
}
