using McpTrackTokens.Server.Hosting;

namespace McpTrackTokens.Tray;

/// <summary>
/// Starts and stops the in-process TrackingHost (API, SQLite, HTTP MCP, dashboard).
/// </summary>
internal sealed class ServerHostController : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public string ServerUrl { get; private set; } = "http://127.0.0.1:5187";

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runTask is { IsCompleted: false };
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_runTask is { IsCompleted: false })
            {
                return;
            }
        }

        // Content root is the tray output directory (appsettings.json + wwwroot).
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        Environment.SetEnvironmentVariable("MCP_TRACK_TOKENS_ENABLE_HTTP_MCP", "true");
        Environment.SetEnvironmentVariable("MCP_TRACK_TOKENS_MIGRATE_ON_STARTUP", "true");
        // Match docker-compose default so the dashboard localStorage key keeps working.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MCP_TRACK_TOKENS_API_KEY")))
        {
            Environment.SetEnvironmentVariable("MCP_TRACK_TOKENS_API_KEY", "OverTheMoon");
        }

        ServerUrl = Environment.GetEnvironmentVariable("MCP_TRACK_TOKENS_SERVER_URL")
            ?? "http://127.0.0.1:5187";

        var cts = new CancellationTokenSource();
        var runTask = Task.Run(
            () => TrackingHost.RunAsync(["--http", "--migrate"], cts.Token),
            CancellationToken.None);

        lock (_gate)
        {
            _cts = cts;
            _runTask = runTask;
        }

        try
        {
            await WaitForHealthyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Health wait failed or was cancelled — tear down this attempt so a retry
            // cannot orphan the CTS or overlap another TrackingHost.
            await ShutdownOwnedAsync(cts, runTask, throwOnTimeout: false).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? runTask;
        lock (_gate)
        {
            cts = _cts;
            runTask = _runTask;
        }

        if (cts is null && runTask is null)
        {
            return;
        }

        // Keep tracking incomplete run tasks so StartAsync cannot overlap a stuck host.
        await ShutdownOwnedAsync(cts, runTask, throwOnTimeout: true).ConfigureAwait(false);
    }

    public async Task<bool> CheckHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http
                .GetAsync(new Uri(new Uri(ServerUrl), "/health"), cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task WaitForHealthyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await CheckHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Server did not become healthy at {ServerUrl}/health within 30 seconds.");
    }

    /// <summary>
    /// Cancels and awaits <paramref name="runTask"/>, disposing <paramref name="cts"/> when safe.
    /// Incomplete tasks remain assigned so <see cref="IsRunning"/> stays true and Start will not overlap.
    /// </summary>
    private async Task ShutdownOwnedAsync(
        CancellationTokenSource? cts,
        Task? runTask,
        bool throwOnTimeout)
    {
        try
        {
            if (cts is not null)
            {
                try
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed by a concurrent shutdown of the same attempt.
                }
            }

            if (runTask is null)
            {
                return;
            }

            if (!runTask.IsCompleted)
            {
                var completed = await Task.WhenAny(runTask, Task.Delay(StopTimeout))
                    .ConfigureAwait(false);
                if (completed != runTask)
                {
                    if (throwOnTimeout)
                    {
                        throw new TimeoutException(
                            "Host did not stop within 5 seconds. It is still tracked so Start will not overlap; Exit the tray to force process shutdown.");
                    }

                    return;
                }
            }

            // Surface host faults after a clean stop; ignore cancel-related exits.
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(static e => e is OperationCanceledException))
            {
            }
        }
        finally
        {
            ClearIfCurrent(cts, runTask);
        }
    }

    private void ClearIfCurrent(CancellationTokenSource? cts, Task? runTask)
    {
        lock (_gate)
        {
            // Never drop an incomplete run task — that is what allowed overlapping hosts.
            if (runTask is not null && ReferenceEquals(_runTask, runTask) && runTask.IsCompleted)
            {
                _runTask = null;
            }

            if (cts is null)
            {
                return;
            }

            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }

            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Host may still be running until process exit; HttpClient must still be released.
        }
        finally
        {
            _http.Dispose();
        }
    }
}
