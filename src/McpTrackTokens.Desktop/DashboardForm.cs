using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace McpTrackTokens.Desktop;

/// <summary>
/// Full-window WebView2 host for the dashboard with no browser chrome.
/// Follows the Windows app light/dark theme.
/// </summary>
internal sealed class DashboardForm : Form
{
    private readonly DesktopOptions _options;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 11f),
        Text = "Starting…"
    };

    public DashboardForm(DesktopOptions options)
    {
        _options = options;

        Text = options.WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1280;
        Height = 800;
        MinimumSize = new Size(900, 600);
        ShowIcon = true;
        Icon = SystemIcons.Application;

        Controls.Add(_webView);
        Controls.Add(_status);
        _webView.BringToFront();
        _webView.Visible = false;

        ApplyWindowsTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        HandleCreated += (_, _) => WindowsTheme.TrySetImmersiveDarkMode(Handle, WindowsTheme.AppsUseDarkTheme());

        Shown += async (_, _) => await InitializeAsync();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
        {
            return;
        }

        if (IsDisposed)
        {
            return;
        }

        BeginInvoke(async () =>
        {
            ApplyWindowsTheme();
            await SyncWebViewColorSchemeAsync();
            await NotifyDashboardThemeAsync();
        });
    }

    private void ApplyWindowsTheme()
    {
        WindowsTheme.ApplyToForm(this);
        WindowsTheme.ApplyToStatusLabel(_status);
    }

    private async Task InitializeAsync()
    {
        try
        {
            _status.Text = $"Connecting to {_options.ServerUrl}…";
            _status.Visible = true;
            _webView.Visible = false;

            var healthy = await WaitForHealthyAsync(TimeSpan.FromSeconds(20));
            if (!healthy)
            {
                _status.Text =
                    $"Dashboard server is not reachable at {_options.ServerUrl}.{Environment.NewLine}" +
                    "Start the tray host (or Docker) first, then reopen this app.";
                return;
            }

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MCP Track Tokens",
                "DesktopWebView");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            // Keep browser chrome shortcuts off (print, find, zoom), but restore
            // Back/Forward ourselves — disabling accelerators also kills Alt+←/→.
            _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _webView.CoreWebView2.Settings.IsSwipeNavigationEnabled = true;

            AttachHistoryAcceleratorKeys();
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            await SyncWebViewColorSchemeAsync();

            // Keep navigation inside the local dashboard origin.
            _webView.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                {
                    e.Cancel = true;
                    return;
                }

                var allowed = new Uri(_options.ServerUrl);
                if (!string.Equals(uri.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(uri.Host, allowed.Host, StringComparison.OrdinalIgnoreCase) ||
                    uri.Port != allowed.Port)
                {
                    e.Cancel = true;
                }
            };

            _webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                var title = _webView.CoreWebView2.DocumentTitle;
                Text = string.IsNullOrWhiteSpace(title)
                    ? _options.WindowTitle
                    : $"{_options.WindowTitle} — {title}";
            };

            _webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                if (_webView.CanFocus)
                {
                    _webView.Focus();
                }
            };

            await InjectBootstrapScriptsAsync();

            _webView.CoreWebView2.Navigate(NormalizeDashboardUrl(_options.ServerUrl));
            _webView.Visible = true;
            _status.Visible = false;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text =
                "Microsoft Edge WebView2 Runtime is required." + Environment.NewLine +
                "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ then reopen this app.";
            _webView.Visible = false;
            _status.Visible = true;
        }
        catch (Exception ex)
        {
            _status.Text = $"Failed to open dashboard:{Environment.NewLine}{ex.Message}";
            _webView.Visible = false;
            _status.Visible = true;
        }
    }

    private async Task SyncWebViewColorSchemeAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            _webView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
        }
        catch
        {
            // Older runtimes may not expose Profile.
        }

        await Task.CompletedTask;
    }

    private void AttachHistoryAcceleratorKeys()
    {
        // WinForms WebView2 does not expose Controller publicly; the private field is the supported host path.
        var field = typeof(WebView2).GetField(
            "_coreWebView2Controller",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(_webView) is not CoreWebView2Controller controller)
        {
            return;
        }

        controller.AcceleratorKeyPressed += OnAcceleratorKeyPressed;
    }

    private void OnAcceleratorKeyPressed(object? sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
    {
        if (e.KeyEventKind is not (CoreWebView2KeyEventKind.KeyDown or CoreWebView2KeyEventKind.SystemKeyDown))
        {
            return;
        }

        // Ignore key-repeat while holding the key (WasKeyDown is 0/1 in this runtime).
        if (e.PhysicalKeyStatus.WasKeyDown != 0)
        {
            return;
        }

        const uint vkLeft = 0x25;
        const uint vkRight = 0x27;
        const uint vkBrowserBack = 0xA6;
        const uint vkBrowserForward = 0xA7;

        var altHeld = e.KeyEventKind == CoreWebView2KeyEventKind.SystemKeyDown
            || e.PhysicalKeyStatus.IsMenuKeyDown != 0
            || (ModifierKeys & Keys.Alt) == Keys.Alt;

        var goBack = e.VirtualKey == vkBrowserBack || (e.VirtualKey == vkLeft && altHeld);
        var goForward = e.VirtualKey == vkBrowserForward || (e.VirtualKey == vkRight && altHeld);
        if (!goBack && !goForward)
        {
            return;
        }

        e.Handled = true;
        if (goBack && _webView.CanGoBack)
        {
            _webView.GoBack();
        }
        else if (goForward && _webView.CanGoForward)
        {
            _webView.GoForward();
        }
    }

    private async Task InjectBootstrapScriptsAsync()
    {
        var keyName = JsonSerializer.Serialize(_options.ApiKeyStorageKey);
        var keyValue = JsonSerializer.Serialize(_options.ApiKey);
        // Force dashboard theme preference to follow Windows (system).
        var script =
            "(function(){try{" +
            "localStorage.setItem(" + keyName + "," + keyValue + ");" +
            "localStorage.setItem('mcp-track-tokens-theme','system');" +
            "var dark=window.matchMedia('(prefers-color-scheme: dark)').matches;" +
            "document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');" +
            "window.mcpTrackTokensDesktop=true;" +
            "}catch(e){}})();";

        await _webView.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(script)
            .ConfigureAwait(true);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            var requestId = root.TryGetProperty("requestId", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            switch (type)
            {
                case "pickFolder":
                {
                    var defaultPath = root.TryGetProperty("defaultPath", out var pathEl)
                        ? pathEl.GetString()
                        : null;
                    var selected = PickFolder(defaultPath);
                    PostHostResult(requestId, new
                    {
                        type = "pickFolderResult",
                        requestId,
                        path = selected,
                        cancelled = selected is null
                    });
                    break;
                }
                case "resolveDefaultBackupFolder":
                {
                    var preferred = root.TryGetProperty("defaultPath", out var pathEl)
                        ? pathEl.GetString()
                        : null;
                    var path = ResolveDefaultBackupFolder(preferred);
                    PostHostResult(requestId, new
                    {
                        type = "resolveDefaultBackupFolderResult",
                        requestId,
                        path
                    });
                    break;
                }
                case "listBackupFiles":
                {
                    var directory = root.TryGetProperty("directory", out var dirEl)
                        ? dirEl.GetString()
                        : null;
                    var files = ListBackupFiles(directory);
                    PostHostResult(requestId, new
                    {
                        type = "listBackupFilesResult",
                        requestId,
                        files
                    });
                    break;
                }
                case "saveFile":
                {
                    var directory = root.TryGetProperty("directory", out var dirEl) ? dirEl.GetString() : null;
                    var fileName = root.TryGetProperty("fileName", out var nameEl) ? nameEl.GetString() : null;
                    var base64 = root.TryGetProperty("base64", out var dataEl) ? dataEl.GetString() : null;
                    try
                    {
                        var saved = SaveFile(directory, fileName, base64);
                        PostHostResult(requestId, new
                        {
                            type = "saveFileResult",
                            requestId,
                            path = saved,
                            error = (string?)null
                        });
                    }
                    catch (Exception ex)
                    {
                        PostHostResult(requestId, new
                        {
                            type = "saveFileResult",
                            requestId,
                            path = (string?)null,
                            error = ex.Message
                        });
                    }

                    break;
                }
                case "readFile":
                {
                    var path = root.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                    try
                    {
                        var (fileName, base64) = ReadFileAsBase64(path);
                        PostHostResult(requestId, new
                        {
                            type = "readFileResult",
                            requestId,
                            fileName,
                            base64,
                            error = (string?)null
                        });
                    }
                    catch (Exception ex)
                    {
                        PostHostResult(requestId, new
                        {
                            type = "readFileResult",
                            requestId,
                            fileName = (string?)null,
                            base64 = (string?)null,
                            error = ex.Message
                        });
                    }

                    break;
                }
                case "deleteFile":
                {
                    var path = root.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                    try
                    {
                        DeleteBackupFile(path);
                        PostHostResult(requestId, new
                        {
                            type = "deleteFileResult",
                            requestId,
                            error = (string?)null
                        });
                    }
                    catch (Exception ex)
                    {
                        PostHostResult(requestId, new
                        {
                            type = "deleteFileResult",
                            requestId,
                            error = ex.Message
                        });
                    }

                    break;
                }
            }
        }
        catch
        {
            // Ignore malformed host messages.
        }
    }

    private void PostHostResult(string requestId, object payload)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload);
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private string? PickFolder(string? defaultPath)
    {
        var initial = ResolveDefaultBackupFolder(defaultPath);
        Directory.CreateDirectory(initial);

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select folder for MCP Track Tokens database backups",
            UseDescriptionForTitle = true,
            SelectedPath = initial,
            ShowNewFolderButton = true
        };

        string? selected = null;
        void Show()
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                selected = dialog.SelectedPath;
            }
        }

        if (InvokeRequired)
        {
            Invoke(Show);
        }
        else
        {
            Show();
        }

        return selected;
    }

    private static string ResolveDefaultBackupFolder(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            try
            {
                var full = Path.GetFullPath(preferred);
                Directory.CreateDirectory(full);
                return full;
            }
            catch
            {
                // fall through to Documents default
            }
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(documents, "MCP Track Tokens");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static object[] ListBackupFiles(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "mcp-track-tokens-backup-*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .Take(50)
            .Select(info => (object)new
            {
                fileName = info.Name,
                fullPath = info.FullName,
                sizeBytes = info.Length,
                createdAtUtc = info.CreationTimeUtc
            })
            .ToArray();
    }

    private static string SaveFile(string? directory, string? fileName, string? base64)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException("directory, fileName, and base64 are required.");
        }

        Directory.CreateDirectory(directory);
        var safeName = Path.GetFileName(fileName);
        var path = Path.Combine(directory, safeName);
        var bytes = Convert.FromBase64String(base64);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static (string FileName, string Base64) ReadFileAsBase64(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Backup file not found.", path);
        }

        EnsureBackupFileName(path);
        var bytes = File.ReadAllBytes(path);
        return (Path.GetFileName(path), Convert.ToBase64String(bytes));
    }

    private static void DeleteBackupFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Backup file not found.", path);
        }

        EnsureBackupFileName(path);
        File.Delete(path);
    }

    private static void EnsureBackupFileName(string path)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith("mcp-track-tokens-backup-", StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only mcp-track-tokens-backup-*.db files can be managed.");
        }
    }

    private async Task NotifyDashboardThemeAsync()
    {
        if (_webView.CoreWebView2 is null || !_webView.Visible)
        {
            return;
        }

        var dark = WindowsTheme.AppsUseDarkTheme() ? "dark" : "light";
        var script =
            "(function(){try{" +
            "localStorage.setItem('mcp-track-tokens-theme','system');" +
            "document.documentElement.setAttribute('data-theme','" + dark + "');" +
            "window.dispatchEvent(new Event('mcp-track-tokens-theme-sync'));" +
            "}catch(e){}})();";
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        }
        catch
        {
            // ignore transient navigation races
        }
    }

    private async Task<bool> WaitForHealthyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        var healthUrl = new Uri(new Uri(NormalizeDashboardUrl(_options.ServerUrl)), "/health");

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync(healthUrl).ConfigureAwait(true);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // retry until timeout
            }

            await Task.Delay(400).ConfigureAwait(true);
        }

        return false;
    }

    private static string NormalizeDashboardUrl(string serverUrl)
    {
        var trimmed = serverUrl.Trim().TrimEnd('/');
        return trimmed + "/";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        base.Dispose(disposing);
    }
}
