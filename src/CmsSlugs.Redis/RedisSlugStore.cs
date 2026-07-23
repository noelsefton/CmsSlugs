using StackExchange.Redis;

namespace CmsSlugs.Redis;

/// <summary>Options for <see cref="RedisSlugStore"/>.</summary>
public sealed class RedisSlugStoreOptions
{
    /// <summary>Key namespace prefix for all CmsSlugs keys.</summary>
    public string KeyPrefix { get; set; } = "slug:";
}

/// <summary>
/// Durable <see cref="ISlugStore"/> backed by Redis: a hash per slug. The reserved
/// <c>contentId</c> field carries the identity; the remaining fields are the Data dictionary.
/// An index set tracks all slug keys (for Count / readiness) and a reverse set per content backs
/// <see cref="RemoveByContent"/> without scanning.
/// </summary>
public sealed class RedisSlugStore : ISlugStore
{
    private const string ContentIdField = "contentId";

    private readonly IDatabase _db;
    private readonly string _prefix;
    private readonly string _indexKey;
    private volatile bool _hasData;

    public RedisSlugStore(IConnectionMultiplexer connection, RedisSlugStoreOptions? options = null)
    {
        if (connection is null) throw new ArgumentNullException(nameof(connection));
        _prefix = options?.KeyPrefix ?? "slug:";
        _db = connection.GetDatabase();
        _indexKey = $"{_prefix}__keys";
        _hasData = _db.SetLength(_indexKey) > 0;
    }

    public bool IsReady => _hasData;

    public bool RequiresBootScan => false;

    public long Count => _db.SetLength(_indexKey);

    public bool TryResolve(string slug, string culture, out SlugEntry? entry)
    {
        entry = null;
        var c = SlugKey.NormalizeCulture(culture);
        var s = SlugKey.NormalizeSlug(slug);
        var key = SlugRedisKey(c, s);

        var fields = _db.HashGetAll(key);
        if (fields.Length == 0)
            return false;

        string? contentId = null;
        var data = new Dictionary<string, string>(fields.Length);
        foreach (var f in fields)
        {
            var name = f.Name.ToString();
            if (name == ContentIdField)
                contentId = f.Value.ToString();
            else
                data[name] = f.Value.ToString();
        }

        if (contentId is null)
            return false;

        entry = new SlugEntry(s, c, contentId, data.Count > 0 ? data : null);
        return true;
    }

    public void Set(SlugEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        var c = SlugKey.NormalizeCulture(entry.Culture);
        var s = SlugKey.NormalizeSlug(entry.Slug);
        var key = SlugRedisKey(c, s);

        // If the key already pointed at a different content, detach it from that reverse set.
        var existing = _db.HashGet(key, ContentIdField);
        if (existing.HasValue && existing.ToString() != entry.ContentId)
            _db.SetRemove(ReverseKey(existing.ToString()), key.ToString());

        WriteEntry(key, entry);

        _db.SetAdd(_indexKey, key.ToString());
        _db.SetAdd(ReverseKey(entry.ContentId), key.ToString());
        _hasData = true;
    }

    public void RemoveByContent(string contentId)
    {
        if (string.IsNullOrEmpty(contentId)) return;
        var reverseKey = ReverseKey(contentId);

        var keys = _db.SetMembers(reverseKey);
        foreach (var k in keys)
        {
            _db.KeyDelete((string)k!);
            _db.SetRemove(_indexKey, k);
        }
        _db.KeyDelete(reverseKey);
        _hasData = _db.SetLength(_indexKey) > 0;
    }

    public void Rebuild(IEnumerable<SlugEntry> entries)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));

        // Materialize the desired end state in memory with last-write-wins per (slug, culture) key,
        // so each key has exactly one owner (matching the in-memory store). This lets the whole load
        // ship as pipelined batches instead of ~5 synchronous round-trips per entry — the latter
        // parks a durable rebuild at "building 100%" for the entire write on a large catalog.
        var forward = new Dictionary<string, SlugEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var c = SlugKey.NormalizeCulture(entry.Culture);
            var slug = SlugKey.NormalizeSlug(entry.Slug);
            forward[SlugRedisKey(c, slug).ToString()!] = entry; // last write wins
        }

        // Clear the previous keyspace (slug hashes + reverse sets + index) in one pipelined batch.
        // Snapshot the existing slug keys once, then derive their reverse-set keys with a pipelined
        // read — reading contentId one key at a time is a round-trip per key, the slow tail that a
        // re-run spends AFTER the read phase already shows 100%.
        var oldKeys = _db.SetMembers(_indexKey);
        var oldReverseKeys = ReverseKeysFor(oldKeys);

        var wipe = _db.CreateBatch();
        var wipeTasks = new List<Task>(oldKeys.Length + oldReverseKeys.Count + 1);
        foreach (var k in oldKeys) wipeTasks.Add(wipe.KeyDeleteAsync((string)k!));
        foreach (var rk in oldReverseKeys) wipeTasks.Add(wipe.KeyDeleteAsync(rk));
        wipeTasks.Add(wipe.KeyDeleteAsync(_indexKey));
        wipe.Execute();
        Task.WaitAll(wipeTasks.ToArray());

        // Load the fresh state in pipelined batches: one hash per slug, the index set, and a reverse
        // set per owner built from the final (deduped) ownership. Chunked so a huge catalog doesn't
        // buffer millions of pending commands at once.
        const int chunkSize = 5000;
        var indexMembers = new List<RedisValue>(forward.Count);
        var reverse = new Dictionary<string, List<RedisValue>>(StringComparer.Ordinal);

        IBatch load = _db.CreateBatch();
        var loadTasks = new List<Task>(chunkSize * 2);
        var pending = 0;
        foreach (var kv in forward)
        {
            RedisKey key = kv.Key;
            var entry = kv.Value;

            loadTasks.Add(load.KeyDeleteAsync(key));               // replace wholesale; no stale fields
            loadTasks.Add(load.HashSetAsync(key, BuildHash(entry)));
            indexMembers.Add(kv.Key);

            if (!reverse.TryGetValue(entry.ContentId, out var list))
                reverse[entry.ContentId] = list = new List<RedisValue>();
            list.Add(kv.Key);

            if (++pending >= chunkSize)
            {
                load.Execute();
                Task.WaitAll(loadTasks.ToArray());
                load = _db.CreateBatch();
                loadTasks.Clear();
                pending = 0;
            }
        }

        // Index + reverse sets: write the memberships in a final batch.
        if (indexMembers.Count > 0)
            loadTasks.Add(load.SetAddAsync(_indexKey, indexMembers.ToArray()));
        foreach (var kv in reverse)
            loadTasks.Add(load.SetAddAsync(ReverseKey(kv.Key), kv.Value.ToArray()));
        load.Execute();
        Task.WaitAll(loadTasks.ToArray());

        _hasData = forward.Count > 0;
    }

    public SlugEntry? this[long index]
    {
        get
        {
            if (index < 0) return null;

            // A Redis set is unordered, so positional access materializes the key set (display-only,
            // not the hot path). SetMembers is a stable snapshot within this call.
            var members = _db.SetMembers(_indexKey);
            if (index >= members.Length) return null;

            var key = members[(int)index].ToString();
            if (!TryParseKey(key, out var culture, out var slug)) return null;

            var fields = _db.HashGetAll(key);
            if (fields.Length == 0) return null;

            string? contentId = null;
            var data = new Dictionary<string, string>(fields.Length);
            foreach (var f in fields)
            {
                var name = f.Name.ToString();
                if (name == ContentIdField)
                    contentId = f.Value.ToString();
                else
                    data[name] = f.Value.ToString();
            }
            if (contentId is null) return null;

            return new SlugEntry(slug, culture, contentId, data.Count > 0 ? data : null);
        }
    }

    // Recover (culture, slug) from a slug key "{prefix}{culture}:{slug}". Culture never contains ':'.
    private bool TryParseKey(string key, out string culture, out string slug)
    {
        culture = slug = string.Empty;
        if (!key.StartsWith(_prefix, StringComparison.Ordinal)) return false;
        var rest = key.Substring(_prefix.Length);
        var idx = rest.IndexOf(':');
        if (idx < 0) return false;
        culture = rest.Substring(0, idx);
        slug = rest.Substring(idx + 1);
        return true;
    }

    private void WriteEntry(RedisKey key, SlugEntry entry)
    {
        // Replace the hash wholesale so stale Data fields from a prior value don't linger.
        _db.KeyDelete(key);
        _db.HashSet(key, BuildHash(entry));
    }

    private static HashEntry[] BuildHash(SlugEntry entry)
    {
        var hash = new List<HashEntry>(1 + (entry.Data?.Count ?? 0))
        {
            new(ContentIdField, entry.ContentId)
        };
        if (entry.Data is not null)
        {
            foreach (var kv in entry.Data)
            {
                if (kv.Key == ContentIdField) continue; // reserved field name
                hash.Add(new HashEntry(kv.Key, kv.Value));
            }
        }
        return hash.ToArray();
    }

    // The reverse-set keys owning the given slug keys, found by reading each key's contentId field
    // in a single pipelined batch rather than one blocking round-trip per key.
    private List<RedisKey> ReverseKeysFor(RedisValue[] slugKeys)
    {
        if (slugKeys.Length == 0)
            return new List<RedisKey>(0);

        var batch = _db.CreateBatch();
        var reads = new List<Task<RedisValue>>(slugKeys.Length);
        foreach (var k in slugKeys)
            reads.Add(batch.HashGetAsync((string)k!, ContentIdField));
        batch.Execute();
        Task.WaitAll(reads.ToArray());

        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in reads)
            if (r.Result.HasValue)
                owners.Add(r.Result.ToString());

        var keys = new List<RedisKey>(owners.Count);
        foreach (var owner in owners)
            keys.Add(ReverseKey(owner));
        return keys;
    }

    private RedisKey SlugRedisKey(string culture, string slug) => $"{_prefix}{culture}:{slug}";

    private RedisKey ReverseKey(string contentId) => $"{_prefix}__content:{contentId}";
}
