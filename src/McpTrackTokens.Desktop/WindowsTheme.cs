using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace McpTrackTokens.Desktop;

internal static class WindowsTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    public static bool AppsUseDarkTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyToForm(Form form)
    {
        var dark = AppsUseDarkTheme();
        form.BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 241, 235);
        form.ForeColor = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(30, 40, 36);
        TrySetImmersiveDarkMode(form.Handle, dark);
    }

    public static void ApplyToStatusLabel(Label label)
    {
        var dark = AppsUseDarkTheme();
        label.BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 241, 235);
        label.ForeColor = dark ? Color.FromArgb(220, 220, 220) : Color.FromArgb(30, 40, 36);
    }

    public static void TrySetImmersiveDarkMode(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = enabled ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
