using System.ComponentModel.DataAnnotations;
using EPiServer.Commerce.Catalog.ContentTypes;
using EPiServer.Commerce.Catalog.DataAnnotations;

namespace Website.Test.Models.Catalog;

/// <summary>
/// A catalog product created by the random-product scheduled job. Ordinary Commerce content —
/// nothing slug-specific here.
/// </summary>
[CatalogContentType(
    GUID = "3D9A1C7E-46B2-4F58-8A0D-92E5C1B7F304",
    MetaClassName = "GeneratedProduct",
    DisplayName = "Generated Product")]
public class GeneratedProduct : ProductContent
{
    [Display(Name = "Marketing title", Order = 10)]
    public virtual string? Title { get; set; }
}
