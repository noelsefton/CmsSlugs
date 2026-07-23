using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.PlugIn;
using EPiServer.Scheduler;
using EPiServer.Security;
using Mediachase.Commerce.Catalog;

namespace Website.Test.ScheduledJobs;

/// <summary>
/// "Delete Generated Products" — removes the harness category and everything beneath it so the
/// catalog can be regenerated from scratch.
///
/// Needed because a slug is baked into a product's RouteSegment when the product is created:
/// changing the generator only affects NEW products, and re-running generation over existing data
/// forces a disambiguating run token onto every code and slug. Reset order:
/// this job -> "Generate Random Products" -> "Rebuild Slug Index".
/// </summary>
[ScheduledPlugIn(
    DisplayName = "Delete Generated Products",
    Description = "Deletes the cmsslugs-harness category and all generated products, so slugs can be regenerated cleanly.",
    GUID = "C1A2B3D4-1001-4F00-9100-000000000011")]
public sealed class DeleteGeneratedProductsJob : ScheduledJobBase
{
    private readonly IContentRepository _content;
    private readonly ReferenceConverter _referenceConverter;

    public DeleteGeneratedProductsJob(IContentRepository content, ReferenceConverter referenceConverter)
    {
        _content = content;
        _referenceConverter = referenceConverter;
        IsStoppable = false;
    }

    public override string Execute()
    {
        var root = _referenceConverter.GetRootLink();
        if (ContentReference.IsNullOrEmpty(root))
            return "No catalog root found; nothing to delete.";

        var catalog = _content.GetChildren<CatalogContent>(root).FirstOrDefault();
        if (catalog is null)
            return "No catalog found; nothing to delete.";

        var category = _content.GetChildren<NodeContent>(catalog.ContentLink)
            .FirstOrDefault(n => string.Equals(n.Name, RandomProductGenerationJob.HarnessCategoryName,
                StringComparison.OrdinalIgnoreCase));

        if (category is null)
            return $"No '{RandomProductGenerationJob.HarnessCategoryName}' category found; nothing to delete.";

        // Delete the node itself — Commerce cascades to every entry beneath it, which is far
        // faster than enumerating and deleting each product. The generation job recreates it.
        OnStatusChanged($"Deleting '{category.Name}' and all generated products...");
        _content.Delete(category.ContentLink, forceDelete: true, access: AccessLevel.NoAccess);

        return $"Deleted '{RandomProductGenerationJob.HarnessCategoryName}' and everything under it. "
             + "Now run 'Generate Random Products', then 'Rebuild Slug Index'.";
    }
}
