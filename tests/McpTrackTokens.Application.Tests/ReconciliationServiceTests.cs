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

public sealed class ReconciliationServiceTests
{
    private readonly IExternalUsageRepository _usage = Substitute.For<IExternalUsageRepository>();
    private readonly IAttributionEngine _engine = Substitute.For<IAttributionEngine>();

    private ReconciliationService CreateSut() =>
        new(_usage, _engine, Microsoft.Extensions.Options.Options.Create(new TrackingOptions()));

    [Fact]
    public async Task RunAsync_LinksAllEligibleUsages_AllowingSamePrompt()
    {
        var projectId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-17T10:00:00Z");

        var usage1 = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at.AddSeconds(-1),
            reportedCost: 0m,
            totalTokens: 1000);
        var usage2 = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at.AddSeconds(2),
            reportedCost: 0.5m,
            totalTokens: 2000);
        var skipped = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at,
            reportedCost: 1m,
            totalTokens: 0);

        _usage.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<UsageSource?>(), Arg.Any<CancellationToken>())
            .Returns([usage1, usage2, skipped]);

        _engine.ProposeAsync(usage1, Arg.Any<CancellationToken>())
            .Returns([UsageAttribution.Create(
                usage1.Id,
                AttributionMethod.ClosestPromptMatch,
                AttributionConfidence.High,
                100m,
                allocatedCost: 0m,
                allocatedTotalTokens: 1000,
                projectId: projectId,
                activityEventId: promptId)]);
        _engine.ProposeAsync(usage2, Arg.Any<CancellationToken>())
            .Returns([UsageAttribution.Create(
                usage2.Id,
                AttributionMethod.ClosestPromptMatch,
                AttributionConfidence.High,
                100m,
                allocatedCost: 0.5m,
                allocatedTotalTokens: 2000,
                projectId: projectId,
                activityEventId: promptId)]);

        var result = await CreateSut().RunAsync(new ReconciliationRequestDto
        {
            FromUtc = at.AddHours(-1),
            ToUtc = at.AddHours(1),
            DryRun = true
        });

        result.ProcessedCount.Should().Be(2);
        result.AllocatedCount.Should().Be(2);
        result.SkippedCount.Should().Be(1);
        result.Attributions.Should().HaveCount(2);
        result.Attributions.Should().OnlyContain(a => a.ActivityEventId == promptId);
        result.Attributions.Select(a => a.UsageRecordId).Should().BeEquivalentTo([usage1.Id, usage2.Id]);
    }

    [Fact]
    public async Task RunAsync_PersistsWhenNotDryRun()
    {
        var at = DateTimeOffset.Parse("2026-07-17T12:00:00Z");
        var usage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at,
            reportedCost: 1m,
            totalTokens: 500);
        var attribution = UsageAttribution.Create(
            usage.Id,
            AttributionMethod.ClosestPromptMatch,
            AttributionConfidence.High,
            100m,
            allocatedCost: 1m,
            allocatedTotalTokens: 500,
            projectId: Guid.NewGuid(),
            activityEventId: Guid.NewGuid());

        _usage.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<UsageSource?>(), Arg.Any<CancellationToken>())
            .Returns([usage]);
        _engine.ProposeAsync(usage, Arg.Any<CancellationToken>()).Returns([attribution]);

        await CreateSut().RunAsync(new ReconciliationRequestDto
        {
            FromUtc = at.AddHours(-1),
            ToUtc = at.AddHours(1),
            DryRun = false
        });

        await _engine.Received(1).PersistAsync(
            usage.Id,
            Arg.Is<IReadOnlyList<UsageAttribution>>(list => list.Count == 1 && list[0].Id == attribution.Id),
            Arg.Any<CancellationToken>());
    }
}
