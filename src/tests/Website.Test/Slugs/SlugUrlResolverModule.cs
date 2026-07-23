using System;
using CmsSlugs.Optimizely;
using EPiServer;
using EPiServer.Core;
using EPiServer.Core.Routing;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;

namespace Website.Test.Slugs;

/// <summary>
/// Routes bare root-level "/{slug}" URLs to catalog content through the normal pipeline.
///
/// A CMS 12 IPartialRouter only fires once content routing has matched a real node and left a
/// remaining path; a first-segment slug directly under the start page matches nothing, so the
/// partial router is never reached. Hooking <see cref="IContentUrlResolverEvents.ResolvingUrl"/>
/// lets CmsSlugs set the routed content BEFORE default routing, so the resolved content renders
/// through its normal template (e.g. GeneratedProductController) instead of 404ing.
/// </summary>
[InitializableModule]
public sealed class SlugUrlResolverModule : IInitializableModule
{
    private IContentUrlResolverEvents? _events;
    private SlugResolver? _resolver;
    private IContentLoader? _loader;

    public void Initialize(InitializationEngine context)
    {
        var services = context.Locate.Advanced;
        _events = services.GetInstance<IContentUrlResolverEvents>();
        _resolver = services.GetInstance<SlugResolver>();
        _loader = services.GetInstance<IContentLoader>();

        _events.ResolvingUrl += OnResolvingUrl;
    }

    public void Uninitialize(InitializationEngine context)
    {
        if (_events is not null)
            _events.ResolvingUrl -= OnResolvingUrl;
    }

    private void OnResolvingUrl(object? sender, UrlResolverEventArgs e)
    {
        // Runs on every url resolution: never let a slug lookup break routing.
        try
        {
            var ctx = e.Context;
            if (ctx.Content is not null) return;   // already resolved by an earlier step
            if (_resolver is null || !_resolver.IsReady) return;

            // Only single-segment slugs: multi-segment paths (edit UI, /util/..., real page trees)
            // are left to default routing.
            var slug = ctx.RemainingPath?.Trim('/');
            if (string.IsNullOrEmpty(slug) || slug.Contains('/')) return;

            var culture = ctx.RequestedLanguage?.Name ?? "en";
            var resolved = _resolver.TryResolve(slug, culture, out var hit);
            if (!resolved && !string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
                resolved = _resolver.TryResolve(slug, "en", out hit);   // index is keyed by branch ("en")
            if (!resolved) return;

            if (!_loader!.TryGet<IContent>(hit.ContentLink, out var content)) return;

            ctx.Content = content;
            ctx.RemainingPath = string.Empty;
            e.State = RoutingState.Done;
        }
        catch
        {
            // Swallow and fall through to default routing / the fallback resolver.
        }
    }
}
