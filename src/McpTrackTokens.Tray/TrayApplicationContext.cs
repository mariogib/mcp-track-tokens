namespace McpTrackTokens.Tray;

/// <summary>
/// System tray UI that hosts and controls the MCP Track Tokens server.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ServerHostController _host = new();
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private bool _exiting;

    public TrayApplicationContext()
    {
        _statusItem = new ToolStripMenuItem("Status: Starting…") { Enabled = false };
        _startItem = new ToolStripMenuItem("Start server", null, OnStartClicked);
        _stopItem = new ToolStripMenuItem("Stop server", null, OnStopClicked);
        var openItem = new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenDashboard());
        var exitItem = new ToolStripMenuItem("Exit", null, OnExitClicked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "MCP Track Tokens",
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenDashboard();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _statusTimer.Tick += OnStatusTick;
        _statusTimer.Start();

        _ = StartServerAsync();
    }

    private async void OnStartClicked(object? sender, EventArgs e) => await StartServerAsync();

    private async void OnStopClicked(object? sender, EventArgs e) => await StopServerAsync();

    private async void OnExitClicked(object? sender, EventArgs e) => await ExitAsync();

    private async void OnStatusTick(object? sender, EventArgs e) => await RefreshStatusAsync();

    private async Task StartServerAsync()
    {
        if (_exiting)
        {
            return;
        }

        SetBusy(true, "Status: Starting…");
        try
        {
            await _host.StartAsync().ConfigureAwait(true);
            _tray.BalloonTipTitle = "MCP Track Tokens";
            _tray.BalloonTipText = $"Server running at {_host.ServerUrl}";
            _tray.ShowBalloonTip(3000);
            await RefreshStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetBusy(false, "Status: Failed");
            MessageBox.Show(
                ex.Message,
                "MCP Track Tokens",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task StopServerAsync()
    {
        SetBusy(true, "Status: Stopping…");
        try
        {
            await _host.StopAsync().ConfigureAwait(true);
            await RefreshStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "MCP Track Tokens",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            await RefreshStatusAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (_exiting)
        {
            return;
        }

        var healthy = _host.IsRunning && await _host.CheckHealthyAsync().ConfigureAwait(true);
        if (healthy)
        {
            _statusItem.Text = $"Status: Running ({_host.ServerUrl})";
            _tray.Text = "MCP Track Tokens — Running";
            _startItem.Enabled = false;
            _stopItem.Enabled = true;
        }
        else if (_host.IsRunning)
        {
            _statusItem.Text = "Status: Starting / unhealthy";
            _tray.Text = "MCP Track Tokens — Unhealthy";
            _startItem.Enabled = false;
            _stopItem.Enabled = true;
        }
        else
        {
            _statusItem.Text = "Status: Stopped";
            _tray.Text = "MCP Track Tokens — Stopped";
            _startItem.Enabled = true;
            _stopItem.Enabled = false;
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _statusItem.Text = status;
        _startItem.Enabled = !busy;
        _stopItem.Enabled = !busy;
    }

    private void OpenDashboard()
    {
        if (DesktopAppLauncher.TryLaunch(out var error))
        {
            return;
        }

        MessageBox.Show(
            error ?? "Could not open the desktop dashboard.",
            "MCP Track Tokens",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _statusTimer.Stop();
        SetBusy(true, "Status: Exiting…");
        try
        {
            await _host.StopAsync().ConfigureAwait(true);
        }
        finally
        {
            _tray.Visible = false;
            _tray.Dispose();
            _statusTimer.Dispose();
            await _host.DisposeAsync().ConfigureAwait(true);
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting)
        {
            _statusTimer.Dispose();
            _tray.Dispose();
            _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }
}
