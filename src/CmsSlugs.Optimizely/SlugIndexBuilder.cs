using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CmsSlugs.Optimizely;

/// <summary>
/// Populates an <see cref="ISlugStore"/> from an <see cref="ISlugSource"/> and records progress on
/// <see cref="SlugIndexDiagnostics"/>. The per-content reads (the slow part on a large catalog) are
/// fanned across threads and collected, then handed to a single atomic <see cref="ISlugStore.Rebuild"/>.
/// All threading lives here, in the package — sources supply data only.
/// </summary>
public static class SlugIndexBuilder
{
    /// <summary>
    /// Build/replace the index. <paramref name="stopRequested"/> lets a scheduled job cancel
    /// cooperatively. Worker count comes from <see cref="SlugIndexOptions.MaxDegreeOfParallelism"/>
    /// (0 => processor count).
    /// </summary>
    public static void Rebuild(
        ISlugStore store,
        ISlugSource source,
        SlugIndexDiagnostics diagnostics,
        SlugIndexOptions? options = null,
        Func<bool>? stopRequested = null)
    {
        options ??= new SlugIndexOptions();

        // Mark in-progress before listing so status never briefly reads idle/0, then set the
        // denominator once the (cheap) listing is done and load each item in parallel.
        diagnostics.MarkStarted();
        var links = source.GetContentLinks().ToList();
        diagnostics.SetTotal(links.Count);

        var entries = new ConcurrentBag<SlugEntry>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism > 0
                ? options.MaxDegreeOfParallelism
                : Environment.ProcessorCount
        };

        Parallel.ForEach(links, parallelOptions, (link, loopState) =>
        {
            if (stopRequested?.Invoke() == true) { loopState.Stop(); return; }

            foreach (var entry in source.Resolve(link))
                entries.Add(entry);

            diagnostics.ReportProgress();
        });

        // Indexing is cheap (dictionary inserts) and stays a single atomic swap, so readers never
        // observe a half-built index and last-write-wins collision handling is unaffected.
        store.Rebuild(entries);
        diagnostics.MarkCompleted(store.Count);
    }
}
