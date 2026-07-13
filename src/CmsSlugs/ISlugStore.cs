namespace CmsSlugs;

/// <summary>
/// The store contract, implemented by the in-memory store (core) and by each storage provider.
/// Synchronous on purpose: <c>IPartialRouter</c> is synchronous and a sync-over-async hop on the
/// hot path is to be avoided.
/// </summary>
public interface ISlugStore
{
    /// <summary>False =&gt; the router should fall back to Find (boot window / empty durable store).</summary>
    bool IsReady { get; }

    /// <summary>Number of (slug, culture) entries currently held.</summary>
    long Count { get; }

    /// <summary>True for in-memory (rebuilt each start); false for durable stores.</summary>
    bool RequiresBootScan { get; }

    /// <summary>Hot path. Returns the whole entry (ContentId + Data). Miss =&gt; caller falls back.</summary>
    bool TryResolve(string slug, string culture, out SlugEntry? entry);

    /// <summary>Add or overwrite a single (slug, culture) mapping.</summary>
    void Set(SlugEntry entry);

    /// <summary>Remove every slug pointing at this content (uses a reverse map).</summary>
    void RemoveByContent(string contentId);

    /// <summary>Full, atomic replacement of the index. Marks the store ready.</summary>
    void Rebuild(IEnumerable<SlugEntry> entries);
}
