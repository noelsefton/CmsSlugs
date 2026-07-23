using System.ComponentModel.DataAnnotations;

namespace Website.Test.Models.Pages;

[ContentType(
    GUID = "8F4E9D2C-5A17-4B63-9E08-D14C7A3B6F52",
    DisplayName = "Start Page",
    Description = "Landing page for the CmsSlugs test harness.")]
public class StartPage : PageData
{
    [Display(Name = "Heading", Order = 10)]
    public virtual string? Heading { get; set; }
}
