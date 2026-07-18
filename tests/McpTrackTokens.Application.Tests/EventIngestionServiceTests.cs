using FluentAssertions;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Application.Validators;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Services;
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
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TrackingOptions _options = new()
    {
        StorePromptContent = false,
        EnablePromptHashing = true
    };

    private EventIngestionService CreateSut()
        => new(
            _events,
            _sessions,
            _projects,
            _projectDetection,
            _activityWindows,
            _encryption,
            _unitOfWork,
            new IngestEventDtoValidator(),
            Microsoft.Extensions.Options.Options.Create(_options));

    private static IngestEventDto CreateDto(string? externalEventId = "evt-1", string? promptContent = null)
        => new()
        {
            EventType = nameof(ActivityEventType.PromptSubmitted),
            Editor = nameof(EditorType.Cursor),
            TimestampUtc = DateTimeOffset.Parse("2026-07-17T09:00:00Z"),
            ExternalEventId = externalEventId,
            ExternalSessionId = "session-1",
            PromptContent = promptContent,
            WorkspacePath = @"C:\work\repo"
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
        var session = EditorSession.Start(EditorType.Cursor, DateTimeOffset.UtcNow, externalSessionId: "session-1");
        _sessions.GetByExternalSessionIdAsync("session-1", EditorType.Cursor, Arg.Any<CancellationToken>())
            .Returns(session);

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
    }
}
