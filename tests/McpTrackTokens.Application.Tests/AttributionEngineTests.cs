using FluentAssertions;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Application.Validators;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class AttributionEngineTests
{
    private readonly IActivityEventRepository _events = Substitute.For<IActivityEventRepository>();
    private readonly IUsageAttributionRepository _attributions = Substitute.For<IUsageAttributionRepository>();
    private readonly IExternalUsageRepository _usage = Substitute.For<IExternalUsageRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private AttributionEngine CreateSut()
        => new(
            _events,
            _attributions,
            _usage,
            _unitOfWork,
            new AllocationRequestDtoValidator());

    private static ExternalUsageRecord CreateUsage(decimal cost = 10m, DateTimeOffset? at = null)
        => ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at ?? DateTimeOffset.Parse("2026-07-17T10:00:00Z"),
            reportedCost: cost,
            totalTokens: 1000);

    [Fact]
    public async Task ProposeAsync_ClosestPriorPrompt_links_usage_to_prompt_project()
    {
        var projectId = Guid.NewGuid();
        var usageAt = DateTimeOffset.Parse("2026-07-17T10:00:30Z");
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-17T10:00:00Z"),
            projectId: projectId);

        _events.FindClosestPriorPromptWithProjectAsync(usageAt, Arg.Any<CancellationToken>())
            .Returns(prompt);

        var result = await CreateSut().ProposeAsync(CreateUsage(at: usageAt));

        result.Should().HaveCount(1);
        result[0].ProjectId.Should().Be(projectId);
        result[0].ActivityEventId.Should().Be(prompt.Id);
        result[0].AttributionMethod.Should().Be(AttributionMethod.ClosestPromptMatch);
        result[0].Confidence.Should().Be(AttributionConfidence.High);
        result[0].AllocatedCost.Should().Be(10m);
        result[0].AllocatedTotalTokens.Should().Be(1000);
    }

    [Fact]
    public async Task ProposeAsync_NoPriorPrompt_is_unallocated()
    {
        var at = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        _events.FindClosestPriorPromptWithProjectAsync(at, Arg.Any<CancellationToken>())
            .Returns((PromptActivityEvent?)null);

        var result = await CreateSut().ProposeAsync(CreateUsage(at: at));

        result.Should().HaveCount(1);
        result[0].ProjectId.Should().BeNull();
        result[0].ActivityEventId.Should().BeNull();
        result[0].AttributionMethod.Should().Be(AttributionMethod.Unallocated);
    }

    [Fact]
    public async Task ProposeAsync_ZeroCost_still_can_link_when_proposed_directly()
    {
        var projectId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            at,
            projectId: projectId);

        _events.FindClosestPriorPromptWithProjectAsync(at, Arg.Any<CancellationToken>())
            .Returns(prompt);

        var usage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at,
            reportedCost: 0m,
            totalTokens: 3250);

        var result = await CreateSut().ProposeAsync(usage);

        result[0].ProjectId.Should().Be(projectId);
        result[0].ActivityEventId.Should().Be(prompt.Id);
        result[0].AllocatedCost.Should().Be(0m);
        result[0].AllocatedTotalTokens.Should().Be(3250);
    }

    [Fact]
    public async Task ProposeAsync_MultipleUsages_can_link_to_same_prompt()
    {
        var projectId = Guid.NewGuid();
        var promptAt = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            promptAt,
            projectId: projectId);

        var usageAt1 = promptAt.AddSeconds(2);
        var usageAt2 = promptAt.AddSeconds(45);

        _events.FindClosestPriorPromptWithProjectAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(prompt);

        var sut = CreateSut();
        var first = await sut.ProposeAsync(CreateUsage(cost: 1m, at: usageAt1));
        var second = await sut.ProposeAsync(CreateUsage(cost: 2m, at: usageAt2));

        first[0].ActivityEventId.Should().Be(prompt.Id);
        second[0].ActivityEventId.Should().Be(prompt.Id);
        first[0].ProjectId.Should().Be(projectId);
        second[0].ProjectId.Should().Be(projectId);
        first[0].AttributionMethod.Should().Be(AttributionMethod.ClosestPromptMatch);
        second[0].AttributionMethod.Should().Be(AttributionMethod.ClosestPromptMatch);
    }
}
