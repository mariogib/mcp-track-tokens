using System.Text.Json;

namespace McpTrackTokens.Desktop;

internal sealed class DesktopOptions
{
    public string ServerUrl { get; init; } = "http://127.0.0.1:5187";

    public string ApiKey { get; init; } = "OverTheMoon";

    public string ApiKeyStorageKey { get; init; } = "mcp-track-tokens-api-key";

    public string WindowTitle { get; init; } = "MCP Track Tokens";

    public static DesktopOptions Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return new DesktopOptions();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DesktopOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new DesktopOptions();
        }
        catch
        {
            return new DesktopOptions();
        }
    }
}
