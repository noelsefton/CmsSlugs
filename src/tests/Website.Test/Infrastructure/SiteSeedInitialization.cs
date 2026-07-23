using System.Globalization;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.DataAccess;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.Security;
using EPiServer.ServiceLocation;
using EPiServer.Web;
using Mediachase.Commerce.Catalog;
using Website.Test.Models.Pages;

namespace Website.Test.Infrastructure;

/// <summary>
/// Seeds the empty scaffold on first run so the site is usable without manual admin steps:
/// a published start page assigned to the site definition, and one Commerce catalog for the
/// random-product job to fill. Idempotent — re-runs are no-ops.
/// </summary>
[InitializableModule]
[ModuleDependency(typeof(EPiServer.Commerce.Initialization.InitializationModule))]
public sealed class SiteSeedInitialization : IInitializableModule
{
    public void Initialize(InitializationEngine context)
    {
        var repo = context.Locate.Advanced.GetInstance<IContentRepository>();

        var startLink = EnsureStartPage(repo);
        EnsureSiteDefinition(context, startLink);
        EnsureCatalog(context, repo);
    }

    public void Uninitialize(InitializationEngine context) { }

    private static ContentReference EnsureStartPage(IContentRepository repo)
    {
        var existing = repo.GetChildren<StartPage>(ContentReference.RootPage).FirstOrDefault();
        if (existing is not null)
            return existing.ContentLink;

        var page = repo.GetDefault<StartPage>(ContentReference.RootPage);
        page.Name = "Start";
        page.Heading = "CmsSlugs harness";
        return repo.Save(page, SaveAction.Publish, AccessLevel.NoAccess);
    }

    private static void EnsureSiteDefinition(InitializationEngine context, ContentReference startLink)
    {
        var sites = context.Locate.Advanced.GetInstance<ISiteDefinitionRepository>();
        var site = sites.List().FirstOrDefault();

        if (site is null)
        {
            site = new SiteDefinition
            {
                Name = "Website.Test",
                SiteUrl = new Uri("https://localhost:5000/"),
                StartPage = startLink,
            };
            site.Hosts.Add(new HostDefinition { Name = HostDefinition.WildcardHostName });
            sites.Save(site);
        }
        else if (ContentReference.IsNullOrEmpty(site.StartPage))
        {
            var clone = site.CreateWritableClone();
            clone.StartPage = startLink;
            sites.Save(clone);
        }
    }

    private static void EnsureCatalog(InitializationEngine context, IContentRepository repo)
    {
        var referenceConverter = context.Locate.Advanced.GetInstance<ReferenceConverter>();
        var root = referenceConverter.GetRootLink();
        if (repo.GetChildren<CatalogContent>(root).Any())
            return;

        var catalog = repo.GetDefault<CatalogContent>(root, CultureInfo.GetCultureInfo("en"));
        catalog.Name = "Harness Catalog";
        catalog.DefaultLanguage = "en";
        catalog.DefaultCurrency = "USD";
        catalog.WeightBase = "kgs";
        catalog.LengthBase = "cm";
        repo.Save(catalog, SaveAction.Publish, AccessLevel.NoAccess);
    }
}
