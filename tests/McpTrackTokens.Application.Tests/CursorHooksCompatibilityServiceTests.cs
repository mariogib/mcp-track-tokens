using FluentAssertions;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class CursorHooksCompatibilityServiceTests
{
    private readonly IActivityEventRepository _events = Substitute.For<IActivityEventRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IEventIngestionService _ingestion = Substitute.For<IEventIngestionService>();

    public CursorHooksCompatibilityServiceTests()
    {
        _events.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _sessions.ListAsync(Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _ingestion.IngestAsync(Arg.Any<IngestEventDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new IngestEventResultDto
            {
                EventId = Guid.NewGuid(),
                WasDuplicate = false
            });
    }

    private CursorHooksCompatibilityService CreateSut()
        => new(_events, _sessions, _ingestion);

    [Fact]
    public async Task CheckAsync_legacy_event_names_are_incompatible()
    {
        var root = CreateTempCursorDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mcp-track-tokens-hooks", "dist"));
            foreach (var script in new[]
                     {
                         "prompt-submitted.js", "agent-started.js", "agent-completed.js", "agent-failed.js",
                         "agent-cancelled.js", "session-started.js", "session-ended.js"
                     })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(root, "mcp-track-tokens-hooks", "dist", script),
                    "process.exit(0);");
            }

            await File.WriteAllTextAsync(Path.Combine(root, "hooks.json"), """
                {
                  "version": 1,
                  "hooks": {
                    "promptSubmitted": [{ "command": "./mcp-track-tokens-hooks/dist/prompt-submitted.js" }],
                    "sessionStarted": [{ "command": "./mcp-track-tokens-hooks/dist/session-started.js" }]
                  }
                }
                """);

            var report = await CreateSut().CheckAsync(root);

            report.Status.Should().Be("incompatible");
            report.LegacyEvents.Should().Contain(["promptSubmitted", "sessionStarted"]);
            report.Checks.Should().Contain(c => c.Id == "legacy_event_names" && c.Status == "fail");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CheckAsync_modern_event_names_pass_config_checks()
    {
        var root = CreateTempCursorDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mcp-track-tokens-hooks", "dist"));
            foreach (var script in new[]
                     {
                         "prompt-submitted.js", "agent-started.js", "agent-completed.js", "agent-failed.js",
                         "agent-cancelled.js", "session-started.js", "session-ended.js"
                     })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(root, "mcp-track-tokens-hooks", "dist", script),
                    "process.exit(0);");
            }

            await File.WriteAllTextAsync(Path.Combine(root, "hooks.json"), """
                {
                  "version": 1,
                  "hooks": {
                    "beforeSubmitPrompt": [{ "command": "./mcp-track-tokens-hooks/dist/prompt-submitted.js", "timeout": 5 }],
                    "sessionStart": [{ "command": "./mcp-track-tokens-hooks/dist/session-started.js", "timeout": 5 }],
                    "sessionEnd": [{ "command": "./mcp-track-tokens-hooks/dist/session-ended.js", "timeout": 5 }],
                    "subagentStart": [{ "command": "./mcp-track-tokens-hooks/dist/agent-started.js", "timeout": 5 }],
                    "subagentStop": [{ "command": "./mcp-track-tokens-hooks/dist/agent-completed.js", "timeout": 5 }],
                    "stop": [{ "command": "./mcp-track-tokens-hooks/dist/agent-completed.js", "timeout": 5 }]
                  }
                }
                """);

            var evt = PromptActivityEvent.Create(
                ActivityEventType.PromptSubmitted,
                EditorType.Cursor,
                DateTimeOffset.UtcNow.AddHours(-1));
            _events.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
                .Returns([evt]);

            var report = await CreateSut().CheckAsync(root);

            report.Status.Should().BeOneOf("compatible", "degraded");
            report.LegacyEvents.Should().BeEmpty();
            report.WiredEvents.Should().Contain(["beforeSubmitPrompt", "sessionStart", "stop"]);
            report.Checks.Should().Contain(c => c.Id == "legacy_event_names" && c.Status == "pass");
            report.Checks.Should().Contain(c => c.Id == "hooks_config_version" && c.Status == "pass");
            report.Checks.Should().Contain(c => c.Id == "command_targets" && c.Status == "pass");
            report.Checks.Should().Contain(c => c.Id == "ingest_probe" && c.Status == "pass");
            report.ProbeEventId.Should().NotBeNull();
            await _ingestion.Received(1).IngestAsync(
                Arg.Is<IngestEventDto>(d =>
                    d.EventType == nameof(ActivityEventType.Heartbeat) &&
                    d.Editor == nameof(EditorType.Cursor) &&
                    d.ExternalEventId!.StartsWith("check_cursor_hooks:", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>());
            report.Checks.Should().NotContain(c => c.Status == "fail");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CheckAsync_missing_version_field_fails()
    {
        var root = CreateTempCursorDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mcp-track-tokens-hooks", "dist"));
            foreach (var script in new[]
                     {
                         "prompt-submitted.js", "agent-started.js", "agent-completed.js", "agent-failed.js",
                         "agent-cancelled.js", "session-started.js", "session-ended.js"
                     })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(root, "mcp-track-tokens-hooks", "dist", script),
                    "process.exit(0);");
            }

            await File.WriteAllTextAsync(Path.Combine(root, "hooks.json"), """
                {
                  "hooks": {
                    "beforeSubmitPrompt": [{ "command": "./mcp-track-tokens-hooks/dist/prompt-submitted.js" }]
                  }
                }
                """);

            var report = await CreateSut().CheckAsync(root);

            report.Status.Should().Be("incompatible");
            report.Checks.Should().Contain(c => c.Id == "hooks_config_version" && c.Status == "fail");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempCursorDir()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mtt-hooks-compat-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
