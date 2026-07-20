using System.Reflection;

namespace McpTrackTokens.Tray;

/// <summary>
/// Loads the LunarQ branding icon for the tray host.
/// </summary>
internal static class AppIconLoader
{
    public static Icon Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(path))
        {
            return new Icon(path);
        }

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            var associated = Icon.ExtractAssociatedIcon(exe);
            if (associated is not null)
            {
                return associated;
            }
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "McpTrackTokens.Tray.Assets.app.ico");
        if (stream is not null)
        {
            return new Icon(stream);
        }

        return SystemIcons.Application;
    }
}
