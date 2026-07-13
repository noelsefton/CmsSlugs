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

        // Clear the existing keyspace using the index set, then load fresh.
        // Gather reverse-set keys BEFORE deleting the slug hashes they are derived from.
        var reverseKeys = EnumerateContentReverseKeys().ToList();
        foreach (var k in _db.SetMembers(_indexKey))
            _db.KeyDelete((string)k!);
        foreach (var rk in reverseKeys)
            _db.KeyDelete(rk);
        _db.KeyDelete(_indexKey);

        long count = 0;
        foreach (var entry in entries)
        {
            Set(entry);
            count++;
        }
        _hasData = count > 0;
    }

    private void WriteEntry(RedisKey key, SlugEntry entry)
    {
        // Replace the hash wholesale so stale Data fields from a prior value don't linger.
        _db.KeyDelete(key);

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
        _db.HashSet(key, hash.ToArray());
    }

    private IEnumerable<RedisKey> EnumerateContentReverseKeys()
    {
        // Each slug entry records its content, so gather owners from the entries still referenced
        // by the index set before it is cleared.
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in _db.SetMembers(_indexKey))
        {
            var owner = _db.HashGet((string)k!, ContentIdField);
            if (owner.HasValue)
                owners.Add(owner.ToString());
        }
        foreach (var owner in owners)
            yield return ReverseKey(owner);
    }

    private RedisKey SlugRedisKey(string culture, string slug) => $"{_prefix}{culture}:{slug}";

    private RedisKey ReverseKey(string contentId) => $"{_prefix}__content:{contentId}";
}
