namespace CmsSlugs;

/// <summary>
/// A tiny logging seam so core stays dependency-free (no Microsoft.Extensions.Logging in core).
/// An adapter wires <see cref="OnWarning"/> to its real logger at startup; unset, warnings are dropped.
/// </summary>
public static class CmsSlugsLog
{
    /// <summary>Set by the host/adapter to forward warnings to a real logger.</summary>
    public static Action<string>? OnWarning { get; set; }

    internal static void CollisionLastWriteWins(string key, string previousContentId, string newContentId)
        => OnWarning?.Invoke(
            $"CmsSlugs collision on key '{key}': '{previousContentId}' overwritten by '{newContentId}' (last write wins).");
}
