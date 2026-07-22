using System.CommandLine;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using McpTrackTokens.Application.DependencyInjection;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.DependencyInjection;
using McpTrackTokens.Infrastructure.Persistence;
using McpTrackTokens.Server.Configuration;
using McpTrackTokens.Server.Hosting;
using McpTrackTokens.Server.Mapping;

namespace McpTrackTokens.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("MCP Track Tokens CLI");
        root.Add(BuildServeCommand());
        root.Add(BuildMigrateCommand());
        root.Add(BuildStatusCommand());
        root.Add(BuildRegisterProjectCommand());
        root.Add(BuildListProjectsCommand());
        root.Add(BuildImportCursorUsageCommand());
        root.Add(BuildExportCommand());
        root.Add(BuildReconcileCommand());
        root.Add(BuildCreateApiKeyCommand());
        root.Add(BuildInstallCursorHooksCommand());
        root.Add(BuildRemoveCursorHooksCommand());
        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    private static Command BuildServeCommand()
    {
        var stdio = new Option<bool>("--stdio") { Description = "Run MCP over stdio instead of HTTP" };
        var migrate = new Option<bool>("--migrate") { Description = "Apply EF Core migrations on startup" };
        var http = new Option<bool>("--http") { Description = "Force HTTP mode" };
        var command = new Command("serve", "Host the MCP Track Tokens server");
        command.Add(stdio);
        command.Add(migrate);
        command.Add(http);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var args = new List<string>();
            if (parseResult.GetValue(stdio) && !parseResult.GetValue(http))
            {
                args.Add("--stdio");
            }
            else
            {
                args.Add("--http");
            }

            if (parseResult.GetValue(migrate))
            {
                args.Add("--migrate");
            }

            return await TrackingHost.RunAsync(args.ToArray(), cancellationToken).ConfigureAwait(false);
        });
        return command;
    }

    private static Command BuildMigrateCommand()
    {
        var command = new Command("migrate", "Apply EF Core migrations to the configured database");
        command.SetAction(async (_, _) =>
        {
            using var host = CreateHost(migrateOnStartup: true);
            await host.StartAsync().ConfigureAwait(false);
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
            await db.Database.MigrateAsync().ConfigureAwait(false);
            Console.WriteLine("Migrations applied successfully.");
            await host.StopAsync().ConfigureAwait(false);
            return 0;
        });
        return command;
    }

    private static Command BuildStatusCommand()
    {
        var command = new Command("status", "Show current tracking status");
        command.SetAction(async (_, _) =>
        {
            using var host = CreateHost();
            using var scope = host.Services.CreateScope();
            var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
            var status = await reports.GetTrackingStatusAsync().ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(status, JsonOptions));
            return 0;
        });
        return command;
    }

    private static Command BuildRegisterProjectCommand()
    {
        var name = new Option<string>("--name") { Description = "Project name", Required = true };
        var slug = new Option<string?>("--slug") { Description = "Optional slug" };
        var client = new Option<string?>("--client") { Description = "Optional client name" };
        var billing = new Option<string?>("--billing-code") { Description = "Optional billing code" };
        var currency = new Option<string?>("--currency") { Description = "Optional currency" };
        var repo = new Option<string?>("--repository") { Description = "Optional repository path" };
        var remote = new Option<string?>("--remote-url") { Description = "Optional remote URL" };
        var command = new Command("register-project", "Register a project");
        command.Add(name);
        command.Add(slug);
        command.Add(client);
        command.Add(billing);
        command.Add(currency);
        command.Add(repo);
        command.Add(remote);
        command.SetAction(async (parseResult, _) =>
        {
            using var host = CreateHost();
            using var scope = host.Services.CreateScope();
            var projects = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
            var result = await projects.RegisterAsync(new CreateProjectRequest
            {
                Name = parseResult.GetValue(name)!,
                Slug = parseResult.GetValue(slug),
                ClientName = parseResult.GetValue(client),
                BillingCode = parseResult.GetValue(billing),
                Currency = parseResult.GetValue(currency),
                RepositoryPath = parseResult.GetValue(repo),
                RemoteUrl = parseResult.GetValue(remote)
            }).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        });
        return command;
    }

    private static Command BuildListProjectsCommand()
    {
        var all = new Option<bool>("--all") { Description = "Include inactive projects" };
        var command = new Command("list-projects", "List registered projects");
        command.Add(all);
        command.SetAction(async (parseResult, _) =>
        {
            using var host = CreateHost();
            using var scope = host.Services.CreateScope();
            var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            var list = await projects.ListAsync(activeOnly: !parseResult.GetValue(all)).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(list.Select(ProjectMapper.ToDto).ToList(), JsonOptions));
            return 0;
        });
        return command;
    }

    private static Command BuildImportCursorUsageCommand()
    {
        var file = new Option<string>("--file") { Description = "Path to Cursor usage export", Required = true };
        var dryRun = new Option<bool>("--dry-run") { Description = "Preview without persisting" };
        var force = new Option<bool>("--force") { Description = "Force import even if file hash exists" };
        var format = new Option<string?>("--format") { Description = "Optional format override" };
        var command = new Command("import-cursor-usage", "Import a Cursor usage export file");
        command.Add(file);
        command.Add(dryRun);
        command.Add(force);
        command.Add(format);
        command.SetAction(async (parseResult, _) =>
        {
            using var host = CreateHost();
            using var scope = host.Services.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<ICursorUsageImporter>();
            var result = await importer.ImportAsync(new ImportCursorUsageRequestDto
            {
                FilePath = parseResult.GetValue(file)!,
                DryRun = parseResult.GetValue(dryRun),
                Force = parseResult.GetValue(force),
                Format = parseResult.GetValue(format)
            }).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        });
        return command;
    }

    private static Command BuildExportCommand()
    {
        var reportType = new Option<string>("--type")
        {
            Description = "Report type",
            DefaultValueFactory = _ => "project-cost"
        };
        var projectId = new Option<Guid?>("--project-id") { Description = "Optional project id" };
        var from = new Option<DateTimeOffset?>("--from") { Description = "Range start UTC" };
        var to = new Option<DateTimeOffset?>("--to") { Description = "Range end UTC" };
        var output = new Option<string?>("--output") { Description = "Optional output directory" };
        var format = new Option<string>("--format")
        {
            Description = "Export format: json, markdown, csv",
            DefaultValueFactory = _ => "json"
        };
        var command = new Command("export", "Export a report to disk");
        command.Add(reportType);
        command.Add(projectId);
        command.Add(from);
        command.Add(to);
        command.Add(output);
        command.Add(format);
        command.SetAction(async (parseResult, _) =>
        {
            using var host = CreateHost();
            using var scope = host.Services.CreateScope();
            var export = scope.ServiceProvider.GetRequiredService<IExportService>();
            var end = parseResult.GetValue(to) ?? DateTimeOffset.UtcNow;
            var start = parseResult.GetValue(from) ?? end.AddDays(-30);
            if (!Enum.TryParse<ExportFormat>(parseResult.GetValue(format), ignoreCase: true, out var exportFormat))
            {
                exportFormat = ExportFormat.Json;
            }

            var result = await export.ExportAsync(new ExportRequestDto
            {
                ReportType = parseResult.GetValue(reportType)!,
                ProjectId = parseResult.GetValue(projectId),
                FromUtc = start,
                ToUtc = end,
                OutputDirectory = parseResult.GetValue(output),
                Format = exportFormat
            }).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        });
        return command;
    }

    private static Command BuildReconcileCommand()
    {
        var from = new Option<DateTimeOffset?>("--from") { Description = "Range start UTC" };
        var to = new Option<DateTimeOffset?>("--to") { Description = "Range end UTC" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Propose without persisting" };
        var includeLow = new Option<bool>("--include-low-confidence") { Description = "Include low-confidence attributions" };
        var command = new Command("reconcile", "Run usage reconciliation");
        command.Add(from);
        command.Add(to);
        command.Add(dryRun);
        command.Add(includeLow);
        command.SetAction(async (parseResult, _) =>
        {
            using var host = CreateHost();
            using var scope = host.Services.CreateScope();
            var reconciliation = scope.ServiceProvider.GetRequiredService<IReconciliationService>();
            var end = parseResult.GetValue(to) ?? DateTimeOffset.UtcNow;
            var start = parseResult.GetValue(from) ?? end.AddDays(-7);
            var result = await reconciliation.RunAsync(new ReconciliationRequestDto
            {
                FromUtc = start,
                ToUtc = end,
                DryRun = parseResult.GetValue(dryRun),
                IncludeLowConfidence = parseResult.GetValue(includeLow)
            }).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        });
        return command;
    }

    private static Command BuildCreateApiKeyCommand()
    {
        var name = new Option<string>("--name")
        {
            Description = "API key display name",
            DefaultValueFactory = _ => "cli"
        };
        var command = new Command("create-api-key", "Create a tracking API key (plaintext shown once)");
        command.Add(name);
        command.SetAction(async (parseResult, _) =>
        {
            using var host = CreateHost(migrateOnStartup: true);
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
            await db.Database.MigrateAsync().ConfigureAwait(false);
            var apiKeys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
            var result = await apiKeys.CreateAsync(new CreateApiKeyRequestDto
            {
                Name = parseResult.GetValue(name)!
            }).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        });
        return command;
    }

    private static Command BuildInstallCursorHooksCommand()
    {
        var target = new Option<string?>("--path") { Description = "Target Cursor config directory (default ~/.cursor)" };
        var yes = new Option<bool>("--yes") { Description = "Skip confirmation prompt" };
        var command = new Command("install-cursor-hooks", "Install Cursor hook scripts and example config");
        command.Add(target);
        command.Add(yes);
        command.SetAction(parseResult =>
        {
            var cursorDir = string.IsNullOrWhiteSpace(parseResult.GetValue(target))
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor")
                : TrackingOptions.ExpandPath(parseResult.GetValue(target)!);
            var hooksTarget = Path.Combine(cursorDir, "mcp-track-tokens-hooks");

            if (!parseResult.GetValue(yes))
            {
                Console.Write($"Install Cursor hooks into '{hooksTarget}'? [y/N]: ");
                var answer = Console.ReadLine();
                if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Cancelled.");
                    return 0;
                }
            }

            Directory.CreateDirectory(hooksTarget);
            var source = ResolveHooksSource();
            if (Directory.Exists(source))
            {
                CopyDirectory(source, hooksTarget);
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(hooksTarget, "dist"));
                File.WriteAllText(
                    Path.Combine(hooksTarget, "README.md"),
                    "Place built Cursor hook scripts in dist/. See integrations/cursor-hooks in the repository.");
            }

            var exampleConfigPath = Path.Combine(cursorDir, "mcp-track-tokens-hooks.example.json");
            File.WriteAllText(exampleConfigPath, """
                {
                  "version": 1,
                  "hooks": {
                    "beforeSubmitPrompt": [
                      { "command": "./mcp-track-tokens-hooks/dist/prompt-submitted.js", "timeout": 5 }
                    ],
                    "sessionStart": [
                      { "command": "./mcp-track-tokens-hooks/dist/session-started.js", "timeout": 5 }
                    ],
                    "sessionEnd": [
                      { "command": "./mcp-track-tokens-hooks/dist/session-ended.js", "timeout": 5 }
                    ],
                    "subagentStart": [
                      { "command": "./mcp-track-tokens-hooks/dist/agent-started.js", "timeout": 5 }
                    ],
                    "subagentStop": [
                      { "command": "./mcp-track-tokens-hooks/dist/agent-completed.js", "timeout": 5 }
                    ],
                    "stop": [
                      { "command": "./mcp-track-tokens-hooks/dist/agent-completed.js", "timeout": 5 }
                    ]
                  }
                }
                """);

            Console.WriteLine($"Installed hooks scaffold to {hooksTarget}");
            Console.WriteLine($"Wrote example config to {exampleConfigPath}");
            return 0;
        });
        return command;
    }

    private static Command BuildRemoveCursorHooksCommand()
    {
        var target = new Option<string?>("--path") { Description = "Target Cursor config directory (default ~/.cursor)" };
        var yes = new Option<bool>("--yes") { Description = "Skip confirmation prompt" };
        var command = new Command("remove-cursor-hooks", "Remove installed Cursor hook scripts");
        command.Add(target);
        command.Add(yes);
        command.SetAction(parseResult =>
        {
            var cursorDir = string.IsNullOrWhiteSpace(parseResult.GetValue(target))
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor")
                : TrackingOptions.ExpandPath(parseResult.GetValue(target)!);
            var hooksTarget = Path.Combine(cursorDir, "mcp-track-tokens-hooks");
            var exampleConfigPath = Path.Combine(cursorDir, "mcp-track-tokens-hooks.example.json");

            if (!parseResult.GetValue(yes))
            {
                Console.Write($"Remove '{hooksTarget}' and example config? [y/N]: ");
                var answer = Console.ReadLine();
                if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Cancelled.");
                    return 0;
                }
            }

            if (Directory.Exists(hooksTarget))
            {
                Directory.Delete(hooksTarget, recursive: true);
            }

            if (File.Exists(exampleConfigPath))
            {
                File.Delete(exampleConfigPath);
            }

            Console.WriteLine("Cursor hooks removed.");
            return 0;
        });
        return command;
    }

    private static IHost CreateHost(bool migrateOnStartup = false)
    {
        var builder = Host.CreateApplicationBuilder();

        var basePath = AppContext.BaseDirectory;
        var serverSettings = Path.Combine(basePath, "appsettings.json");
        if (File.Exists(serverSettings))
        {
            builder.Configuration.AddJsonFile(serverSettings, optional: true, reloadOnChange: false);
        }

        // Env / CLI flags must load after appsettings so MCP_TRACK_TOKENS_* wins.
        TrackingEnvironmentVariables.Apply(builder.Configuration);
        TrackingEnvironmentVariables.ApplyArgs(builder.Configuration, migrateOnStartup);

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        return builder.Build();
    }

    private static string ResolveHooksSource()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "integrations", "cursor-hooks")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "integrations", "cursor-hooks")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "integrations", "cursor-hooks"))
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
