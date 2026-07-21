using FluentAssertions;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
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

        var session = EditorSession.Start(EditorType.Cursor, from.AddMinutes(5), projectId);
        session.TransitionTo(SessionStatus.Ended, from.AddMinutes(35));
        // Active time = 30 minutes = 1800 seconds; agent duration = 150000 ms

        _events.ListAsync(from, to, projectId, Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(events);
        _sessions.ListAsync(projectId, from, to, Arg.Any<CancellationToken>())
            .Returns([session]);

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
        var session = EditorSession.Start(EditorType.Cursor, from, project.Id);
        session.TransitionTo(SessionStatus.Ended, from.AddMinutes(20));

        _events.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), project.Id, Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(events);
        _sessions.ListAsync(project.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([session]);

        var sut = CreateSut();
        var report = await sut.GetProjectActivityAsync(project.Id, from, to);

        report.AgentDurationMilliseconds.Should().Be(45_000);
        report.ActiveProjectTimeSeconds.Should().Be(1200);
        report.AgentDurationMilliseconds.Should().NotBe(report.ActiveProjectTimeSeconds);
    }

    [Fact]
    public async Task GetProjectUsageSummaryAsync_aggregates_allocated_usage_flat()
    {
        var project = Project.Create("Demo", "demo", currency: "USD");
        var from = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-07-18T00:00:00Z");

        var usageFull = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            from.AddHours(1),
            inputTokens: 1000,
            outputTokens: 200,
            cachedInputTokens: 400,
            reasoningTokens: 50,
            totalTokens: 1650,
            reportedCost: 2.5m);

        var usagePartial = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            from.AddHours(2),
            inputTokens: 800,
            outputTokens: 100,
            cachedInputTokens: 200,
            reasoningTokens: 40,
            totalTokens: 1140,
            reportedCost: 1.0m);

        var usageUnallocated = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            from.AddHours(3),
            totalTokens: 999,
            reportedCost: 9m);

        _projects.GetByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        _attributions.ListAsync(from, to, project.Id, Arg.Any<CancellationToken>())
            .Returns(
            [
                UsageAttribution.Create(
                    usageFull.Id,
                    AttributionMethod.ClosestPromptMatch,
                    AttributionConfidence.High,
                    100m,
                    allocatedCost: 2.5m,
                    allocatedInputTokens: 1000,
                    allocatedOutputTokens: 200,
                    allocatedTotalTokens: 1650,
                    projectId: project.Id),
                UsageAttribution.Create(
                    usagePartial.Id,
                    AttributionMethod.Manual,
                    AttributionConfidence.Medium,
                    50m,
                    allocatedCost: 0.5m,
                    allocatedInputTokens: 400,
                    allocatedOutputTokens: 50,
                    allocatedTotalTokens: 570,
                    projectId: project.Id),
                UsageAttribution.Create(
                    usageUnallocated.Id,
                    AttributionMethod.Unallocated,
                    AttributionConfidence.Unallocated,
                    0m,
                    allocatedCost: 0m,
                    allocatedTotalTokens: 999)
            ]);
        _usage.GetByIdAsync(usageFull.Id, Arg.Any<CancellationToken>()).Returns(usageFull);
        _usage.GetByIdAsync(usagePartial.Id, Arg.Any<CancellationToken>()).Returns(usagePartial);

        var sut = CreateSut();
        var summary = await sut.GetProjectUsageSummaryAsync(project.Id, from, to);

        summary.InputTokens.Should().Be(1400);
        summary.OutputTokens.Should().Be(250);
        summary.CachedInputTokens.Should().Be(500); // 400 + 50% of 200
        summary.ReasoningTokens.Should().Be(70); // 50 + 50% of 40
        summary.TotalTokens.Should().Be(2220);
        summary.RequestCount.Should().Be(2);
        summary.ReportedCost.Should().Be(3.0m);
        summary.Currency.Should().Be("USD");
        summary.FromUtc.Should().Be(from);
        summary.ToUtc.Should().Be(to);
    }

    [Fact]
    public async Task GetProjectCostAsync_splits_provider_unallocated_and_by_model()
    {
        var project = Project.Create("Demo", "demo", currency: "USD");
        var from = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-07-18T00:00:00Z");

        var cursorUsage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            from.AddHours(1),
            model: "gpt-4.1",
            provider: AIProvider.Cursor,
            totalTokens: 1000,
            reportedCost: 2.0m,
            requestCount: 2);

        var openAiUsage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            from.AddHours(2),
            model: "gpt-4o",
            provider: AIProvider.OpenAI,
            totalTokens: 500,
            reportedCost: 1.5m,
            requestCount: 1);

        var unallocatedInWindow = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            from.AddHours(1).AddMinutes(30),
            model: "claude",
            provider: AIProvider.Anthropic,
            totalTokens: 200,
            reportedCost: 0.8m);

        var unallocatedOutsideWindow = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            from.AddHours(10),
            totalTokens: 100,
            reportedCost: 9.0m);

        var window = ActivityWindow.Create(from.AddHours(1), from.AddHours(3), 15, project.Id);
        var session = EditorSession.Start(EditorType.Cursor, from.AddHours(1), project.Id);
        session.TransitionTo(SessionStatus.Ended, from.AddHours(3));

        _projects.GetByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        _events.ListAsync(from, to, project.Id, Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                PromptActivityEvent.Create(
                    ActivityEventType.PromptSubmitted,
                    EditorType.Cursor,
                    from.AddHours(1),
                    project.Id)
            ]);
        _sessions.ListAsync(project.Id, from, to, Arg.Any<CancellationToken>())
            .Returns([session]);
        _windows.ListAsync(from, to, project.Id, Arg.Any<CancellationToken>())
            .Returns([window]);
        _windowService.MergeOverlappingSameProjectWindows(Arg.Any<IEnumerable<ActivityWindow>>())
            .Returns(ci => ci.Arg<IEnumerable<ActivityWindow>>().ToList());

        _attributions.ListAsync(from, to, project.Id, Arg.Any<CancellationToken>())
            .Returns(
            [
                UsageAttribution.Create(
                    cursorUsage.Id,
                    AttributionMethod.ClosestPromptMatch,
                    AttributionConfidence.High,
                    100m,
                    allocatedCost: 2.0m,
                    allocatedTotalTokens: 1000,
                    projectId: project.Id),
                UsageAttribution.Create(
                    openAiUsage.Id,
                    AttributionMethod.Manual,
                    AttributionConfidence.Certain,
                    100m,
                    allocatedCost: 1.5m,
                    allocatedTotalTokens: 500,
                    projectId: project.Id)
            ]);

        _usage.GetByIdAsync(cursorUsage.Id, Arg.Any<CancellationToken>()).Returns(cursorUsage);
        _usage.GetByIdAsync(openAiUsage.Id, Arg.Any<CancellationToken>()).Returns(openAiUsage);
        _usage.ListUnallocatedAsync(from, to, Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([unallocatedInWindow, unallocatedOutsideWindow]);

        _subscription.AllocateAsync(from, to, Arg.Any<AllocationRuleType?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<Guid, decimal>?>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ProjectAllocationShareDto
                {
                    ProjectId = project.Id,
                    Percentage = 50m
                }
            ]);

        var sut = new ReportService(
            _projects,
            _sessions,
            _events,
            _windows,
            _windowService,
            _usage,
            _attributions,
            _imports,
            _subscription,
            Microsoft.Extensions.Options.Options.Create(new TrackingOptions
            {
                DefaultCurrency = "USD",
                CursorSubscriptionAmount = 20m,
                CursorAllocationMethod = AllocationRuleType.ByActiveProjectTime
            }));

        var report = await sut.GetProjectCostAsync(project.Id, from, to);

        report.UsageBasedCursorCost.Should().Be(2.0m);
        report.OtherProviderCost.Should().Be(1.5m);
        report.SubscriptionAllocation.Should().Be(10.0m);
        report.UnallocatedCost.Should().Be(0.8m);
        report.TotalAiCost.Should().Be(13.5m); // 2 + 10 + 1.5; unallocated excluded
        report.ImportedTotalTokens.Should().Be(1500);
        report.CalculatedTokenCost.Should().BeGreaterThan(0m);
        report.HasRateCard.Should().BeTrue();
        report.ByModel.Should().HaveCount(2);
        report.ByModel.Sum(m => m.CalculatedTokenCost).Should().Be(report.CalculatedTokenCost);

        var gpt41 = report.ByModel.Single(r => r.Name == "gpt-4.1");
        gpt41.UsageBasedCost.Should().Be(2.0m);
        gpt41.PromptCount.Should().Be(2);
        gpt41.SubscriptionAllocation.Should().Be(5.71m); // 20*50% * (2/3.5)

        var gpt4o = report.ByModel.Single(r => r.Name == "gpt-4o");
        gpt4o.UsageBasedCost.Should().Be(1.5m);
        gpt4o.PromptCount.Should().Be(1);
        gpt4o.SubscriptionAllocation.Should().Be(4.29m); // remainder of $10
    }
}
