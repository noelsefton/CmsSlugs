using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.DataAccess;
using EPiServer.PlugIn;
using EPiServer.Scheduler;
using EPiServer.Security;
using Mediachase.Commerce.Catalog;
using Website.Test.Models.Catalog;

namespace Website.Test.ScheduledJobs;

/// <summary>
/// "Generate Random Products" — seeds the real Commerce catalog with N random products so the
/// CmsSlugs index can be rebuilt from actual CMS content (not synthetic SlugEntry data).
///
/// Run it once (or repeatedly) to grow the catalog, then run "Rebuild Slug Index" to populate the
/// store under test.
/// </summary>
[ScheduledPlugIn(
    DisplayName = "Generate Random Products",
    Description = "Creates random Commerce products for CmsSlugs scale testing.",
    GUID = "C1A2B3D4-1001-4F00-9100-000000000010")]
public sealed class RandomProductGenerationJob : ScheduledJobBase
{
    // ---- knobs: tweak before compiling a run -----------------------------------------------
    private const int Seed = 12345;
    private const int ProductCount = 1_00_000;    // bump to 10_000 / 100_000 for scale runs
    private const string SkuPrefix = "PRD";    // entry codes must be unique across the whole
                                               // catalog, so pick a fresh prefix per run (e.g.
                                               // "RUN2"). On a collision the job automatically
                                               // switches to a random prefix and continues.
    internal const string HarnessCategoryName = "cmsslugs-harness";
    // ----------------------------------------------------------------------------------------

    private const int MaxCollisionRetries = 5;

    private readonly IContentRepository _content;
    private readonly ReferenceConverter _referenceConverter;
    private string _runToken = "";   // set to a random hex token on first collision
    private bool _stop;

    public RandomProductGenerationJob(IContentRepository content, ReferenceConverter referenceConverter)
    {
        _content = content;
        _referenceConverter = referenceConverter;
        IsStoppable = true;
    }

    public override void Stop() => _stop = true;

    public override string Execute()
    {
        _stop = false;
        _runToken = "";

        var catalog = FirstCatalog();
        if (ContentReference.IsNullOrEmpty(catalog))
            return "No catalog found. Create a catalog first (Commerce admin) and re-run.";

        var category = GetOrCreateCategory(catalog, HarnessCategoryName);

        var options = new RandomCatalogOptions
        {
            ProductCount = ProductCount,
            Cultures = new[] { "en" },
            AliasFraction = 0.0,   // aliases need extra SeoUri handling; keep off for catalog seeding
            DeepPathFraction = 0.0 // RouteSegment is single-segment; deep paths are for store-level tests
        };
        var generator = new RandomCatalogData(Seed, options);

        long created = 0;
        // contentIdFactory is unused for catalog seeding (Commerce assigns the id on Save).
        foreach (var spec in generator.Stream(i => i.ToString(CultureInfo.InvariantCulture)))
        {
            if (_stop) break;

            CreateProduct(category, spec);
            created++;

            if (created % 100 == 0)
                OnStatusChanged($"Created {created:N0} products...");
        }

        return _stop
            ? $"Stopped after creating {created:N0} products."
            : $"Created {created:N0} products under '{HarnessCategoryName}'"
              + (_runToken.Length == 0 ? "." : $" (run token '{_runToken}').");
    }

    private void CreateProduct(ContentReference parentCategory, ProductSpec spec)
    {
        for (var attempt = 0; ; attempt++)
        {
            var code = SkuPrefix + _runToken + spec.Sku["SKU".Length..];
            var slug = RandomCatalogData.Slugify(spec.Slug);
            if (_runToken.Length > 0)
                slug += "-" + _runToken.ToLowerInvariant();

            // Deterministic seed => re-runs regenerate the same codes/slugs. If this code already
            // exists (previous run), switch to a random guid-derived token; every subsequent
            // product uses it too, so this check only trips once per run.
            if (attempt < MaxCollisionRetries
                && !ContentReference.IsNullOrEmpty(_referenceConverter.GetContentLink(code)))
            {
                _runToken = NewRunToken();
                OnStatusChanged($"'{code}' already exists; switching to run token '{_runToken}'.");
                continue;
            }

            try
            {
                var product = _content.GetDefault<GeneratedProduct>(parentCategory);
                product.Name = spec.DisplayName;
                product.Title = spec.DisplayName;
                product.RouteSegment = slug; // the slug the router resolves
                product.Code = code;
                _content.Save(product, SaveAction.Publish, AccessLevel.NoAccess);

                // A single variation gives the product something purchasable; optional for routing tests.
                var variation = _content.GetDefault<GeneratedVariation>(product.ContentLink);
                variation.Name = spec.DisplayName + " (variant)";
                variation.Code = code + "-V";
                _content.Save(variation, SaveAction.Publish, AccessLevel.NoAccess);
                return;
            }
            catch (ValidationException) when (attempt < MaxCollisionRetries)
            {
                // Backstop for collisions the existence check can't see (e.g. slug taken by
                // something outside this job).
                _runToken = NewRunToken();
                OnStatusChanged($"Code/slug collision; switching to run token '{_runToken}'.");
            }
        }
    }

    private static string NewRunToken() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private ContentReference FirstCatalog()
    {
        var root = _referenceConverter.GetRootLink();
        var catalog = _content.GetChildren<CatalogContent>(root).FirstOrDefault();
        return catalog?.ContentLink ?? ContentReference.EmptyReference;
    }

    private ContentReference GetOrCreateCategory(ContentReference catalog, string name)
    {
        var existing = _content.GetChildren<NodeContent>(catalog)
            .FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing.ContentLink;

        var node = _content.GetDefault<NodeContent>(catalog);
        node.Name = name;
        node.RouteSegment = name;
        node.Code = name;
        return _content.Save(node, SaveAction.Publish, AccessLevel.NoAccess);
    }
}
