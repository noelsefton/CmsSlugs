using CmsSlugs.Optimizely;
using EPiServer.Core.Routing;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;
using Microsoft.Extensions.DependencyInjection;

namespace Website.Test.Slugs;

/// <summary>
/// Wires CmsSlugs into the test site: the resolver + a store + the catalog slug source, plus the
/// partial router (in CMS 12, DI registration of <see cref="IPartialRouter"/> is the complete
/// registration — PartialRouteHandler receives all registered routers). Pick exactly ONE store line.
/// </summary>
[InitializableModule]
[ModuleDependency(typeof(EPiServer.Commerce.Initialization.InitializationModule))]
public sealed class CmsSlugsInitialization : IConfigurableModule
{
    public void ConfigureContainer(ServiceConfigurationContext context)
    {
        var services = context.Services;

        services.AddCmsSlugs();                                  // resolver + default in-memory store
        services.AddSingleton<ISlugSource, CatalogSlugSource>(); // rebuild from real catalog content
        services.AddSingleton<IPartialRouter, SlugPartialRouter>();

        // --- choose ONE durable store to actually exercise that package (else in-memory is used) ---
        // services.AddCmsSlugsSqlServer("<connection string>");
        // services.AddCmsSlugsRedis("localhost:6379");
    }

    public void Initialize(InitializationEngine context) { }

    public void Uninitialize(InitializationEngine context) { }
}
