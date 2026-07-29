using FluentAssertions;
using McpTrackTokens.Application.Services;

namespace McpTrackTokens.Application.Tests;

public sealed class OfflineQueueDiskTests
{
    [Fact]
    public void TrimToMax_DropsOldestJsonlLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mtt-q-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "cursor-events.jsonl");
        try
        {
            File.WriteAllText(file, string.Join('\n', Enumerable.Range(0, 10).Select(i => $"{{\"id\":{i}}}")) + "\n");
            var dropped = OfflineQueueDisk.TrimToMax(dir, maxQueuedEvents: 4);
            dropped.Should().BeGreaterThan(0);
            OfflineQueueDisk.CountEvents(dir).Should().BeLessThanOrEqualTo(4);
            var body = File.ReadAllText(file);
            body.Should().Contain("\"id\":9");
            body.Should().NotContain("\"id\":0");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
