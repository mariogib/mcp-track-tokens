using FluentAssertions;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Services;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class ActivityWindowServiceTests
{
    private readonly IActivityEventRepository _events = Substitute.For<IActivityEventRepository>();
    private readonly IActivityWindowRepository _windows = Substitute.For<IActivityWindowRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TrackingOptions _options = new() { InactivityThresholdMinutes = 15 };

    private ActivityWindowService CreateSut()
        => new(_events, _windows, _unitOfWork, new ActivityWindowCalculator(), Microsoft.Extensions.Options.Options.Create(_options));

    [Fact]
    public void MergeOverlappingSameProjectWindows_merges_overlapping_windows()
    {
        var projectId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-07-17T09:00:00Z");
        var windows = new[]
        {
            ActivityWindow.Create(start, start.AddMinutes(20), 15, projectId),
            ActivityWindow.Create(start.AddMinutes(15), start.AddMinutes(40), 15, projectId)
        };

        var sut = CreateSut();
        var merged = sut.MergeOverlappingSameProjectWindows(windows);

        merged.Should().HaveCount(1);
        merged[0].StartedAtUtc.Should().Be(start);
        merged[0].EndedAtUtc.Should().Be(start.AddMinutes(40));
        merged[0].ProjectId.Should().Be(projectId);
    }

    [Fact]
    public void MergeOverlappingSameProjectWindows_does_not_merge_non_overlapping()
    {
        var projectId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-07-17T09:00:00Z");
        var windows = new[]
        {
            ActivityWindow.Create(start, start.AddMinutes(10), 15, projectId),
            ActivityWindow.Create(start.AddMinutes(30), start.AddMinutes(45), 15, projectId)
        };

        var sut = CreateSut();
        var merged = sut.MergeOverlappingSameProjectWindows(windows);

        merged.Should().HaveCount(2);
    }

    [Fact]
    public async Task RecalculateAsync_splits_on_inactivity_gap()
    {
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var day = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
        var events = new[]
        {
            PromptActivityEvent.Create(
                ActivityEventType.PromptSubmitted,
                EditorType.Cursor,
                day.AddHours(9),
                projectId,
                sessionId),
            PromptActivityEvent.Create(
                ActivityEventType.PromptSubmitted,
                EditorType.Cursor,
                day.AddHours(9).AddMinutes(8),
                projectId,
                sessionId),
            PromptActivityEvent.Create(
                ActivityEventType.AgentCompleted,
                EditorType.Cursor,
                day.AddHours(9).AddMinutes(14),
                projectId,
                sessionId),
            PromptActivityEvent.Create(
                ActivityEventType.PromptSubmitted,
                EditorType.Cursor,
                day.AddHours(9).AddMinutes(31),
                projectId,
                sessionId)
        };

        _events.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), projectId, Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(events);

        IReadOnlyList<ActivityWindow>? persisted = null;
        _windows.AddRangeAsync(Arg.Any<IEnumerable<ActivityWindow>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                persisted = ci.Arg<IEnumerable<ActivityWindow>>().ToList();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        var result = await sut.RecalculateAsync(
            projectId,
            day,
            day.AddDays(1),
            inactivityThresholdMinutes: 15);

        result.WindowCount.Should().Be(2);
        persisted.Should().NotBeNull();
        var windows = persisted!;
        windows.Should().HaveCount(2);
        windows[0].StartedAtUtc.Should().Be(day.AddHours(9));
        windows[0].EndedAtUtc.Should().Be(day.AddHours(9).AddMinutes(29));
        windows[1].StartedAtUtc.Should().Be(day.AddHours(9).AddMinutes(31));
        windows[1].EndedAtUtc.Should().Be(day.AddHours(9).AddMinutes(46));
    }
}
