using System;
using System.Threading;

namespace CmsSlugs.Optimizely;

/// <summary>
/// Timing and live progress of the last full index build (startup boot scan or the "Rebuild Slug
/// Index" job). Lets a host show "read 45,000 of 100,000..." while a build is running and "took
/// 3.2s" once it finishes, without wiring its own counters. Written by <see cref="SlugIndexBuilder"/>;
/// read by the diagnostics endpoint or directly on CMS 11.
/// </summary>
public sealed class SlugIndexDiagnostics
{
    private long _processed;
    private long _total;

    /// <summary>UTC time the last full build began, or null if none has run yet.</summary>
    public DateTime? LastBuildStartedUtc { get; private set; }

    /// <summary>UTC time the last full build finished, or null while one is in progress.</summary>
    public DateTime? LastBuildCompletedUtc { get; private set; }

    /// <summary>Entry count produced by the last completed build.</summary>
    public long? LastBuildEntryCount { get; private set; }

    /// <summary>Items processed so far in the current/last build (live during a build).</summary>
    public long Processed => Interlocked.Read(ref _processed);

    /// <summary>Total items expected in the current build, or 0 when the source can't say up front.</summary>
    public long Total => Interlocked.Read(ref _total);

    /// <summary>True while a build is running (started but not yet completed).</summary>
    public bool InProgress => LastBuildStartedUtc.HasValue && LastBuildCompletedUtc is null;

    /// <summary>Wall-clock duration of the last completed build (read + index), or null.</summary>
    public TimeSpan? LastBuildDuration =>
        LastBuildStartedUtc is { } started && LastBuildCompletedUtc is { } completed
            ? completed - started
            : null;

    /// <summary>Stamp the start of a build. <paramref name="total"/> is 0 when not known up front.</summary>
    public void MarkStarted(long total = 0)
    {
        Interlocked.Exchange(ref _processed, 0);
        Interlocked.Exchange(ref _total, total);
        LastBuildCompletedUtc = null;
        LastBuildStartedUtc = DateTime.UtcNow;
    }

    /// <summary>Set the expected total once known (e.g. after the source lists what to index).</summary>
    public void SetTotal(long total) => Interlocked.Exchange(ref _total, total);

    /// <summary>Advance the live progress counter (thread-safe; called from parallel workers).</summary>
    public void ReportProgress(long delta = 1) => Interlocked.Add(ref _processed, delta);

    /// <summary>Stamp the end of a build and record how many entries it produced.</summary>
    public void MarkCompleted(long entryCount)
    {
        LastBuildEntryCount = entryCount;
        LastBuildCompletedUtc = DateTime.UtcNow;
    }
}
