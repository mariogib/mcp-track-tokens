using McpTrackTokens.Server.Hosting;

namespace McpTrackTokens.Server;

/// <summary>
/// Entry point for the MCP Track Tokens server (HTTP or stdio).
/// </summary>
public partial class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    public static Task<int> Main(string[] args) => TrackingHost.RunAsync(args);
}
