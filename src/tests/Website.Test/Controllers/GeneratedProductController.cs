using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using Website.Test.Models.Catalog;

namespace Website.Test.Controllers;

/// <summary>
/// Renders a resolved product. Reaching this controller proves the URL was resolved to real
/// catalog content through CmsSlugs (the ResolvingUrl hook) and rendered by the normal template
/// pipeline — a proper view, not the plain-text fallback.
/// </summary>
public class GeneratedProductController : ContentController<GeneratedProduct>
{
    public IActionResult Index(GeneratedProduct currentContent) => View(currentContent);
}
