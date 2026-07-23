using CmsSlugs;
using CmsSlugs.Optimizely;
using EPiServer.Commerce.Catalog.ContentTypes;
using Mediachase.Commerce.Catalog;

namespace Website.Test.Slugs;

/// <summary>
/// The bridge the harness tests: turns real Commerce catalog content into <see cref="SlugEntry"/>
/// values. GetContentLinks lists the catalog references for a full rebuild; Resolve reads one item
/// (read concurrently by the package after WarmUp primes the shared caches); GetForContent handles
/// one changed entry. There is deliberately no threading here — the source lists what to index,
/// warms the shared caches once, and resolves one item.
/// Slug = the entry's RouteSegment (what the random-product job set).
/// </summary>
public sealed class CatalogSlugSource : ISlugSource
{
    private readonly IContentRepository _content;
    private readonly ReferenceConverter _referenceConverter;
    private readonly ILanguageBranchRepository _languages;
    private readonly IServiceScopeFactory _scopeFactory;

    public CatalogSlugSource(
        IContentRepository content,
        ReferenceConverter referenceConverter,
        ILanguageBranchRepository languages,
        IServiceScopeFactory scopeFactory)
    {
        _content = content;
        _referenceConverter = referenceConverter;
        _languages = languages;
        _scopeFactory = scopeFactory;
    }

    // Catalog reads are thread-safe once the lazily-built shared caches are warm (a live Commerce
    // site serves concurrent requests against them). The cold-cache race that throws "non-concurrent
    // collection" is prevented by WarmUp below, so concurrent Resolve is safe and worth the speed-up.
    public bool SupportsConcurrentResolve => true;

    // Prime the lazily-populated shared caches that content materialization touches — the CMS
    // content-type model and the Commerce catalog metadata — on a single thread. Once warm, the
    // concurrent Resolve calls that follow only read them, so a parallel rebuild can't corrupt a
    // non-concurrent collection on a cold cache. Called once by the package before the parallel pass.
    public void WarmUp()
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        // Whole content-type model in one shot.
        sp.GetRequiredService<IContentTypeRepository>().List();
        // Materialize the enabled language branches.
        _ = _languages.ListEnabled().ToList();

        // Load the first real catalog entry end-to-end so the full materialization path is warm.
        var content = sp.GetRequiredService<IContentRepository>();
        foreach (var link in GetContentLinks())
        {
            if (content.TryGet<EntryContentBase>(link, out _))
                break;
        }
    }

    public IEnumerable<ContentReference> GetContentLinks()
    {
        var root = _referenceConverter.GetRootLink();
        if (ContentReference.IsNullOrEmpty(root))
            return Enumerable.Empty<ContentReference>();

        // GetDescendents returns references only (no content load), so listing is fast. It includes
        // catalogs/nodes/variations as well as products; Resolve filters those out (they contribute
        // no entries), so the indexed count is products even though the scan denominator is larger.
        return _content.GetDescendents(root);
    }

    public IEnumerable<SlugEntry> Resolve(ContentReference contentLink)
    {
        // Called concurrently after WarmUp has primed the shared caches. Take a fresh DI scope per
        // call for a cleanly-scoped content loader rather than the captured singleton one, and
        // materialize inside the scope before it is disposed.
        using var scope = _scopeFactory.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentRepository>();
        var languages = scope.ServiceProvider.GetRequiredService<ILanguageBranchRepository>();

        return content.TryGet<EntryContentBase>(contentLink, out var entry)
            ? ToEntries(content, languages, entry).ToList()
            : Enumerable.Empty<SlugEntry>();
    }

    public IEnumerable<SlugEntry> GetForContent(IContent content)
    {
        if (content is EntryContentBase entry)
            return ToEntries(_content, _languages, entry);
        return Enumerable.Empty<SlugEntry>();
    }

    private static IEnumerable<SlugEntry> ToEntries(
        IContentRepository content, ILanguageBranchRepository languages, EntryContentBase entry)
    {
        // Only products carry a deliberately-set RouteSegment (the random-product job sets it).
        // Variations get an auto-generated segment we never intend to route — and no controller
        // renders them, so an indexed variation slug resolves but then 404s. Skip them.
        if (entry is VariationContent)
            yield break;

        var contentId = ContentIdCodec.Encode(entry.ContentLink);

        // One entry per enabled language; RouteSegment is per-language on IRoutable content.
        foreach (var lang in languages.ListEnabled())
        {
            var localized = content.Get<EntryContentBase>(entry.ContentLink, lang.Culture);
            var slug = localized?.RouteSegment;
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            var data = new Dictionary<string, string> { ["title"] = localized!.DisplayName };
            yield return new SlugEntry(slug, lang.Culture.Name, contentId, data);
        }
    }
}
