using EPiServer.PlugIn;
using EPiServer.Scheduler;
using Microsoft.Extensions.Options;

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
    private readonly SlugIndexDiagnostics _diagnostics;
    private readonly SlugIndexOptions _options;
    private bool _stopRequested;

    public RebuildSlugIndexJob(ISlugStore store, ISlugSource source, SlugIndexDiagnostics diagnostics, IOptions<SlugIndexOptions> options)
    {
        _store = store;
        _source = source;
        _diagnostics = diagnostics;
        _options = options.Value;
        IsStoppable = true;
    }

    public override void Stop() => _stopRequested = true;

    public override string Execute()
    {
        _stopRequested = false;

        SlugIndexBuilder.Rebuild(_store, _source, _diagnostics, _options, stopRequested: () => _stopRequested);
        var count = _diagnostics.LastBuildEntryCount ?? _store.Count;

        return _stopRequested
            ? $"Stop requested. Index rebuilt with {count:N0} entries before stopping."
            : $"Index rebuilt: {count:N0} entries.";
    }

}
