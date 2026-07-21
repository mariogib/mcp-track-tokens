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

public sealed class SubscriptionAllocationServiceTests
{
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly IActivityEventRepository _events = Substitute.For<IActivityEventRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    private SubscriptionAllocationService CreateSut(TrackingOptions? options = null)
        => new(
            _projects,
            _events,
            _sessions,
            new SubscriptionAllocationCalculator(),
            Microsoft.Extensions.Options.Options.Create(options ?? new TrackingOptions
            {
                CursorSubscriptionAmount = 100m,
                CursorAllocationMethod = AllocationRuleType.ByActiveProjectTime
            }));

    [Fact]
    public async Task AllocateAsync_by_active_time_returns_proportional_shares()
    {
        var projectA = Project.Create("A", "a", id: Guid.NewGuid());
        var projectB = Project.Create("B", "b", id: Guid.NewGuid());
        _projects.ListAsync(true, Arg.Any<CancellationToken>()).Returns([projectA, projectB]);

        _events.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), projectA.Id, Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns([PromptActivityEvent.Create(ActivityEventType.PromptSubmitted, EditorType.Cursor, DateTimeOffset.UtcNow, projectA.Id)]);
        _events.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), projectB.Id, Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns([PromptActivityEvent.Create(ActivityEventType.PromptSubmitted, EditorType.Cursor, DateTimeOffset.UtcNow, projectB.Id)]);

        var from = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        var to = from.AddMonths(1);

        var sessionA = EditorSession.Start(EditorType.Cursor, from, projectA.Id);
        sessionA.TransitionTo(SessionStatus.Ended, from.AddHours(3));
        var sessionB = EditorSession.Start(EditorType.Cursor, from, projectB.Id);
        sessionB.TransitionTo(SessionStatus.Ended, from.AddHours(1));

        _sessions.ListAsync(projectA.Id, from, to, Arg.Any<CancellationToken>()).Returns([sessionA]);
        _sessions.ListAsync(projectB.Id, from, to, Arg.Any<CancellationToken>()).Returns([sessionB]);

        var sut = CreateSut();
        var shares = await sut.AllocateAsync(from, to, AllocationRuleType.ByActiveProjectTime, amount: 10m);

        shares.Should().HaveCount(2);
        shares.Single(s => s.ProjectId == projectA.Id).Percentage.Should().Be(75m);
        shares.Single(s => s.ProjectId == projectB.Id).Percentage.Should().Be(25m);
    }

    [Fact]
    public async Task AllocateAsync_NotAllocated_returns_empty()
    {
        var sut = CreateSut(new TrackingOptions
        {
            CursorSubscriptionAmount = 100m,
            CursorAllocationMethod = AllocationRuleType.NotAllocated
        });

        var shares = await sut.AllocateAsync(
            DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow,
            AllocationRuleType.NotAllocated);

        shares.Should().BeEmpty();
    }
}
