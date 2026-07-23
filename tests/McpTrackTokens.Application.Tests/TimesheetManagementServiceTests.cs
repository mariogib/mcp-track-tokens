using FluentAssertions;
using FluentValidation;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class TimesheetManagementServiceTests
{
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly IProjectDetectionService _projectDetection = Substitute.For<IProjectDetectionService>();
    private readonly ITimesheetEntryRepository _timesheets = Substitute.For<ITimesheetEntryRepository>();
    private readonly ITimesheetCategoryRepository _categories = Substitute.For<ITimesheetCategoryRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IActivityEventRepository _events = Substitute.For<IActivityEventRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private TimesheetManagementService CreateSut()
    {
        _sessions.ListByProjectAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        return new(
            _projects,
            _projectDetection,
            _timesheets,
            _categories,
            _sessions,
            _events,
            _unitOfWork,
            Substitute.For<IValidator<CreateTimesheetEntryRequest>>(),
            Substitute.For<IValidator<UpdateTimesheetEntryRequest>>(),
            Substitute.For<IValidator<StartTimesheetRequest>>(),
            Substitute.For<IValidator<EndTimesheetRequest>>());
    }
    [Fact]
    public async Task EnsureAutocreated_same_day_open_does_not_create_another()
    {
        var projectId = Guid.NewGuid();
        var open = TimesheetEntry.Start(
            projectId,
            TimesheetCategory.WorkId,
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"),
            "autocreated");

        _timesheets.ListOpenByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns([open]);

        var sut = CreateSut();
        await sut.EnsureAutocreatedOpenEntryAsync(
            projectId,
            DateTimeOffset.Parse("2026-07-20T18:00:00Z"));

        await _timesheets.DidNotReceive().AddAsync(Arg.Any<TimesheetEntry>(), Arg.Any<CancellationToken>());
        await _timesheets.DidNotReceive().UpdateAsync(Arg.Any<TimesheetEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureAutocreated_cross_day_closes_at_last_prompt_and_opens_new()
    {
        var projectId = Guid.NewGuid();
        var open = TimesheetEntry.Start(
            projectId,
            TimesheetCategory.WorkId,
            DateTimeOffset.Parse("2026-07-20T09:00:00Z"),
            "autocreated");
        var lastPrompt = DateTimeOffset.Parse("2026-07-20T17:30:00Z");
        var nextDayActivity = DateTimeOffset.Parse("2026-07-21T08:15:00Z");

        _timesheets.ListOpenByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns([open]);
        _timesheets.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _events.GetLatestPromptTimestampForProjectAsync(
                projectId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(lastPrompt);
        _categories.GetByIdAsync(TimesheetCategory.WorkId, Arg.Any<CancellationToken>())
            .Returns(TimesheetCategory.Create("Work", sortOrder: 0, id: TimesheetCategory.WorkId));

        TimesheetEntry? added = null;
        _timesheets.AddAsync(Arg.Any<TimesheetEntry>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                added = ci.Arg<TimesheetEntry>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        await sut.EnsureAutocreatedOpenEntryAsync(projectId, nextDayActivity);

        open.EndedAtUtc.Should().Be(lastPrompt);
        open.Notes.Should().Contain("day-boundary");
        await _timesheets.Received(1).UpdateAsync(open, Arg.Any<CancellationToken>());

        added.Should().NotBeNull();
        added!.ProjectId.Should().Be(projectId);
        added.StartedAtUtc.Should().Be(nextDayActivity);
        added.EndedAtUtc.Should().BeNull();
        added.Notes.Should().Be("autocreated");
    }

    [Fact]
    public async Task EnsureAutocreated_cross_day_without_prompts_closes_at_start()
    {
        var projectId = Guid.NewGuid();
        var started = DateTimeOffset.Parse("2026-07-20T09:00:00Z");
        var open = TimesheetEntry.Start(
            projectId,
            TimesheetCategory.WorkId,
            started,
            "autocreated");

        _timesheets.ListOpenByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns([open]);
        _timesheets.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _events.GetLatestPromptTimestampForProjectAsync(
                projectId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        _categories.GetByIdAsync(TimesheetCategory.WorkId, Arg.Any<CancellationToken>())
            .Returns(TimesheetCategory.Create("Work", sortOrder: 0, id: TimesheetCategory.WorkId));

        var sut = CreateSut();
        await sut.EnsureAutocreatedOpenEntryAsync(
            projectId,
            DateTimeOffset.Parse("2026-07-21T08:00:00Z"));

        open.EndedAtUtc.Should().Be(started);
        open.Notes.Should().Contain("day-boundary");
    }

    [Fact]
    public async Task EndAsync_without_explicit_end_uses_last_session_end_on_start_day()
    {
        var projectId = Guid.NewGuid();
        var started = DateTimeOffset.Parse("2026-07-20T09:00:00Z");
        var sessionEnded = DateTimeOffset.Parse("2026-07-20T16:45:00Z");
        var open = TimesheetEntry.Start(projectId, TimesheetCategory.WorkId, started, "work");

        _timesheets.GetLatestOpenByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(open);
        _categories.GetByIdAsync(TimesheetCategory.WorkId, Arg.Any<CancellationToken>())
            .Returns(TimesheetCategory.Create("Work", sortOrder: 0, id: TimesheetCategory.WorkId));
        _projects.GetByIdAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(Project.Create("Demo", "demo", id: projectId));

        var olderSession = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"),
            projectId: projectId);
        olderSession.TransitionTo(SessionStatus.Ended, DateTimeOffset.Parse("2026-07-20T12:00:00Z"));

        var lastSession = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-20T13:00:00Z"),
            projectId: projectId);
        lastSession.TransitionTo(SessionStatus.Ended, sessionEnded);

        var sut = CreateSut();
        _sessions.ListByProjectAsync(
                projectId,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns([lastSession, olderSession]);
        var result = await sut.EndAsync(new EndTimesheetRequest { ProjectId = projectId });

        result.EndedAtUtc.Should().Be(sessionEnded);
        open.EndedAtUtc.Should().Be(sessionEnded);
    }

    [Fact]
    public async Task EndAsync_respects_explicit_ended_at()
    {
        var projectId = Guid.NewGuid();
        var started = DateTimeOffset.Parse("2026-07-20T09:00:00Z");
        var explicitEnd = DateTimeOffset.Parse("2026-07-20T18:00:00Z");
        var open = TimesheetEntry.Start(projectId, TimesheetCategory.WorkId, started);

        _timesheets.GetLatestOpenByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(open);
        _categories.GetByIdAsync(TimesheetCategory.WorkId, Arg.Any<CancellationToken>())
            .Returns(TimesheetCategory.Create("Work", sortOrder: 0, id: TimesheetCategory.WorkId));
        _projects.GetByIdAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(Project.Create("Demo", "demo", id: projectId));

        var session = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"),
            projectId: projectId);
        session.TransitionTo(SessionStatus.Ended, DateTimeOffset.Parse("2026-07-20T12:00:00Z"));

        var sut = CreateSut();
        _sessions.ListByProjectAsync(
                projectId,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns([session]);
        await sut.EndAsync(new EndTimesheetRequest
        {
            ProjectId = projectId,
            EndedAtUtc = explicitEnd
        });

        open.EndedAtUtc.Should().Be(explicitEnd);
    }

    [Fact]
    public async Task EnsureAutocreated_cross_day_prefers_session_end_over_prompt()
    {
        var projectId = Guid.NewGuid();
        var open = TimesheetEntry.Start(
            projectId,
            TimesheetCategory.WorkId,
            DateTimeOffset.Parse("2026-07-20T09:00:00Z"),
            "autocreated");
        var sessionEnded = DateTimeOffset.Parse("2026-07-20T16:00:00Z");
        var lastPrompt = DateTimeOffset.Parse("2026-07-20T17:30:00Z");

        _timesheets.ListOpenByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns([open]);
        _timesheets.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _events.GetLatestPromptTimestampForProjectAsync(
                projectId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(lastPrompt);
        _categories.GetByIdAsync(TimesheetCategory.WorkId, Arg.Any<CancellationToken>())
            .Returns(TimesheetCategory.Create("Work", sortOrder: 0, id: TimesheetCategory.WorkId));

        var session = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"),
            projectId: projectId);
        session.TransitionTo(SessionStatus.Ended, sessionEnded);

        var sut = CreateSut();
        _sessions.ListByProjectAsync(
                projectId,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns([session]);
        await sut.EnsureAutocreatedOpenEntryAsync(
            projectId,
            DateTimeOffset.Parse("2026-07-21T08:15:00Z"));

        open.EndedAtUtc.Should().Be(sessionEnded);
        open.Notes.Should().Contain("day-boundary");
    }
}
