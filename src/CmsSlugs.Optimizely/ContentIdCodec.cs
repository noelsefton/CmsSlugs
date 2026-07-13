using EPiServer.Core;

namespace CmsSlugs.Optimizely;

/// <summary>
/// Encodes an Optimizely identity into the neutral string <c>ContentId</c> at the edge, and parses
/// it back. Format: <c>"{ID}|{ProviderName}"</c> (ProviderName empty for the default provider).
/// </summary>
public static class ContentIdCodec
{
    /// <summary>Encode a <see cref="ContentReference"/> into the neutral content id.</summary>
    public static string Encode(ContentReference link)
    {
        if (ContentReference.IsNullOrEmpty(link))
            throw new ArgumentException("Content reference is null or empty.", nameof(link));

        return $"{link.ID}|{link.ProviderName ?? string.Empty}";
    }

    /// <summary>Parse a neutral content id back into a <see cref="ContentReference"/>.</summary>
    public static bool TryDecode(string contentId, out ContentReference link)
    {
        link = ContentReference.EmptyReference;
        if (string.IsNullOrEmpty(contentId))
            return false;

        var sep = contentId.IndexOf('|');
        var idPart = sep >= 0 ? contentId.Substring(0, sep) : contentId;
        var provider = sep >= 0 ? contentId.Substring(sep + 1) : string.Empty;

        if (!int.TryParse(idPart, out var id))
            return false;

        link = string.IsNullOrEmpty(provider)
            ? new ContentReference(id)
            : new ContentReference(id, provider);
        return true;
    }
}
