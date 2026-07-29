using System.Reflection;

namespace McpTrackTokens.Shared;

/// <summary>
/// Loads the LunarQ branding icon for Windows Forms hosts (Desktop / Tray).
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

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            static name => name.EndsWith(".Assets.app.ico", StringComparison.OrdinalIgnoreCase));
        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }

        return SystemIcons.Application;
    }
}
