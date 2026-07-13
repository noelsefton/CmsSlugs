namespace CmsSlugs;

/// <summary>
/// The unit of the index. CMS-neutral. One entry per (slug, culture).
/// Multiple slugs pointing at the same content are multiple entries sharing a <see cref="ContentId"/>.
/// </summary>
public sealed record SlugEntry(
    string Slug,
    string Culture,
    string ContentId,
    IReadOnlyDictionary<string, string>? Data = null);
