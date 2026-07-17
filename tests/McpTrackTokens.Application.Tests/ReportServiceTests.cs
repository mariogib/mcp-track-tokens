using FluentAssertions;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class ReportServiceTests
{
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IActivityEventRepository _events = Substitute.For<IActivityEventRepository>();
    private readonly IActivityWindowRepository _windows = Substitute.For<IActivityWindowRepository>();
    private readonly IActivityWindowService _windowService = Substitute.For<IActivityWindowService>();
    private readonly IExternalUsageRepository _usage = Substitute.For<IExternalUsageRepository>();
    private readonly IUsageAttributionRepository _attributions = Substitute.For<IUsageAttributionRepository>();
    private readonly IImportBatchRepository _imports = Substitute.For<IImportBatchRepository>();
    private readonly ISubscriptionAllocationService _subscription = Substitute.For<ISubscriptionAllocationService>();

    private ReportService CreateSut()
        => new(
            _projects,
            _sessions,
            _events,
            _windows,
            _windowService,
            _usage,
            _attributions,
            _imports,
            _subscription,
            Microsoft.Extensions.Options.Options.Create(new TrackingOptions { DefaultCurrency = "USD" }));

    [Fact]
    public async Task GetActivitySummaryAsync_separates_agent_duration_from_active_project_time()
    {
        var projectId = Guid.NewGuid();
        var from = DateTimeOffset.Parse("2026-07-17T08:00:00Z");
        var to = DateTimeOffset.Parse("2026-07-17T12:00:00Z");

        var events = new[]
        {
            PromptActivityEvent.Create(
                ActivityEventType.PromptSubmitted,
                EditorType.Cursor,
                from.AddMinutes(5),
                projectId),
            PromptActivityEvent.Create(
                ActivityEventType.AgentStarted,
                EditorType.Cursor,
                from.AddMinutes(6),
                projectId),
            PromptActivityEvent.Create(
                ActivityEventType.AgentCompleted,
                EditorType.Cursor,
                from.AddMinutes(8),
                projectId,
                durationMilliseconds: 120_000),
            PromptActivityEvent.Create(
                ActivityEventType.AgentFailed,
                EditorType.Cursor,
                from.AddMinutes(20),
                projectId,
                durationMilliseconds: 30_000)
        };

        var window = ActivityWindow.Create(from.AddMinutes(5), from.AddMinutes(35), 15, projectId);
        // Active time = 30 minutes = 1800 seconds; agent duration = 150000 ms

        _events.ListAsync(from, to, projectId, Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(events);
        _windows.ListAsync(from, to, projectId, Arg.Any<CancellationToken>())
            .Returns([window]);
        _windowService.MergeOverlappingSameProjectWindows(Arg.Any<IEnumerable<ActivityWindow>>())
            .Returns(ci => ci.Arg<IEnumerable<ActivityWindow>>().ToList());

        var sut = CreateSut();
        var summary = await sut.GetActivitySummaryAsync(projectId, from, to);

        summary.PromptCount.Should().Be(1);
        summary.AgentRuns.Should().Be(1);
        summary.AgentDurationMilliseconds.Should().Be(150_000);
        summary.ActiveProjectTimeSeconds.Should().Be(1800);
        summary.AgentDurationMilliseconds.Should().NotBe(summary.ActiveProjectTimeSeconds);
    }

    [Fact]
    public async Task GetProjectActivityAsync_exposes_both_metrics_separately()
    {
        var project = Project.Create("Demo", "demo");
        var from = DateTimeOffset.Parse("2026-07-17T08:00:00Z");
        var to = DateTimeOffset.Parse("2026-07-17T12:00:00Z");

        _projects.GetByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);

        var events = new[]
        {
            PromptActivityEvent.Create(
                ActivityEventType.PromptSubmitted,
                EditorType.Cursor,
                from.AddMinutes(1),
                project.Id),
            PromptActivityEvent.Create(
                ActivityEventType.AgentCompleted,
                EditorType.Cursor,
                from.AddMinutes(3),
                project.Id,
                durationMilliseconds: 45_000)
        };
        var window = ActivityWindow.Create(from, from.AddMinutes(20), 15, project.Id);

        _events.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), project.Id, Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(events);
        _windows.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), project.Id, Arg.Any<CancellationToken>())
            .Returns([window]);
        _windowService.MergeOverlappingSameProjectWindows(Arg.Any<IEnumerable<ActivityWindow>>())
            .Returns(ci => ci.Arg<IEnumerable<ActivityWindow>>().ToList());

        var sut = CreateSut();
        var report = await sut.GetProjectActivityAsync(project.Id, from, to);

        report.AgentDurationMilliseconds.Should().Be(45_000);
        report.ActiveProjectTimeSeconds.Should().Be(1200);
        report.AgentDurationMilliseconds.Should().NotBe(report.ActiveProjectTimeSeconds);
    }
}
