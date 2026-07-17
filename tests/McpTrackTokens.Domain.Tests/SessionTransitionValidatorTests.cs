using FluentAssertions;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.Validation;

namespace McpTrackTokens.Domain.Tests;

public sealed class SessionTransitionValidatorTests
{
    [Theory]
    [InlineData(SessionStatus.Active, SessionStatus.Paused, true)]
    [InlineData(SessionStatus.Active, SessionStatus.Ended, true)]
    [InlineData(SessionStatus.Active, SessionStatus.Abandoned, true)]
    [InlineData(SessionStatus.Paused, SessionStatus.Active, true)]
    [InlineData(SessionStatus.Paused, SessionStatus.Ended, true)]
    [InlineData(SessionStatus.Paused, SessionStatus.Abandoned, true)]
    [InlineData(SessionStatus.Ended, SessionStatus.Active, false)]
    [InlineData(SessionStatus.Ended, SessionStatus.Paused, false)]
    [InlineData(SessionStatus.Abandoned, SessionStatus.Active, false)]
    [InlineData(SessionStatus.Active, SessionStatus.Active, true)]
    public void CanTransition_enforces_allowed_lifecycle(SessionStatus from, SessionStatus to, bool expected)
    {
        SessionTransitionValidator.CanTransition(from, to).Should().Be(expected);
    }

    [Fact]
    public void EnsureCanTransition_throws_for_illegal_transition()
    {
        var act = () => SessionTransitionValidator.EnsureCanTransition(SessionStatus.Ended, SessionStatus.Active);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void GetAllowedTargets_for_active_excludes_self()
    {
        SessionTransitionValidator.GetAllowedTargets(SessionStatus.Active)
            .Should().BeEquivalentTo(
            [
                SessionStatus.Paused,
                SessionStatus.Ended,
                SessionStatus.Abandoned
            ]);
    }

    [Fact]
    public void GetAllowedTargets_for_terminal_is_empty()
    {
        SessionTransitionValidator.GetAllowedTargets(SessionStatus.Ended).Should().BeEmpty();
        SessionTransitionValidator.GetAllowedTargets(SessionStatus.Abandoned).Should().BeEmpty();
    }
}
