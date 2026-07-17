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
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IActivityEventRepository _events = Substitute.For<IActivityEventRepository>();
    private readonly IActivityWindowRepository _windows = Substitute.For<IActivityWindowRepository>();
    private readonly IUsageAttributionRepository _attributions = Substitute.For<IUsageAttributionRepository>();
    private readonly IExternalUsageRepository _usage = Substitute.For<IExternalUsageRepository>();
    private readonly IPathNormalizer _pathNormalizer = Substitute.For<IPathNormalizer>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private AttributionEngine CreateSut()
        => new(
            _projects,
            _sessions,
            _events,
            _windows,
            _attributions,
            _usage,
            _pathNormalizer,
            _unitOfWork,
            new AllocationRequestDtoValidator());

    private static ExternalUsageRecord CreateUsage(decimal cost = 10m, DateTimeOffset? at = null)
        => ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at ?? DateTimeOffset.Parse("2026-07-17T10:00:00Z"),
            reportedCost: cost,
            totalTokens: 1000);

    [Fact]
    public async Task ProposeAsync_SingleActiveSession_attributes_to_only_project_session()
    {
        var projectId = Guid.NewGuid();
        var session = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.UtcNow.AddHours(-1),
            projectId: projectId);

        _sessions.GetActiveAtAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([session]);
        _windows.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var sut = CreateSut();
        var result = await sut.ProposeAsync(CreateUsage());

        result.Should().HaveCount(1);
        result[0].ProjectId.Should().Be(projectId);
        result[0].AttributionMethod.Should().Be(AttributionMethod.SingleActiveSession);
        result[0].Confidence.Should().Be(AttributionConfidence.High);
        result[0].AllocationPercentage.Should().Be(100m);
    }

    [Fact]
    public async Task ProposeAsync_TimeWindowMatch_when_single_covering_project()
    {
        var projectId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var window = ActivityWindow.Create(at.AddMinutes(-5), at.AddMinutes(10), 15, projectId);

        _sessions.GetActiveAtAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _windows.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([window]);

        var sut = CreateSut();
        var result = await sut.ProposeAsync(CreateUsage(at: at));

        result.Should().HaveCount(1);
        result[0].ProjectId.Should().Be(projectId);
        result[0].AttributionMethod.Should().Be(AttributionMethod.TimeWindowMatch);
        result[0].Confidence.Should().Be(AttributionConfidence.Medium);
    }

    [Fact]
    public async Task ProposeAsync_ProportionalTimeAllocation_for_overlapping_windows()
    {
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        // DurationSeconds derived from timestamps: 3h vs 1h
        var windowA = ActivityWindow.Create(at.AddHours(-3), at.AddHours(0), 15, projectA);
        var windowB = ActivityWindow.Create(at.AddHours(-1), at.AddHours(0), 15, projectB);

        _sessions.GetActiveAtAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _windows.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([windowA, windowB]);

        var sut = CreateSut();
        var result = await sut.ProposeAsync(CreateUsage(10m, at));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(a => a.AttributionMethod == AttributionMethod.ProportionalTimeAllocation);
        result.Should().OnlyContain(a => a.Confidence == AttributionConfidence.Low);
        result.Sum(a => a.AllocatedCost).Should().Be(10m);
        result.Sum(a => a.AllocationPercentage).Should().Be(100m);

        var byProject = result.ToDictionary(a => a.ProjectId!.Value, a => a.AllocatedCost);
        byProject[projectA].Should().Be(7.50m);
        byProject[projectB].Should().Be(2.50m);
    }

    [Fact]
    public async Task ProposeAsync_Unallocated_when_no_match()
    {
        _sessions.GetActiveAtAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _windows.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var sut = CreateSut();
        var result = await sut.ProposeAsync(CreateUsage());

        result.Should().HaveCount(1);
        result[0].ProjectId.Should().BeNull();
        result[0].AttributionMethod.Should().Be(AttributionMethod.Unallocated);
        result[0].Confidence.Should().Be(AttributionConfidence.Unallocated);
        result[0].AllocatedCost.Should().Be(0m);
    }

    [Fact]
    public async Task ProposeAsync_does_not_promote_low_confidence_proportional_to_certain()
    {
        // Covered by ProportionalTimeAllocation confidence assertion above; keep explicit guard.
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-17T12:00:00Z");
        _sessions.GetActiveAtAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        _windows.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                ActivityWindow.Create(at.AddMinutes(-30), at.AddMinutes(5), 15, projectA),
                ActivityWindow.Create(at.AddMinutes(-20), at.AddMinutes(5), 15, projectB)
            ]);

        var result = await CreateSut().ProposeAsync(CreateUsage(at: at));
        result.Should().OnlyContain(a => a.Confidence == AttributionConfidence.Low);
        result.Should().NotContain(a => a.Confidence == AttributionConfidence.Certain);
    }
}
