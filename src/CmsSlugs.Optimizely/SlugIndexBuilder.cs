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
        // A source that can never be read concurrently is scanned serially; otherwise honour the
        // configured degree (0 => processor count).
        var maxDop = !source.SupportsConcurrentResolve
            ? 1
            : options.MaxDegreeOfParallelism > 0
                ? options.MaxDegreeOfParallelism
                : Environment.ProcessorCount;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxDop };

        // Reading a cold, lazily-built shared cache (e.g. Commerce catalog metadata) from many
        // threads at once corrupts a non-concurrent collection. Prime it once on this thread before
        // fanning out so the parallel pass only reads it.
        if (maxDop > 1)
            source.WarmUp();

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
