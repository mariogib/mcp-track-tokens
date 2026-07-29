namespace McpTrackTokens.Application.Services;

/// <summary>
/// Counts and trims offline event files under the configured queue directory
/// (Cursor hooks JSONL plus optional single-event <c>*.json</c> stubs).
/// </summary>
public static class OfflineQueueDisk
{
    /// <summary>
    /// Counts queued events on disk (JSONL lines + individual <c>*.json</c> files).
    /// </summary>
    public static int CountEvents(string queuePath)
    {
        if (string.IsNullOrWhiteSpace(queuePath) || !Directory.Exists(queuePath))
        {
            return 0;
        }

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(queuePath, "*.jsonl"))
        {
            count += CountNonEmptyLines(file);
        }

        count += Directory.GetFiles(queuePath, "*.json").Length;
        return count;
    }

    /// <summary>
    /// Drops oldest events when total queued events exceed <paramref name="maxQueuedEvents"/>.
    /// Returns the number of events removed.
    /// </summary>
    public static int TrimToMax(string queuePath, int maxQueuedEvents)
    {
        if (string.IsNullOrWhiteSpace(queuePath) || !Directory.Exists(queuePath))
        {
            return 0;
        }

        var max = Math.Max(1, maxQueuedEvents);
        var dropped = 0;

        foreach (var file in Directory.EnumerateFiles(queuePath, "*.jsonl"))
        {
            dropped += TrimJsonlFile(file, max);
        }

        // Individual *.json stubs: keep newest files up to remaining capacity.
        var jsonFiles = Directory.GetFiles(queuePath, "*.json")
            .Select(path => new FileInfo(path))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        var jsonlCount = Directory.EnumerateFiles(queuePath, "*.jsonl")
            .Sum(CountNonEmptyLines);
        var remaining = Math.Max(0, max - jsonlCount);
        if (jsonFiles.Count > remaining)
        {
            var remove = jsonFiles.Count - remaining;
            foreach (var file in jsonFiles.Take(remove))
            {
                try
                {
                    file.Delete();
                    dropped += 1;
                }
                catch
                {
                    // best effort
                }
            }
        }

        return dropped;
    }

    private static int TrimJsonlFile(string filePath, int maxQueuedEvents)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(filePath)
                .Where(static l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
        }
        catch
        {
            return 0;
        }

        if (lines.Length <= maxQueuedEvents)
        {
            return 0;
        }

        // Drop ~10% oldest, but enough to get under the cap.
        var over = lines.Length - maxQueuedEvents;
        var dropCount = Math.Max(over, Math.Max(1, maxQueuedEvents / 10));
        dropCount = Math.Min(dropCount, lines.Length);
        var kept = lines.Skip(dropCount).ToArray();
        File.WriteAllText(filePath, kept.Length == 0 ? string.Empty : string.Join('\n', kept) + "\n");
        return dropCount;
    }

    private static int CountNonEmptyLines(string filePath)
    {
        try
        {
            return File.ReadLines(filePath).Count(static l => !string.IsNullOrWhiteSpace(l));
        }
        catch
        {
            return 0;
        }
    }
}
