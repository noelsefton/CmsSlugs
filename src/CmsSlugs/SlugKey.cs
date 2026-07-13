namespace CmsSlugs;

/// <summary>
/// The one place the lookup-key format lives. All store reads and writes route through this so
/// reads and writes always agree.
/// </summary>
public static class SlugKey
{
    /// <summary>Trim, strip leading/trailing slashes, lower-case (invariant).</summary>
    public static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrEmpty(slug))
            return string.Empty;

        var s = slug.Trim().Trim('/');
        return s.ToLowerInvariant();
    }

    /// <summary>Lower-case (invariant) and trim the culture token.</summary>
    public static string NormalizeCulture(string? culture)
        => string.IsNullOrWhiteSpace(culture) ? string.Empty : culture.Trim().ToLowerInvariant();

    /// <summary>Compose the composite key: "{culture}|{normalizedSlug}".</summary>
    public static string Compose(string slug, string? culture)
        => $"{NormalizeCulture(culture)}|{NormalizeSlug(slug)}";
}
