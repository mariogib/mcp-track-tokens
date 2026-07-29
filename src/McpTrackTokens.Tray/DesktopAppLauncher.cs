using System.Diagnostics;
using System.Runtime.InteropServices;

namespace McpTrackTokens.Tray;

/// <summary>
/// Resolves, starts, and foregrounds the desktop WebView dashboard shell.
/// Activation is done from the tray process so Windows allows SetForegroundWindow
/// (the tray received the user click).
/// </summary>
internal static class DesktopAppLauncher
{
    private const string DesktopExeName = "mcp-track-tokens-desktop.exe";
    private const string DesktopProcessName = "mcp-track-tokens-desktop";

    public static bool TryLaunch(out string? error)
    {
        error = null;

        if (TryActivateExisting())
        {
            return true;
        }

        var path = ResolveDesktopExePath();
        if (path is null)
        {
            error =
                "Desktop app not found. Expected mcp-track-tokens-desktop.exe next to the tray host " +
                "or in a Desktop subfolder (as installed by the MSI).";
            return false;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });

            if (process is not null)
            {
                var processId = process.Id;
                process.Dispose();
                AllowSetForegroundWindow(processId);
                // Window may not exist yet; briefly poll so a cold start still lands in front.
                _ = Task.Run(async () =>
                {
                    for (var i = 0; i < 40; i++)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        if (TryActivateExisting())
                        {
                            return;
                        }
                    }
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static string? ResolveDesktopExePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Desktop", DesktopExeName),
            Path.Combine(baseDir, DesktopExeName),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "McpTrackTokens.Desktop", "bin", "Debug", "net8.0-windows", DesktopExeName)),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "McpTrackTokens.Desktop", "bin", "Release", "net8.0-windows", DesktopExeName)),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryActivateExisting()
    {
        foreach (var process in Process.GetProcessesByName(DesktopProcessName))
        {
            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                AllowSetForegroundWindow(process.Id);

                var hwnd = process.MainWindowHandle;
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = FindMainWindow(process.Id);
                }

                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                ForceForegroundWindow(hwnd);
                return true;
            }
            catch
            {
                // Process may exit while we inspect it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static IntPtr FindMainWindow(int processId)
    {
        var result = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid != (uint)processId || !IsWindowVisible(hwnd))
            {
                return true;
            }

            // Skip owned tool windows; prefer a top-level untitled/titled frame.
            if (GetWindow(hwnd, GwOwner) != IntPtr.Zero)
            {
                return true;
            }

            result = hwnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static void ForceForegroundWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }
        else
        {
            ShowWindow(hwnd, SwShow);
        }

        var foreground = GetForegroundWindow();
        var foreThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var currentThread = GetCurrentThreadId();

        var attachedFore = false;
        var attachedTarget = false;
        try
        {
            if (foreThread != 0 && foreThread != currentThread)
            {
                attachedFore = AttachThreadInput(currentThread, foreThread, true);
            }

            if (targetThread != 0 && targetThread != currentThread && targetThread != foreThread)
            {
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedFore)
            {
                AttachThreadInput(currentThread, foreThread, false);
            }
        }

        // Nudge stubborn Z-order without leaving the window permanently top-most.
        SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        SetWindowPos(hwnd, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        SetForegroundWindow(hwnd);
    }

    private const int SwRestore = 9;
    private const int SwShow = 5;
    private const uint GwOwner = 4;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
}
