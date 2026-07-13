using EPiServer.Core;

namespace CmsSlugs.Optimizely;

/// <summary>The outcome of resolving a slug: the target reference plus any carried data.</summary>
public readonly struct SlugResolution
{
    public SlugResolution(ContentReference contentLink, IReadOnlyDictionary<string, string> data)
    {
        ContentLink = contentLink;
        Data = data;
    }

    public ContentReference ContentLink { get; }
    public IReadOnlyDictionary<string, string> Data { get; }
}

/// <summary>
/// Wraps <see cref="ISlugStore"/> for the partial router: builds the key, runs the lookup and
/// translates the neutral <c>ContentId</c> back into a <see cref="ContentReference"/>.
/// </summary>
public sealed class SlugResolver
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(0);

    private readonly ISlugStore _store;

    public SlugResolver(ISlugStore store) => _store = store;

    /// <summary>False during the boot window / empty durable store — caller should fall back to Find.</summary>
    public bool IsReady => _store.IsReady;

    /// <summary>Resolve (slug, culture) to a content reference plus data. Miss =&gt; false.</summary>
    public bool TryResolve(string slug, string culture, out SlugResolution resolution)
    {
        resolution = default;

        if (!_store.TryResolve(slug, culture, out var entry) || entry is null)
            return false;

        if (!ContentIdCodec.TryDecode(entry.ContentId, out var link))
            return false;

        resolution = new SlugResolution(link, entry.Data ?? Empty);
        return true;
    }
}
