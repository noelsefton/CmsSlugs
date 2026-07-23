using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Commerce.Catalog.DataAnnotations;

namespace Website.Test.Models.Catalog;

/// <summary>
/// A catalog variation created by the random-product scheduled job. Ordinary Commerce content —
/// nothing slug-specific here.
/// </summary>
[CatalogContentType(
    GUID = "A52F8E19-7D03-4C6B-B1E4-68D0F9A2C735",
    MetaClassName = "GeneratedVariation",
    DisplayName = "Generated Variation")]
public class GeneratedVariation : VariationContent
{
}
