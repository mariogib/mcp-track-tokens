using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Verifies Cursor hooks install + hooks.json against the current Cursor hooks API.
/// </summary>
public sealed class CursorHooksCompatibilityService : ICursorHooksCompatibilityService
{
    /// <summary>Hook events documented by Cursor (agent + tab + app lifecycle).</summary>
    internal static readonly HashSet<string> KnownCursorHookEvents = new(StringComparer.Ordinal)
    {
        "sessionStart",
        "sessionEnd",
        "preToolUse",
        "postToolUse",
        "postToolUseFailure",
        "subagentStart",
        "subagentStop",
        "beforeShellExecution",
        "afterShellExecution",
        "beforeMCPExecution",
        "afterMCPExecution",
        "beforeReadFile",
        "afterFileEdit",
        "beforeSubmitPrompt",
        "preCompact",
        "stop",
        "afterAgentResponse",
        "afterAgentThought",
        "beforeTabFileRead",
        "afterTabFileEdit",
        "workspaceOpen"
    };

    /// <summary>Legacy event names from older MCP Track Tokens examples (Cursor ignores these).</summary>
    internal static readonly HashSet<string> LegacyTrackTokensHookEvents = new(StringComparer.Ordinal)
    {
        "promptSubmitted",
        "agentStarted",
        "agentCompleted",
        "agentFailed",
        "agentCancelled",
        "sessionStarted",
        "sessionEnded"
    };

    /// <summary>Recommended Cursor → script mapping for activity tracking.</summary>
    internal static readonly IReadOnlyDictionary<string, string> RecommendedEventScripts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["beforeSubmitPrompt"] = "prompt-submitted",
            ["sessionStart"] = "session-started",
            ["sessionEnd"] = "session-ended",
            ["subagentStart"] = "agent-started",
            ["subagentStop"] = "agent-completed",
            ["stop"] = "agent-completed"
        };

    private static readonly string[] RequiredDistScripts =
    [
        "prompt-submitted.js",
        "agent-started.js",
        "agent-completed.js",
        "agent-failed.js",
        "agent-cancelled.js",
        "session-started.js",
        "session-ended.js"
    ];

    private static readonly Regex CmdRunHook =
        new(@"mcp-track-tokens-hooks[\\/]+run\.cmd\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DistScript =
        new(@"mcp-track-tokens-hooks[\\/]+dist[\\/]+([a-z0-9\-]+)\.js", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IActivityEventRepository _events;
    private readonly ISessionRepository _sessions;
    private readonly IEventIngestionService _ingestion;

    public CursorHooksCompatibilityService(
        IActivityEventRepository events,
        ISessionRepository sessions,
        IEventIngestionService ingestion)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _ingestion = ingestion ?? throw new ArgumentNullException(nameof(ingestion));
    }

    /// <inheritdoc />
    public async Task<CursorHooksCompatibilityReportDto> CheckAsync(
        string? cursorUserDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<CursorHooksCompatibilityCheckDto>();
        var recommendations = new List<string>();
        var wiredEvents = new List<string>();
        var legacyEvents = new List<string>();

        var cursorDir = string.IsNullOrWhiteSpace(cursorUserDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor")
            : Path.GetFullPath(cursorUserDirectory);
        var hooksInstallDir = Path.Combine(cursorDir, "mcp-track-tokens-hooks");
        var hooksConfigPath = Path.Combine(cursorDir, "hooks.json");

        var (cursorVersion, cursorVersionSource) = DetectCursorVersion();
        checks.Add(cursorVersion is null
            ? Check("cursor_install", "warn", "Could not detect an installed Cursor app version from common install paths.")
            : Check("cursor_install", "pass", $"Detected Cursor {cursorVersion} ({cursorVersionSource})."));

        var hooksOnDisk = Directory.Exists(hooksInstallDir);
        string? packageVersion = null;
        if (!hooksOnDisk)
        {
            checks.Add(Check(
                "hooks_install",
                "fail",
                $"Hook scripts directory not found at {hooksInstallDir}."));
            recommendations.Add(
                "Install hooks with: mcp-track-tokens install-cursor-hooks --yes (or reinstall the MSI with hooks enabled).");
        }
        else
        {
            packageVersion = TryReadPackageVersion(Path.Combine(hooksInstallDir, "package.json"));
            var distDir = Path.Combine(hooksInstallDir, "dist");
            var missing = RequiredDistScripts
                .Where(name => !File.Exists(Path.Combine(distDir, name)))
                .ToList();
            if (missing.Count > 0)
            {
                checks.Add(Check(
                    "hooks_install",
                    "fail",
                    $"Hooks directory exists but missing dist scripts: {string.Join(", ", missing)}."));
                recommendations.Add("Rebuild and reinstall cursor-hooks so dist/*.js entrypoints are present.");
            }
            else
            {
                var versionNote = packageVersion is null ? string.Empty : $" (package {packageVersion})";
                checks.Add(Check(
                    "hooks_install",
                    "pass",
                    $"Hook scripts found under {hooksInstallDir}{versionNote}."));
            }
        }

        int? schemaVersion = null;
        var trackTokensCommands = new List<(string EventName, string Command)>();
        if (!File.Exists(hooksConfigPath))
        {
            checks.Add(Check(
                "hooks_config",
                "fail",
                $"Cursor hooks config not found at {hooksConfigPath}."));
            recommendations.Add(
                "Create ~/.cursor/hooks.json (or merge the example config) using current Cursor event names such as beforeSubmitPrompt and sessionStart.");
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(hooksConfigPath, cancellationToken).ConfigureAwait(false));
                var root = doc.RootElement;
                if (root.TryGetProperty("version", out var versionEl) && versionEl.TryGetInt32(out var ver))
                {
                    schemaVersion = ver;
                }

                if (schemaVersion is null)
                {
                    checks.Add(Check(
                        "hooks_config_version",
                        "fail",
                        "hooks.json is missing required top-level \"version\": 1 (Cursor 3.x rejects the file)."));
                    recommendations.Add("Add \"version\": 1 at the top level of ~/.cursor/hooks.json.");
                }
                else if (schemaVersion != 1)
                {
                    checks.Add(Check(
                        "hooks_config_version",
                        "warn",
                        $"hooks.json version is {schemaVersion}; this tool expects schema version 1."));
                }
                else
                {
                    checks.Add(Check("hooks_config_version", "pass", "hooks.json declares schema version 1."));
                }

                if (!root.TryGetProperty("hooks", out var hooksEl) || hooksEl.ValueKind != JsonValueKind.Object)
                {
                    checks.Add(Check("hooks_config", "fail", "hooks.json has no \"hooks\" object."));
                }
                else
                {
                    foreach (var property in hooksEl.EnumerateObject())
                    {
                        var eventName = property.Name;
                        var commands = ExtractCommands(property.Value);
                        var ours = commands
                            .Where(IsTrackTokensCommand)
                            .Select(c => (eventName, c))
                            .ToList();
                        if (ours.Count == 0)
                        {
                            continue;
                        }

                        trackTokensCommands.AddRange(ours);
                        if (LegacyTrackTokensHookEvents.Contains(eventName))
                        {
                            legacyEvents.Add(eventName);
                        }
                        else if (KnownCursorHookEvents.Contains(eventName))
                        {
                            wiredEvents.Add(eventName);
                        }
                        else
                        {
                            wiredEvents.Add(eventName);
                            checks.Add(Check(
                                $"event_{eventName}",
                                "warn",
                                $"Event \"{eventName}\" is wired to MCP Track Tokens but is not in the known Cursor hooks catalog (may be newer or renamed)."));
                        }
                    }

                    checks.Add(Check(
                        "hooks_config",
                        "pass",
                        $"Parsed {hooksConfigPath} ({trackTokensCommands.Count} MCP Track Tokens command binding(s))."));
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                checks.Add(Check("hooks_config", "fail", $"Failed to parse hooks.json: {ex.Message}"));
            }
        }

        if (legacyEvents.Count > 0)
        {
            checks.Add(Check(
                "legacy_event_names",
                "fail",
                "hooks.json uses legacy event names Cursor no longer fires: " +
                string.Join(", ", legacyEvents.Distinct(StringComparer.Ordinal)) + "."));
            recommendations.Add(
                "Rename legacy keys to current Cursor events: promptSubmitted→beforeSubmitPrompt, " +
                "sessionStarted→sessionStart, sessionEnded→sessionEnd, agentStarted→subagentStart, " +
                "agentCompleted→stop (and optionally subagentStop / afterAgentResponse).");
        }
        else if (trackTokensCommands.Count > 0)
        {
            checks.Add(Check("legacy_event_names", "pass", "No legacy MCP Track Tokens event names detected."));
        }

        var missingRecommended = RecommendedEventScripts.Keys
            .Where(e => !wiredEvents.Contains(e, StringComparer.Ordinal))
            .ToList();
        if (trackTokensCommands.Count == 0 && File.Exists(hooksConfigPath))
        {
            checks.Add(Check(
                "track_tokens_bindings",
                "fail",
                "hooks.json exists but no commands reference mcp-track-tokens-hooks."));
            recommendations.Add("Wire MCP Track Tokens scripts into ~/.cursor/hooks.json using current Cursor event names.");
        }
        else if (missingRecommended.Count > 0 && trackTokensCommands.Count > 0)
        {
            checks.Add(Check(
                "recommended_events",
                "warn",
                "Missing recommended tracking events: " + string.Join(", ", missingRecommended) + "."));
            recommendations.Add(
                "For full activity coverage, map beforeSubmitPrompt, sessionStart, sessionEnd, subagentStart, subagentStop, and stop.");
        }
        else if (trackTokensCommands.Count > 0)
        {
            checks.Add(Check(
                "recommended_events",
                "pass",
                "Recommended Cursor events are wired to MCP Track Tokens."));
        }

        var missingCommandTargets = new List<string>();
        foreach (var (_, command) in trackTokensCommands)
        {
            foreach (var relative in ExtractReferencedRelativePaths(command))
            {
                var full = Path.GetFullPath(Path.Combine(cursorDir, relative));
                if (!File.Exists(full))
                {
                    missingCommandTargets.Add(relative);
                }
            }
        }

        if (missingCommandTargets.Count > 0)
        {
            checks.Add(Check(
                "command_targets",
                "fail",
                "hooks.json references missing files: " +
                string.Join(", ", missingCommandTargets.Distinct(StringComparer.OrdinalIgnoreCase))));
            recommendations.Add("Reinstall hook scripts or fix command paths so they resolve under ~/.cursor.");
        }
        else if (trackTokensCommands.Count > 0)
        {
            checks.Add(Check("command_targets", "pass", "Referenced hook command files exist under the Cursor user directory."));
        }

        var smoke = await TrySmokeTestAdapterAsync(hooksInstallDir, cursorVersion, cancellationToken).ConfigureAwait(false);
        if (smoke is not null)
        {
            checks.Add(smoke);
            if (smoke.Status == "fail")
            {
                recommendations.Add(
                    "Hook script smoke test failed. Ensure Node.js is on PATH and dist/prompt-submitted.js runs against a modern Cursor stdin payload.");
            }
        }

        var probe = await TryIngestProbeEventAsync(cursorVersion, cancellationToken).ConfigureAwait(false);
        checks.Add(probe.Check);
        if (probe.Check.Status == "fail")
        {
            recommendations.Add(
                "Could not ingest a compatibility probe event. Confirm the tracking database is writable and the host is healthy.");
        }

        var now = DateTimeOffset.UtcNow;
        var recent = await _events
            .ListAsync(now.AddDays(-14), now, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var lastCursor = recent
            .Where(e => e.Editor == EditorType.Cursor)
            .OrderByDescending(e => e.TimestampUtc)
            .FirstOrDefault();

        string? lastEditorVersion = probe.EditorVersion;
        DateTimeOffset? lastCursorAt = probe.IngestedAtUtc;
        if (probe.EventId is null)
        {
            lastCursorAt = lastCursor?.TimestampUtc;
            if (lastCursor?.EditorSessionId is Guid sessionId)
            {
                var session = await _sessions.GetByIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
                lastEditorVersion = session?.EditorVersion;
            }

            if (string.IsNullOrWhiteSpace(lastEditorVersion))
            {
                var recentSessions = await _sessions
                    .ListAsync(fromUtc: now.AddDays(-14), toUtc: now, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                lastEditorVersion = recentSessions
                    .Where(s => s.Editor == EditorType.Cursor && !string.IsNullOrWhiteSpace(s.EditorVersion))
                    .OrderByDescending(s => s.LastActivityAtUtc)
                    .Select(s => s.EditorVersion)
                    .FirstOrDefault();
            }
        }

        if (probe.EventId is not null)
        {
            var versionNote = string.IsNullOrWhiteSpace(lastEditorVersion)
                ? string.Empty
                : $", editorVersion={lastEditorVersion}";
            checks.Add(Check(
                "recent_ingest",
                "pass",
                $"Probe event ingested at {probe.IngestedAtUtc:O} (Heartbeat{versionNote})."));
        }
        else if (lastCursor is null)
        {
            checks.Add(Check(
                "recent_ingest",
                "warn",
                "No Cursor activity events ingested in the last 14 days (hooks may not be firing, or you have not used Agent/Chat yet)."));
        }
        else
        {
            var versionNote = string.IsNullOrWhiteSpace(lastEditorVersion)
                ? string.Empty
                : $", editorVersion={lastEditorVersion}";
            checks.Add(Check(
                "recent_ingest",
                "pass",
                $"Last Cursor event at {lastCursor.TimestampUtc:O} ({lastCursor.EventType}{versionNote})."));
        }

        if (cursorVersion is null && !string.IsNullOrWhiteSpace(lastEditorVersion))
        {
            cursorVersion = lastEditorVersion;
            cursorVersionSource = "recent Cursor editor session";
        }

        var status = AggregateStatus(checks);
        var summary = status switch
        {
            "compatible" => "Cursor hooks look compatible with the installed Cursor hooks API.",
            "degraded" => "Cursor hooks are partially configured; some checks need attention.",
            _ => "Cursor hooks are missing or use an incompatible configuration for current Cursor."
        };

        return new CursorHooksCompatibilityReportDto
        {
            Status = status,
            Summary = summary,
            CursorVersion = cursorVersion,
            CursorVersionSource = cursorVersionSource,
            CursorUserDirectory = cursorDir,
            HooksInstallDirectory = hooksOnDisk ? hooksInstallDir : null,
            HooksConfigPath = File.Exists(hooksConfigPath) ? hooksConfigPath : null,
            HooksConfigSchemaVersion = schemaVersion,
            Checks = checks,
            WiredEvents = wiredEvents.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            LegacyEvents = legacyEvents.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            Recommendations = recommendations.Distinct(StringComparer.Ordinal).ToList(),
            LastCursorEventAtUtc = lastCursorAt ?? lastCursor?.TimestampUtc,
            LastCursorEventEditorVersion = lastEditorVersion,
            ProbeEventId = probe.EventId,
            ProbeIngestedAtUtc = probe.IngestedAtUtc
        };
    }

    private static CursorHooksCompatibilityCheckDto Check(string id, string status, string message)
        => new() { Id = id, Status = status, Message = message };

    private static string AggregateStatus(IReadOnlyList<CursorHooksCompatibilityCheckDto> checks)
    {
        if (checks.Any(c => c.Status == "fail"))
        {
            return "incompatible";
        }

        if (checks.Any(c => c.Status == "warn"))
        {
            return "degraded";
        }

        return "compatible";
    }

    private static bool IsTrackTokensCommand(string command)
        => command.Contains("mcp-track-tokens-hooks", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ExtractCommands(JsonElement value)
    {
        var result = new List<string>();
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                if (value.GetString() is { Length: > 0 } s)
                {
                    result.Add(s);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } direct)
                    {
                        result.Add(direct);
                    }
                    else if (item.ValueKind == JsonValueKind.Object &&
                             item.TryGetProperty("command", out var cmd) &&
                             cmd.GetString() is { Length: > 0 } command)
                    {
                        result.Add(command);
                    }
                }

                break;
            case JsonValueKind.Object:
                if (value.TryGetProperty("command", out var single) &&
                    single.GetString() is { Length: > 0 } one)
                {
                    result.Add(one);
                }

                break;
        }

        return result;
    }

    private static IEnumerable<string> ExtractReferencedRelativePaths(string command)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DistScript.Matches(command))
        {
            var rel = $"mcp-track-tokens-hooks/dist/{match.Groups[1].Value}.js".Replace('/', Path.DirectorySeparatorChar);
            if (seen.Add(rel))
            {
                yield return rel;
            }
        }

        foreach (Match match in CmdRunHook.Matches(command))
        {
            var runCmd = $"mcp-track-tokens-hooks{Path.DirectorySeparatorChar}run.cmd";
            if (seen.Add(runCmd))
            {
                yield return runCmd;
            }
        }

        // Plain relative path form: ./mcp-track-tokens-hooks/...
        var trimmed = command.Trim().Trim('"');
        if (trimmed.Contains("mcp-track-tokens-hooks", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains(' ') &&
            seen.Count == 0)
        {
            var rel = trimmed.Replace('/', Path.DirectorySeparatorChar).TrimStart('.', Path.DirectorySeparatorChar);
            if (seen.Add(rel))
            {
                yield return rel;
            }
        }
    }

    private static (string? Version, string? Source) DetectCursorVersion()
    {
        foreach (var candidate in EnumerateCursorPackageJsonPaths())
        {
            var version = TryReadPackageVersion(candidate);
            if (!string.IsNullOrWhiteSpace(version))
            {
                return (version, candidate);
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cursor.cmd" : "cursor",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return (line.Trim(), "cursor --version");
                }
            }
        }
        catch
        {
            // PATH may not include Cursor CLI.
        }

        return (null, null);
    }

    private static IEnumerable<string> EnumerateCursorPackageJsonPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] roots =
        [
            Path.Combine(localAppData, "Programs", "cursor"),
            Path.Combine(localAppData, "Programs", "Cursor"),
            Path.Combine(programFiles, "Cursor"),
            Path.Combine(programFiles, "cursor"),
            Path.Combine(home, "Applications", "Cursor.app", "Contents", "Resources", "app"),
            "/Applications/Cursor.app/Contents/Resources/app"
        ];

        foreach (var root in roots)
        {
            yield return Path.Combine(root, "resources", "app", "package.json");
            yield return Path.Combine(root, "package.json");
        }
    }

    private static string? TryReadPackageVersion(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("version", out var version) &&
                version.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch
        {
            // ignore unreadable package metadata
        }

        return null;
    }

    private async Task<(
        CursorHooksCompatibilityCheckDto Check,
        Guid? EventId,
        DateTimeOffset? IngestedAtUtc,
        string? EditorVersion)> TryIngestProbeEventAsync(
        string? cursorVersion,
        CancellationToken cancellationToken)
    {
        var ingestedAt = DateTimeOffset.UtcNow;
        var editorVersion = string.IsNullOrWhiteSpace(cursorVersion) ? null : cursorVersion.Trim();
        var externalId = $"check_cursor_hooks:{ingestedAt:yyyyMMddHHmmssfff}:{Guid.NewGuid():N}";
        var metadataJson = JsonSerializer.Serialize(new
        {
            source = "check_cursor_hooks",
            purpose = "compatibility_probe",
            detectedCursorVersion = editorVersion
        });

        try
        {
            using var metadataDoc = JsonDocument.Parse(metadataJson);
            var result = await _ingestion.IngestAsync(
                new IngestEventDto
                {
                    SchemaVersion = "1.0",
                    ExternalEventId = externalId,
                    EventType = nameof(ActivityEventType.Heartbeat),
                    TimestampUtc = ingestedAt,
                    Editor = nameof(EditorType.Cursor),
                    EditorVersion = editorVersion,
                    MachineName = Environment.MachineName,
                    UserName = Environment.UserName,
                    ExternalSessionId = "check_cursor_hooks",
                    Status = "Completed",
                    Metadata = metadataDoc.RootElement.Clone()
                },
                cancellationToken).ConfigureAwait(false);

            return (
                Check(
                    "ingest_probe",
                    "pass",
                    $"Ingested compatibility probe event {result.EventId} (Heartbeat" +
                    (editorVersion is null ? string.Empty : $", editorVersion={editorVersion}") + ")."),
                result.EventId,
                ingestedAt,
                editorVersion);
        }
        catch (Exception ex)
        {
            return (
                Check("ingest_probe", "fail", $"Failed to ingest compatibility probe event: {ex.Message}"),
                null,
                null,
                editorVersion);
        }
    }

    private static async Task<CursorHooksCompatibilityCheckDto?> TrySmokeTestAdapterAsync(
        string hooksInstallDir,
        string? cursorVersion,
        CancellationToken cancellationToken)
    {
        var script = Path.Combine(hooksInstallDir, "dist", "prompt-submitted.js");
        if (!File.Exists(script))
        {
            return null;
        }

        var node = ResolveNodeExecutable();
        if (node is null)
        {
            return Check(
                "payload_smoke",
                "warn",
                "Skipped stdin smoke test (Node.js not found on PATH).");
        }

        var version = string.IsNullOrWhiteSpace(cursorVersion) ? "unknown" : cursorVersion.Trim();
        var conversationId = $"check-cursor-hooks-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["hook_event_name"] = "beforeSubmitPrompt",
            ["cursor_version"] = version,
            ["prompt"] = "check_cursor_hooks payload smoke",
            ["conversation_id"] = conversationId,
            ["generation_id"] = conversationId,
            ["model"] = "compatibility-smoke",
            ["workspace_roots"] = Array.Empty<string>()
        });

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = node,
                Arguments = $"\"{script}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Dead port: validates stdin adapt/exit only. Real ingest is covered by ingest_probe.
            psi.Environment["MCP_TRACK_TOKENS_SERVER_URL"] = "http://127.0.0.1:9";
            psi.Environment["MCP_TRACK_TOKENS_API_KEY"] = "smoke-test";
            psi.Environment["MCP_TRACK_TOKENS_TIMEOUT_MS"] = "200";

            using var process = Process.Start(psi);
            if (process is null)
            {
                return Check("payload_smoke", "warn", "Could not start Node.js for hook smoke test.");
            }

            await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }

                return Check("payload_smoke", "fail", "Hook script smoke test timed out.");
            }

            return process.ExitCode == 0
                ? Check(
                    "payload_smoke",
                    "pass",
                    "Hook script accepted a modern beforeSubmitPrompt stdin payload (exit 0).")
                : Check(
                    "payload_smoke",
                    "fail",
                    $"Hook script exited {process.ExitCode} on a modern Cursor payload.");
        }
        catch (Exception ex)
        {
            return Check("payload_smoke", "warn", $"Hook smoke test could not run: {ex.Message}");
        }
    }

    private static string? ResolveNodeExecutable()
    {
        foreach (var name in OperatingSystem.IsWindows()
                     ? new[] { "node.exe", "node" }
                     : new[] { "node" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process is null)
                {
                    continue;
                }

                process.WaitForExit(2000);
                if (process.ExitCode == 0)
                {
                    return name;
                }
            }
            catch
            {
                // try next
            }
        }

        return null;
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }
}
