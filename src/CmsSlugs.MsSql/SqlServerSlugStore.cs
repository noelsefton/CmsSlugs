using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace CmsSlugs.MsSql;

/// <summary>Options for <see cref="SqlServerSlugStore"/>.</summary>
public sealed class SqlServerSlugStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string TableName { get; set; } = "dbo.CmsSlugsEntries";
}

/// <summary>
/// Durable <see cref="ISlugStore"/> over SQL Server. One row per (Culture, Slug); Data is a JSON
/// column round-tripped whole. Resolve is a single indexed select.
/// </summary>
public sealed class SqlServerSlugStore : ISlugStore
{
    private readonly string _connectionString;
    private readonly string _table;
    private volatile bool _hasData;

    public SqlServerSlugStore(SqlServerSlugStoreOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("ConnectionString is required.", nameof(options));

        _connectionString = options.ConnectionString;
        _table = options.TableName;
        _hasData = Count > 0;
    }

    public bool IsReady => _hasData;

    public bool RequiresBootScan => false;

    public long Count
    {
        get
        {
            using var conn = Open();
            using var cmd = new SqlCommand($"SELECT COUNT_BIG(*) FROM {_table}", conn);
            return (long)cmd.ExecuteScalar();
        }
    }

    public bool TryResolve(string slug, string culture, out SlugEntry? entry)
    {
        entry = null;
        var c = SlugKey.NormalizeCulture(culture);
        var s = SlugKey.NormalizeSlug(slug);

        using var conn = Open();
        using var cmd = new SqlCommand(
            $"SELECT ContentId, Data FROM {_table} WHERE Culture = @c AND Slug = @s", conn);
        cmd.Parameters.Add(new SqlParameter("@c", c));
        cmd.Parameters.Add(new SqlParameter("@s", s));

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return false;

        var contentId = reader.GetString(0);
        var data = reader.IsDBNull(1) ? null : DeserializeData(reader.GetString(1));
        entry = new SlugEntry(s, c, contentId, data);
        return true;
    }

    public void Set(SlugEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        using var conn = Open();
        SetInternal(conn, null, entry);
        _hasData = true;
    }

    public void RemoveByContent(string contentId)
    {
        if (string.IsNullOrEmpty(contentId)) return;
        using var conn = Open();
        using var cmd = new SqlCommand($"DELETE FROM {_table} WHERE ContentId = @id", conn);
        cmd.Parameters.Add(new SqlParameter("@id", contentId));
        cmd.ExecuteNonQuery();
    }

    public void Rebuild(IEnumerable<SlugEntry> entries)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var truncate = new SqlCommand($"DELETE FROM {_table}", conn, tx))
                truncate.ExecuteNonQuery();

            long count = 0;
            foreach (var entry in entries)
            {
                SetInternal(conn, tx, entry);
                count++;
            }

            tx.Commit();
            _hasData = count > 0;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private void SetInternal(SqlConnection conn, SqlTransaction? tx, SlugEntry entry)
    {
        var c = SlugKey.NormalizeCulture(entry.Culture);
        var s = SlugKey.NormalizeSlug(entry.Slug);
        var json = entry.Data is { Count: > 0 } ? JsonSerializer.Serialize(entry.Data) : null;

        // Upsert keyed on the (Culture, Slug) primary key.
        const string sql = @"
MERGE {0} WITH (HOLDLOCK) AS target
USING (SELECT @c AS Culture, @s AS Slug) AS source
    ON target.Culture = source.Culture AND target.Slug = source.Slug
WHEN MATCHED THEN UPDATE SET ContentId = @id, Data = @data
WHEN NOT MATCHED THEN INSERT (Culture, Slug, ContentId, Data) VALUES (@c, @s, @id, @data);";

        using var cmd = new SqlCommand(string.Format(sql, _table), conn, tx);
        cmd.Parameters.Add(new SqlParameter("@c", c));
        cmd.Parameters.Add(new SqlParameter("@s", s));
        cmd.Parameters.Add(new SqlParameter("@id", entry.ContentId));
        cmd.Parameters.Add(new SqlParameter("@data", (object?)json ?? DBNull.Value));
        cmd.ExecuteNonQuery();
    }

    private SqlConnection Open()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static IReadOnlyDictionary<string, string>? DeserializeData(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json);
}
