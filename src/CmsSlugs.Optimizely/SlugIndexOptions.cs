namespace CmsSlugs.Optimizely;

/// <summary>
/// Tunables for a full index build. Override by registering a configured instance (e.g. bound from
/// appsettings) or via <c>AddCmsSlugs(o =&gt; ...)</c>. Read by <see cref="SlugIndexBuilder"/>.
/// </summary>
public sealed class SlugIndexOptions
{
    /// <summary>
    /// Worker threads used to fan the per-item reads of a full build.
    /// 0 (default) means one per logical processor (<see cref="System.Environment.ProcessorCount"/>).
    ///
    /// Guidance: catalog reads are I/O-bound (SQL / cache), so threads spend time waiting — a value
    /// ABOVE the core count usually helps. Start at 0 (auto); if the box and database have headroom,
    /// try 2x–4x the core count. Don't go far past that: every worker holds a DB connection, so keep
    /// it under the SQL connection-pool limit (default 100) and watch database CPU — beyond the
    /// database's throughput more threads only add contention and can cause pool-timeout errors.
    /// A sensible practical ceiling for a single SQL Server is ~16–32.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 0;
}
