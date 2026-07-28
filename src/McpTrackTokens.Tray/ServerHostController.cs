using McpTrackTokens.Server.Hosting;

namespace McpTrackTokens.Tray;

/// <summary>
/// Starts and stops the in-process TrackingHost (API, SQLite, HTTP MCP, dashboard).
/// </summary>
internal sealed class ServerHostController : IAsyncDisposable
{
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

        await WaitForHealthyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? runTask;
        lock (_gate)
        {
            cts = _cts;
            runTask = _runTask;
            _cts = null;
            _runTask = null;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
            if (runTask is not null)
            {
                var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)))
                    .ConfigureAwait(false);
                if (completed != runTask)
                {
                    // Host did not stop in time (e.g. stuck MCP/SSE work). Abandon
                    // the run task; callers that need a dead process should Exit.
                    return;
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
        }
        finally
        {
            cts.Dispose();
        }
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

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}
