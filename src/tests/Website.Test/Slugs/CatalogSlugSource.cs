using CmsSlugs;
using CmsSlugs.Optimizely;
using EPiServer.Commerce.Catalog.ContentTypes;
using Mediachase.Commerce.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace Website.Test.Slugs;

/// <summary>
/// The bridge the harness tests: turns real Commerce catalog content into <see cref="SlugEntry"/>
/// values. GetContentLinks lists the catalog references for a full rebuild; Resolve reads one item
/// (called concurrently by the package); GetForContent handles one changed entry. There is
/// deliberately no threading here — the source only lists what to index and resolves one item.
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
        // Called concurrently by SlugIndexBuilder. EPiServer's content-loading pipeline has scoped,
        // non-thread-safe pieces, so each concurrent call gets its OWN DI scope and loader rather
        // than sharing the captured singleton one. Materialize inside the scope before disposal.
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
