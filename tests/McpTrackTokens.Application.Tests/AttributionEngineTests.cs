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

    private static ExternalUsageRecord CreateUsage(
        decimal cost = 10m,
        DateTimeOffset? at = null,
        string? model = "claude-4.5-sonnet")
        => ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at ?? DateTimeOffset.Parse("2026-07-17T10:00:00Z"),
            model: model,
            reportedCost: cost,
            totalTokens: 1000);

    [Fact]
    public async Task ProposeAsync_ClosestPriorPrompt_links_usage_to_prompt_project()
    {
        var projectId = Guid.NewGuid();
        var usageAt = DateTimeOffset.Parse("2026-07-17T10:00:30Z");
        var model = "claude-4.5-sonnet";
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-17T10:00:00Z"),
            projectId: projectId,
            model: model);

        _events.FindClosestPriorPromptWithProjectAsync(usageAt, model, Arg.Any<CancellationToken>())
            .Returns(prompt);

        var result = await CreateSut().ProposeAsync(CreateUsage(at: usageAt, model: model));

        result.Should().HaveCount(1);
        result[0].ProjectId.Should().Be(projectId);
        result[0].ActivityEventId.Should().Be(prompt.Id);
        result[0].AttributionMethod.Should().Be(AttributionMethod.ClosestPromptMatch);
        result[0].Confidence.Should().Be(AttributionConfidence.High);
        result[0].AllocatedCost.Should().Be(10m);
        result[0].AllocatedTotalTokens.Should().Be(1000);
        result[0].Reason.Should().Contain(model);
    }

    [Fact]
    public async Task ProposeAsync_NoPriorPrompt_is_unallocated()
    {
        var at = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var model = "claude-4.5-sonnet";
        _events.FindClosestPriorPromptWithProjectAsync(at, model, Arg.Any<CancellationToken>())
            .Returns((PromptActivityEvent?)null);

        var result = await CreateSut().ProposeAsync(CreateUsage(at: at, model: model));

        result.Should().HaveCount(1);
        result[0].ProjectId.Should().BeNull();
        result[0].ActivityEventId.Should().BeNull();
        result[0].AttributionMethod.Should().Be(AttributionMethod.Unallocated);
        result[0].Reason.Should().Contain(model);
    }

    [Fact]
    public async Task ProposeAsync_ZeroCost_still_can_link_when_proposed_directly()
    {
        var projectId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var model = "gpt-5.4";
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            at,
            projectId: projectId,
            model: model);

        _events.FindClosestPriorPromptWithProjectAsync(at, model, Arg.Any<CancellationToken>())
            .Returns(prompt);

        var usage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at,
            model: model,
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
        var model = "claude-4.5-sonnet";
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            promptAt,
            projectId: projectId,
            model: model);

        var usageAt1 = promptAt.AddSeconds(2);
        var usageAt2 = promptAt.AddSeconds(45);

        _events.FindClosestPriorPromptWithProjectAsync(
                Arg.Any<DateTimeOffset>(),
                model,
                Arg.Any<CancellationToken>())
            .Returns(prompt);

        var sut = CreateSut();
        var first = await sut.ProposeAsync(CreateUsage(cost: 1m, at: usageAt1, model: model));
        var second = await sut.ProposeAsync(CreateUsage(cost: 2m, at: usageAt2, model: model));

        first[0].ActivityEventId.Should().Be(prompt.Id);
        second[0].ActivityEventId.Should().Be(prompt.Id);
        first[0].ProjectId.Should().Be(projectId);
        second[0].ProjectId.Should().Be(projectId);
        first[0].AttributionMethod.Should().Be(AttributionMethod.ClosestPromptMatch);
        second[0].AttributionMethod.Should().Be(AttributionMethod.ClosestPromptMatch);
    }

    [Fact]
    public async Task ProposeAsync_Passes_usage_model_to_prompt_lookup()
    {
        var at = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var model = "composer-2";
        _events.FindClosestPriorPromptWithProjectAsync(at, model, Arg.Any<CancellationToken>())
            .Returns((PromptActivityEvent?)null);

        await CreateSut().ProposeAsync(CreateUsage(at: at, model: model));

        await _events.Received(1).FindClosestPriorPromptWithProjectAsync(
            at,
            model,
            Arg.Any<CancellationToken>());
    }
}
