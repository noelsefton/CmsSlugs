using EPiServer.PlugIn;
using EPiServer.Scheduler;

namespace CmsSlugs.Optimizely;

/// <summary>
/// "Rebuild Slug Index" — full repopulation via <see cref="ISlugStore.Rebuild"/>.
/// Primary population path for durable stores (run once after deploy to seed); a manual refresh
/// for the in-memory store.
/// </summary>
[ScheduledPlugIn(
    DisplayName = "Rebuild Slug Index",
    Description = "Rebuilds the CmsSlugs (slug, culture) -> content index from the configured ISlugSource.",
    GUID = "7E2D1F4A-3C90-4B6E-9A1D-5F0C7B2E8A11")]
public sealed class RebuildSlugIndexJob : ScheduledJobBase
{
    private readonly ISlugStore _store;
    private readonly ISlugSource _source;
    private bool _stopRequested;

    public RebuildSlugIndexJob(ISlugStore store, ISlugSource source)
    {
        _store = store;
        _source = source;
        IsStoppable = true;
    }

    public override void Stop() => _stopRequested = true;

    public override string Execute()
    {
        _stopRequested = false;

        long count = 0;
        _store.Rebuild(Counting(_source.GetAll(), () => count++));

        return _stopRequested
            ? $"Stop requested. Index rebuilt with {count:N0} entries before stopping."
            : $"Index rebuilt: {count:N0} entries.";
    }

    private IEnumerable<SlugEntry> Counting(IEnumerable<SlugEntry> source, Action onItem)
    {
        foreach (var entry in source)
        {
            if (_stopRequested) yield break;
            onItem();
            yield return entry;
        }
    }
}
