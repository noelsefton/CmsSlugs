using System.Globalization;
using CmsSlugs.Optimizely;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Core.Routing;
using EPiServer.Core.Routing.Pipeline;
using Website.Test.Models.Pages;

namespace Website.Test.Slugs;

/// <summary>
/// CMS 12 partial router that resolves a slug via CmsSlugs instead of a Find query. Falls through
/// (null) during the boot window or on an index miss, so behaviour degrades to a plain 404.
/// </summary>
public sealed class SlugPartialRouter : IPartialRouter<StartPage, CatalogContentBase>
{
    private readonly SlugResolver _resolver;
    private readonly IContentLoader _contentLoader;

    public SlugPartialRouter(SlugResolver resolver, IContentLoader contentLoader)
    {
        _resolver = resolver;
        _contentLoader = contentLoader;
    }

    public object? RoutePartial(StartPage content, UrlResolverContext context)
    {
        var segment = context.GetNextSegment();
        if (segment.Next.IsEmpty)
            return null;

        if (!_resolver.IsReady)
            return null; // boot window: fall back to the next router / 404

        var slug = segment.Next.ToString();
        var culture = context.RequestedLanguage?.Name
                      ?? context.ContentLanguage?.Name
                      ?? CultureInfo.CurrentUICulture.Name;

        if (_resolver.TryResolve(slug, culture, out var hit)
            && _contentLoader.TryGet<CatalogContentBase>(hit.ContentLink, out var target))
        {
            context.RemainingSegments = segment.Remaining;
            return target;
        }

        return null; // miss -> next router / 404
    }

    public PartialRouteData? GetPartialVirtualPath(CatalogContentBase content, UrlGeneratorContext context)
        => null; // URL generation is out of scope for the harness
}
