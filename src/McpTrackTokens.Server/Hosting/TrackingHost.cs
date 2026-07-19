using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AspNetCoreRateLimit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;
using McpTrackTokens.Application.DependencyInjection;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Infrastructure.DependencyInjection;
using McpTrackTokens.Infrastructure.Persistence;
using McpTrackTokens.Server.Configuration;
using McpTrackTokens.Server.Endpoints;
using McpTrackTokens.Server.Middleware;

namespace McpTrackTokens.Server.Hosting;

/// <summary>
/// Shared host construction for HTTP and stdio modes.
/// </summary>
public static class TrackingHost
{
    /// <summary>
    /// Shared JSON serializer options for API and MCP responses.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>
    /// Parses process arguments for dual-mode startup.
    /// </summary>
    public static HostLaunchOptions ParseArgs(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var stdio = args.Any(a => string.Equals(a, "--stdio", StringComparison.OrdinalIgnoreCase));
        var http = args.Any(a => string.Equals(a, "--http", StringComparison.OrdinalIgnoreCase));
        var migrate = args.Any(a => string.Equals(a, "--migrate", StringComparison.OrdinalIgnoreCase));
        return new HostLaunchOptions(
            UseStdio: stdio && !http,
            Migrate: migrate || IsTruthy(Environment.GetEnvironmentVariable("MCP_TRACK_TOKENS_MIGRATE_ON_STARTUP")));
    }

    /// <summary>
    /// Runs the server in HTTP or stdio mode.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var launch = ParseArgs(args);
        Log.Logger = CreateBootstrapLogger(launch.UseStdio);

        try
        {
            if (launch.UseStdio)
            {
                await RunStdioAsync(args, launch, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunHttpAsync(args, launch, cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "MCP Track Tokens host terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates a configured <see cref="WebApplication"/> for HTTP mode (also used by integration tests).
    /// </summary>
    public static WebApplication CreateWebApplication(string[] args, HostLaunchOptions? launch = null)
    {
        launch ??= ParseArgs(args);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });
        ConfigureConfiguration(builder.Configuration, launch.Migrate);
        ConfigureSerilog(builder, stdioMode: false);

        // Docker / LAN binds often use Host headers outside localhost; allow all local deployments.
        builder.WebHost.UseSetting(WebHostDefaults.CaptureStartupErrorsKey, "true");
        builder.Services.Configure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(options =>
        {
            options.AllowedHosts.Clear();
            options.AllowedHosts.Add("*");
        });

        var trackingSection = builder.Configuration.GetSection(TrackingOptions.SectionName);
        var bindAddress = trackingSection.GetValue<string>("BindAddress") ?? "http://127.0.0.1:5187";
        builder.WebHost.UseUrls(bindAddress);

        var maxRequestBytes = trackingSection.GetValue<long?>("MaxRequestBytes") ?? 1_048_576L;
        var maxBackupUploadBytes = trackingSection.GetValue<long?>("MaxBackupUploadBytes") ?? 104_857_600L;
        var maxBodyBytes = Math.Max(maxRequestBytes, maxBackupUploadBytes);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = maxBodyBytes;
        });
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxBodyBytes;
        });

        RegisterCoreServices(builder.Services, builder.Configuration);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "MCP Track Tokens API", Version = "v1" });
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("LocalDashboard", policy =>
            {
                policy
                    .SetIsOriginAllowed(origin =>
                    {
                        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                        {
                            return false;
                        }

                        return uri.Host is "localhost" or "127.0.0.1" or "::1";
                    })
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        builder.Services.AddMemoryCache();
        builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
        builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));
        builder.Services.AddInMemoryRateLimiting();
        builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

        var enableHttpMcp = trackingSection.GetValue<bool>("EnableHttpMcp");
        var mcpBuilder = builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "mcp-track-tokens",
                    Version = "1.0.0"
                };
            })
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly()
            .WithPromptsFromAssembly();

        if (enableHttpMcp)
        {
            mcpBuilder.WithHttpTransport(options => options.Stateless = true);
        }

        var app = builder.Build();
        ConfigureHttpPipeline(app, enableHttpMcp);
        return app;
    }

    private static async Task RunHttpAsync(string[] args, HostLaunchOptions launch, CancellationToken cancellationToken)
    {
        var app = CreateWebApplication(args, launch);
        await InitializePersistenceAsync(app.Services, cancellationToken).ConfigureAwait(false);
        await app.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunStdioAsync(string[] args, HostLaunchOptions launch, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureConfiguration(builder.Configuration, launch.Migrate);
        ConfigureSerilogForStdio(builder);

        RegisterCoreServices(builder.Services, builder.Configuration);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "mcp-track-tokens",
                    Version = "1.0.0"
                };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly()
            .WithPromptsFromAssembly();

        var host = builder.Build();
        await InitializePersistenceAsync(host.Services, cancellationToken).ConfigureAwait(false);
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void RegisterCoreServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddHttpContextAccessor();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
    }

    private static void ConfigureHttpPipeline(WebApplication app, bool enableHttpMcp)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
            };
            options.GetLevel = (ctx, _, ex) =>
            {
                if (ex is not null || ctx.Response.StatusCode >= 500)
                {
                    return LogEventLevel.Error;
                }

                return ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning : LogEventLevel.Information;
            };
        });

        var wwwroot = ResolveWwwRoot(app);
        if (wwwroot is not null)
        {
            var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwroot);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = fileProvider,
                RequestPath = string.Empty
            });
        }

        app.UseIpRateLimiting();
        app.UseCors("LocalDashboard");
        app.UseMiddleware<RequestBodySizeMiddleware>();
        app.UseMiddleware<ApiKeyAuthMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapHealthEndpoints();
        app.MapApiEndpoints();
        app.MapDashboardAdminEndpoints();

        if (wwwroot is not null)
        {
            var indexPath = Path.Combine(wwwroot, "index.html");
            app.MapGet("/", () => Results.Bytes(File.ReadAllBytes(indexPath), "text/html; charset=utf-8"))
                .ExcludeFromDescription();
            app.MapGet("/index.html", () => Results.Bytes(File.ReadAllBytes(indexPath), "text/html; charset=utf-8"))
                .ExcludeFromDescription();
            app.MapFallback(async context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(indexPath).ConfigureAwait(false);
            });
            Log.Information(
                "Serving dashboard from {WwwRoot} (WebRoot={WebRoot}, ContentRoot={ContentRoot})",
                wwwroot,
                app.Environment.WebRootPath,
                app.Environment.ContentRootPath);
        }
        else
        {
            Log.Warning("Dashboard wwwroot not found; UI will not be available");
        }

        if (enableHttpMcp)
        {
            app.MapMcp("/mcp");
        }
    }

    private static string? ResolveWwwRoot(WebApplication app)
    {
        var candidates = new[]
        {
            app.Environment.WebRootPath,
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(app.Environment.ContentRootPath, "wwwroot")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var full = Path.GetFullPath(candidate);
            var indexPath = Path.Combine(full, "index.html");
            if (Directory.Exists(full) && File.Exists(indexPath))
            {
                return full;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies migrations (when enabled) and bootstraps the configured API key.
    /// </summary>
    public static async Task InitializePersistenceAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<TrackingOptions>>().Value;
        var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();

        if (options.MigrateOnStartup)
        {
            Log.Information("Applying EF Core migrations on startup");
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        await EnsureDirectoriesAsync(options).ConfigureAwait(false);
        var rateStore = scope.ServiceProvider.GetRequiredService<ICursorTokenRateStore>();
        await rateStore.LoadIntoAsync(options, cancellationToken).ConfigureAwait(false);
        await BootstrapApiKeyAsync(scope.ServiceProvider, options, cancellationToken).ConfigureAwait(false);
    }

    private static Task EnsureDirectoriesAsync(TrackingOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.GetResolvedDatabasePath())!);
        Directory.CreateDirectory(options.GetResolvedExportPath());
        Directory.CreateDirectory(TrackingOptions.ExpandPath(options.LogPath));
        Directory.CreateDirectory(TrackingOptions.ExpandPath(options.QueuePath));
        return Task.CompletedTask;
    }

    private static async Task BootstrapApiKeyAsync(
        IServiceProvider services,
        TrackingOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return;
        }

        var repository = services.GetRequiredService<IApiKeyRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var hash = ApiKeyService.HashKey(options.ApiKey);
        var existing = await repository.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        var entity = TrackingApiKey.Create("bootstrap", hash);
        await repository.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.Information("Bootstrapped tracking API key from configuration");
    }

    private static void ConfigureConfiguration(ConfigurationManager configuration, bool migrate)
    {
        TrackingEnvironmentVariables.Apply(configuration);
        TrackingEnvironmentVariables.ApplyArgs(configuration, migrate);
    }

    private static void ConfigureSerilog(WebApplicationBuilder builder, bool stdioMode)
    {
        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            ConfigureLogger(loggerConfiguration, context.Configuration, stdioMode);
        });
    }

    private static void ConfigureSerilogForStdio(HostApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, loggerConfiguration) =>
        {
            ConfigureLogger(loggerConfiguration, builder.Configuration, stdioMode: true);
        });
    }

    private static void ConfigureLogger(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        bool stdioMode)
    {
        var tracking = configuration.GetSection(TrackingOptions.SectionName);
        var logPath = TrackingOptions.ExpandPath(
            tracking.GetValue<string>("LogPath") ?? "~/.mcp-track-tokens/logs/");
        Directory.CreateDirectory(logPath);
        var levelName = tracking.GetValue<string>("LogLevel") ?? "Information";
        if (!Enum.TryParse<LogEventLevel>(levelName, ignoreCase: true, out var level))
        {
            level = LogEventLevel.Information;
        }

        loggerConfiguration
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Filter.ByExcluding(logEvent =>
                logEvent.Properties.Values.Any(v =>
                    v.ToString().Contains("Authorization", StringComparison.OrdinalIgnoreCase)))
            .WriteTo.File(
                Path.Combine(logPath, "mcp-track-tokens-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true);

        if (stdioMode)
        {
            loggerConfiguration.WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                formatProvider: CultureInfo.InvariantCulture);
        }
        else
        {
            loggerConfiguration.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
        }
    }

    private static Serilog.ILogger CreateBootstrapLogger(bool stdioMode)
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext();

        if (stdioMode)
        {
            config.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);
        }
        else
        {
            config.WriteTo.Console();
        }

        return config.CreateLogger();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static bool IsTruthy(string? value)
        => value is not null &&
           (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Parsed launch flags for the tracking host.
/// </summary>
/// <param name="UseStdio">When true, run MCP over stdio.</param>
/// <param name="Migrate">When true, apply EF migrations on startup.</param>
public sealed record HostLaunchOptions(bool UseStdio, bool Migrate);
