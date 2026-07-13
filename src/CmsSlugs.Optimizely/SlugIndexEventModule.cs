using EPiServer;
using EPiServer.Core;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.Logging;
using EPiServer.ServiceLocation;

namespace CmsSlugs.Optimizely;

/// <summary>
/// Keeps the index fresh on content change and seeds the in-memory store at startup.
/// Subscribes to <see cref="IContentEvents"/> and, on each change, removes the content's existing
/// slugs then re-sets whatever the source now produces. An empty source result (e.g. unpublished)
/// leaves the content removed. The startup boot scan that the spec assigns to an IHostedService
/// lives here instead, behind <see cref="ISlugStore.RequiresBootScan"/>, so a single code path
/// covers both CMS 11 (no generic host) and CMS 12.
/// </summary>
// No [ModuleDependency] on EPiServer.Web.InitializationModule: that type lives in different
// assemblies across CMS 11 (System.Web) and CMS 12 (AspNetCore) and is not in EPiServer.CMS.Core.
// Initializable modules run after the DI container is built, so IContentEvents/ISlugStore resolve
// fine here without it.
[InitializableModule]
public sealed class SlugIndexEventModule : IInitializableModule
{
    private static readonly ILogger Log = LogManager.GetLogger(typeof(SlugIndexEventModule));

    private IContentEvents? _events;
    private ISlugStore? _store;
    private ISlugSource? _source;

    public void Initialize(InitializationEngine context)
    {
        var services = context.Locate.Advanced;
        _events = services.GetInstance<IContentEvents>();
        _store = services.GetInstance<ISlugStore>();
        _source = services.GetInstance<ISlugSource>();

        // Route core's dependency-free warning seam into EPiServer logging.
        CmsSlugsLog.OnWarning ??= msg => Log.Warning(msg);

        _events.PublishedContent += OnChanged;
        _events.MovedContent += OnChanged;
        _events.DeletedContent += OnDeleted;

        // The in-memory store is empty every start: populate it off the startup thread.
        if (_store.RequiresBootScan)
            BootScan(_store, _source);
    }

    public void Uninitialize(InitializationEngine context)
    {
        if (_events is null) return;
        _events.PublishedContent -= OnChanged;
        _events.MovedContent -= OnChanged;
        _events.DeletedContent -= OnDeleted;
    }

    private static void BootScan(ISlugStore store, ISlugSource source)
    {
        Task.Run(() =>
        {
            try
            {
                store.Rebuild(source.GetAll());
                Log.Information($"CmsSlugs boot scan complete: {store.Count:N0} entries.");
            }
            catch (Exception ex)
            {
                Log.Error("CmsSlugs boot scan failed; resolver stays not-ready and the router falls back to Find.", ex);
            }
        });
    }

    private void OnChanged(object? sender, ContentEventArgs e)
    {
        if (_store is null || _source is null) return;

        var contentId = ContentIdCodec.Encode(e.ContentLink);

        // Always clear the old slugs first so a slug change / unpublish leaves no ghost entry.
        _store.RemoveByContent(contentId);

        if (e.Content is IContent content)
        {
            foreach (var entry in _source.GetForContent(content))
                _store.Set(entry);
        }
    }

    private void OnDeleted(object? sender, ContentEventArgs e)
    {
        if (_store is null) return;
        _store.RemoveByContent(ContentIdCodec.Encode(e.ContentLink));
    }
}
