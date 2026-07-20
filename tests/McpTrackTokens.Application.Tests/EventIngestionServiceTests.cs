using FluentAssertions;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Application.Validators;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class EventIngestionServiceTests
{
    private readonly IActivityEventRepository _events = Substitute.For<IActivityEventRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly IProjectDetectionService _projectDetection = Substitute.For<IProjectDetectionService>();
    private readonly IActivityWindowService _activityWindows = Substitute.For<IActivityWindowService>();
    private readonly IContentEncryptionService _encryption = Substitute.For<IContentEncryptionService>();
    private readonly ITimesheetManagementService _timesheets = Substitute.For<ITimesheetManagementService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TrackingOptions _options = new()
    {
        StorePromptContent = false,
        EnablePromptHashing = true,
        SessionInactivityCloseMinutes = 60
    };

    private EventIngestionService CreateSut()
        => new(
            _events,
            _sessions,
            _projects,
            _projectDetection,
            _activityWindows,
            _encryption,
            _timesheets,
            _unitOfWork,
            new IngestEventDtoValidator(),
            Microsoft.Extensions.Options.Options.Create(_options));

    private static IngestEventDto CreateDto(
        string? externalEventId = "evt-1",
        string? promptContent = null,
        string? workspacePath = @"C:\work\repo",
        string? externalSessionId = "session-1",
        DateTimeOffset? timestampUtc = null)
        => new()
        {
            EventType = nameof(ActivityEventType.PromptSubmitted),
            Editor = nameof(EditorType.Cursor),
            TimestampUtc = timestampUtc ?? DateTimeOffset.Parse("2026-07-17T09:00:00Z"),
            ExternalEventId = externalEventId,
            ExternalSessionId = externalSessionId,
            PromptContent = promptContent,
            WorkspacePath = workspacePath
        };

    [Fact]
    public async Task IngestAsync_persists_new_event()
    {
        var sut = CreateSut();
        PromptActivityEvent? saved = null;
        _events.AddAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                saved = ci.Arg<PromptActivityEvent>();
                return Task.CompletedTask;
            });

        var result = await sut.IngestAsync(CreateDto());

        result.WasDuplicate.Should().BeFalse();
        result.EventId.Should().NotBe(Guid.Empty);
        saved.Should().NotBeNull();
        saved!.EditorSessionId.Should().NotBeNull();
        await _sessions.Received(1).AddAsync(Arg.Any<EditorSession>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _activityWindows.Received(1).UpdateForEventAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_returns_duplicate_for_existing_external_id()
    {
        var existing = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            DateTimeOffset.UtcNow,
            externalEventId: "evt-1",
            projectId: Guid.NewGuid(),
            editorSessionId: Guid.NewGuid());

        _events.FindByExternalIdAsync("evt-1", EditorType.Cursor, Arg.Any<CancellationToken>())
            .Returns(existing);

        var sut = CreateSut();
        var result = await sut.IngestAsync(CreateDto("evt-1"));

        result.WasDuplicate.Should().BeTrue();
        result.EventId.Should().Be(existing.Id);
        result.ProjectId.Should().Be(existing.ProjectId);
        result.SessionId.Should().Be(existing.EditorSessionId);
        await _events.DidNotReceive().AddAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_does_not_store_prompt_content_by_default()
    {
        var session = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-17T08:00:00Z"),
            workspacePath: @"C:\work\repo",
            externalSessionId: "session-1");
        _sessions.GetActiveForWorkspaceAsync(EditorType.Cursor, @"C:\work\repo", Arg.Any<CancellationToken>())
            .Returns(session);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _events.GetLatestPromptTimestampAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.Parse("2026-07-17T08:55:00Z"));

        PromptActivityEvent? saved = null;
        _events.AddAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                saved = ci.Arg<PromptActivityEvent>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        await sut.IngestAsync(CreateDto(promptContent: "secret user prompt"));

        saved.Should().NotBeNull();
        saved!.PromptContentStored.Should().BeFalse();
        saved.PromptContentEncrypted.Should().BeNull();
        saved.PromptHash.Should().NotBeNullOrEmpty();
        await _encryption.DidNotReceive().EncryptAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_creates_session_when_none_active_even_without_external_session_id()
    {
        PromptActivityEvent? saved = null;
        EditorSession? created = null;
        _events.AddAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                saved = ci.Arg<PromptActivityEvent>();
                return Task.CompletedTask;
            });
        _sessions.AddAsync(Arg.Any<EditorSession>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                created = ci.Arg<EditorSession>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        var result = await sut.IngestAsync(CreateDto(externalSessionId: null));

        created.Should().NotBeNull();
        created!.Status.Should().Be(SessionStatus.Active);
        saved!.EditorSessionId.Should().Be(created.Id);
        result.SessionId.Should().Be(created.Id);
    }

    [Fact]
    public async Task IngestAsync_reuses_fresh_active_workspace_session()
    {
        var session = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-17T08:00:00Z"),
            workspacePath: @"C:\work\repo",
            externalSessionId: "old-ext");
        _sessions.GetActiveForWorkspaceAsync(EditorType.Cursor, @"C:\work\repo", Arg.Any<CancellationToken>())
            .Returns(session);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _events.GetLatestPromptTimestampAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.Parse("2026-07-17T08:50:00Z"));

        PromptActivityEvent? saved = null;
        _events.AddAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                saved = ci.Arg<PromptActivityEvent>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        await sut.IngestAsync(CreateDto(
            externalEventId: "evt-reuse",
            externalSessionId: "new-ext",
            timestampUtc: DateTimeOffset.Parse("2026-07-17T09:00:00Z")));

        saved!.EditorSessionId.Should().Be(session.Id);
        session.ExternalSessionId.Should().Be("new-ext");
        await _sessions.DidNotReceive().AddAsync(Arg.Any<EditorSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_closes_stale_session_at_last_prompt_then_creates_new()
    {
        var lastPromptAt = DateTimeOffset.Parse("2026-07-17T07:00:00Z");
        var now = DateTimeOffset.Parse("2026-07-17T09:00:00Z");
        var stale = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-17T06:00:00Z"),
            workspacePath: @"C:\work\repo",
            externalSessionId: "stale-ext");
        stale.RecordActivity(lastPromptAt);

        _sessions.GetActiveForWorkspaceAsync(EditorType.Cursor, @"C:\work\repo", Arg.Any<CancellationToken>())
            .Returns(stale);
        _sessions.GetByIdAsync(stale.Id, Arg.Any<CancellationToken>()).Returns(stale);
        _events.GetLatestPromptTimestampAsync(stale.Id, Arg.Any<CancellationToken>())
            .Returns(lastPromptAt);

        EditorSession? created = null;
        PromptActivityEvent? saved = null;
        _sessions.AddAsync(Arg.Any<EditorSession>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                created = ci.Arg<EditorSession>();
                return Task.CompletedTask;
            });
        _events.AddAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                saved = ci.Arg<PromptActivityEvent>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        await sut.IngestAsync(CreateDto(externalEventId: "evt-stale", timestampUtc: now));

        stale.Status.Should().Be(SessionStatus.Ended);
        stale.EndedAtUtc.Should().Be(lastPromptAt);
        created.Should().NotBeNull();
        created!.Id.Should().NotBe(stale.Id);
        created.Status.Should().Be(SessionStatus.Active);
        saved!.EditorSessionId.Should().Be(created.Id);
        await _sessions.Received(1).UpdateAsync(stale, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_does_not_attach_to_ended_external_session()
    {
        // Ended row with same external id must not be reused; create a new Active session.
        _sessions.GetActiveForWorkspaceAsync(EditorType.Cursor, @"C:\work\repo", Arg.Any<CancellationToken>())
            .Returns((EditorSession?)null);

        EditorSession? created = null;
        PromptActivityEvent? saved = null;
        _sessions.AddAsync(Arg.Any<EditorSession>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                created = ci.Arg<EditorSession>();
                return Task.CompletedTask;
            });
        _events.AddAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                saved = ci.Arg<PromptActivityEvent>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        await sut.IngestAsync(CreateDto(externalEventId: "evt-ended", externalSessionId: "ended-ext"));

        await _sessions.DidNotReceive()
            .GetByExternalSessionIdAsync(Arg.Any<string>(), Arg.Any<EditorType?>(), Arg.Any<CancellationToken>());
        created.Should().NotBeNull();
        created!.Status.Should().Be(SessionStatus.Active);
        saved!.EditorSessionId.Should().Be(created.Id);
    }

    [Fact]
    public async Task IngestAsync_other_workspace_active_does_not_block_create()
    {
        // GetActiveForWorkspace only returns matching workspace; other workspace Active is invisible here.
        _sessions.GetActiveForWorkspaceAsync(EditorType.Cursor, @"C:\work\repo-a", Arg.Any<CancellationToken>())
            .Returns((EditorSession?)null);

        EditorSession? created = null;
        _sessions.AddAsync(Arg.Any<EditorSession>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                created = ci.Arg<EditorSession>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        await sut.IngestAsync(CreateDto(
            externalEventId: "evt-ws-a",
            workspacePath: @"C:\work\repo-a"));

        created.Should().NotBeNull();
        created!.WorkspacePath.Should().Be(@"C:\work\repo-a");
        await _sessions.Received(1)
            .GetActiveForWorkspaceAsync(EditorType.Cursor, @"C:\work\repo-a", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSessionAsync_reuses_active_workspace_session()
    {
        var active = EditorSession.Start(
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-17T08:00:00Z"),
            workspacePath: @"C:\work\repo",
            externalSessionId: "ext-1");
        _sessions.GetActiveForWorkspaceAsync(EditorType.Cursor, @"C:\work\repo", Arg.Any<CancellationToken>())
            .Returns(active);
        _sessions.GetByIdAsync(active.Id, Arg.Any<CancellationToken>()).Returns(active);

        var sut = CreateSut();
        var result = await sut.StartSessionAsync(new SessionStartDto
        {
            Editor = nameof(EditorType.Cursor),
            WorkspacePath = @"C:\work\repo",
            ExternalSessionId = "ext-2"
        });

        result.Id.Should().Be(active.Id);
        await _sessions.DidNotReceive().AddAsync(Arg.Any<EditorSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_completion_updates_matching_prompt_submitted()
    {
        var started = DateTimeOffset.Parse("2026-07-17T16:00:00Z");
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            started,
            externalEventId: "gen-42",
            externalRequestId: "gen-42",
            projectId: Guid.NewGuid(),
            status: ActivityStatus.Unknown);

        _events.FindByExternalIdAsync("gen-42:AgentCompleted", EditorType.Cursor, Arg.Any<CancellationToken>())
            .Returns((PromptActivityEvent?)null);
        _events.FindByExternalIdAsync("gen-42", EditorType.Cursor, Arg.Any<CancellationToken>())
            .Returns(prompt);

        PromptActivityEvent? added = null;
        _events.AddAsync(Arg.Any<PromptActivityEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                added = ci.Arg<PromptActivityEvent>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        var result = await sut.IngestAsync(new IngestEventDto
        {
            EventType = nameof(ActivityEventType.AgentCompleted),
            Editor = nameof(EditorType.Cursor),
            TimestampUtc = DateTimeOffset.Parse("2026-07-17T16:02:30Z"),
            ExternalEventId = "gen-42:AgentCompleted",
            ExternalRequestId = "gen-42",
            Status = "completed",
            WorkspacePath = @"C:\work\repo"
        });

        result.WasDuplicate.Should().BeFalse();
        prompt.Status.Should().Be(ActivityStatus.Completed);
        prompt.DurationMilliseconds.Should().Be(150_000);
        prompt.ResponseCompletedAtUtc.Should().NotBeNull();
        added.Should().NotBeNull();
        added!.EventType.Should().Be(ActivityEventType.AgentCompleted);
        added.Status.Should().Be(ActivityStatus.Completed);
        await _events.Received().UpdateAsync(prompt, Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().AddAsync(Arg.Any<EditorSession>(), Arg.Any<CancellationToken>());
    }
}
