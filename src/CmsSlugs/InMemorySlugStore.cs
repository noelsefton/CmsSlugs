using System.Collections.Concurrent;

namespace CmsSlugs;

/// <summary>
/// The default <see cref="ISlugStore"/>, used when no storage provider is installed.
/// Forward map (key -&gt; entry) plus a reverse map (contentId -&gt; keys) for clean re-index and
/// removal. Rebuild swaps the whole state atomically so readers never observe a half-built index.
/// </summary>
public sealed class InMemorySlugStore : ISlugStore
{
    private sealed class State
    {
        public readonly ConcurrentDictionary<string, SlugEntry> Forward =
            new(StringComparer.Ordinal);

        // contentId -> set of keys it owns. Inner value bag is a dictionary used as a set.
        public readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> Reverse =
            new(StringComparer.Ordinal);
    }

    private volatile State _state = new();
    private volatile bool _isReady;

    public bool IsReady => _isReady;

    public long Count => _state.Forward.Count;

    public bool RequiresBootScan => true;

    public bool TryResolve(string slug, string culture, out SlugEntry? entry)
    {
        var key = SlugKey.Compose(slug, culture);
        return _state.Forward.TryGetValue(key, out entry);
    }

    public void Set(SlugEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        SetInternal(_state, entry, logCollision: true);
    }

    public void RemoveByContent(string contentId)
    {
        if (string.IsNullOrEmpty(contentId)) return;
        RemoveByContentInternal(_state, contentId);
    }

    public void Rebuild(IEnumerable<SlugEntry> entries)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));

        var fresh = new State();
        foreach (var entry in entries)
            SetInternal(fresh, entry, logCollision: false);

        // Publish the new state atomically; readers see either the old or the new, never a mix.
        _state = fresh;
        _isReady = true;
    }

    public SlugEntry? this[long index]
    {
        get
        {
            if (index < 0) return null;
            return _state.Forward.Values.Skip((int)index).FirstOrDefault();
        }
    }

    private static void SetInternal(State state, SlugEntry entry, bool logCollision)
    {
        var key = SlugKey.Compose(entry.Slug, entry.Culture);

        // If this key already belonged to a different content, detach it from that owner so the
        // reverse map stays accurate (collision-safe: removing the old owner later won't drop this key).
        if (state.Forward.TryGetValue(key, out var existing) &&
            !string.Equals(existing.ContentId, entry.ContentId, StringComparison.Ordinal))
        {
            DetachKeyFromOwner(state, existing.ContentId, key);
            if (logCollision)
                CmsSlugsLog.CollisionLastWriteWins(key, existing.ContentId, entry.ContentId);
        }

        state.Forward[key] = entry;

        var keys = state.Reverse.GetOrAdd(entry.ContentId,
            _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        keys[key] = 0;
    }

    private static void RemoveByContentInternal(State state, string contentId)
    {
        if (!state.Reverse.TryRemove(contentId, out var keys))
            return;

        foreach (var key in keys.Keys)
        {
            // Only remove the forward entry if this content still owns it (another content may have
            // re-claimed the key since).
            if (state.Forward.TryGetValue(key, out var current) &&
                string.Equals(current.ContentId, contentId, StringComparison.Ordinal))
            {
                state.Forward.TryRemove(key, out _);
            }
        }
    }

    private static void DetachKeyFromOwner(State state, string ownerContentId, string key)
    {
        if (state.Reverse.TryGetValue(ownerContentId, out var keys))
        {
            keys.TryRemove(key, out _);
            if (keys.IsEmpty)
                state.Reverse.TryRemove(ownerContentId, out _);
        }
    }
}
