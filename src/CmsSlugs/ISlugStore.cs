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

    /// <summary>
    /// The entry at ordinal <paramref name="index"/> in the index, or null when out of range. For
    /// display and diagnostics only — NOT the hot path. Ordering is unspecified but stable within a
    /// single call; do not treat the position as meaningful. Cost varies by store: in-memory and SQL
    /// are positional, but the Redis set is unordered so each access materializes the key set (O(N)).
    /// </summary>
    SlugEntry? this[long index] { get; }
}
