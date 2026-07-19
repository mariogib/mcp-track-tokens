using FluentValidation;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using DomainValidationException = McpTrackTokens.Domain.Exceptions.ValidationException;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Manual timesheet start/end and dashboard CRUD.
/// </summary>
public sealed class TimesheetManagementService : ITimesheetManagementService
{
    internal const string AutocreatedNotes = "autocreated";
    internal const string AutoclosedNotes = "autoclosed";

    private readonly IProjectRepository _projects;
    private readonly IProjectDetectionService _projectDetection;
    private readonly ITimesheetEntryRepository _timesheets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateTimesheetEntryRequest> _createValidator;
    private readonly IValidator<UpdateTimesheetEntryRequest> _updateValidator;
    private readonly IValidator<StartTimesheetRequest> _startValidator;
    private readonly IValidator<EndTimesheetRequest> _endValidator;

    public TimesheetManagementService(
        IProjectRepository projects,
        IProjectDetectionService projectDetection,
        ITimesheetEntryRepository timesheets,
        IUnitOfWork unitOfWork,
        IValidator<CreateTimesheetEntryRequest> createValidator,
        IValidator<UpdateTimesheetEntryRequest> updateValidator,
        IValidator<StartTimesheetRequest> startValidator,
        IValidator<EndTimesheetRequest> endValidator)
    {
        _projects = projects;
        _projectDetection = projectDetection;
        _timesheets = timesheets;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _startValidator = startValidator;
        _endValidator = endValidator;
    }

    /// <inheritdoc />
    public async Task<TimesheetEntryDto> StartAsync(
        StartTimesheetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _startValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var project = await ResolveProjectAsync(
            request.ProjectId,
            request.WorkspacePath,
            request.RepositoryPath,
            request.RemoteUrl,
            request.ActiveFilePath,
            createIfMissing: true,
            cancellationToken).ConfigureAwait(false);

        var started = (request.StartedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        await CloseAllOpenEntriesAsync(started, AutoclosedNotes, exceptEntryId: null, cancellationToken)
            .ConfigureAwait(false);

        var entry = TimesheetEntry.Start(project.Id, started, request.Notes);
        await _timesheets.AddAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry);
    }

    /// <inheritdoc />
    public async Task<TimesheetEntryDto> EndAsync(
        EndTimesheetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _endValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        TimesheetEntry? entry = null;
        if (request.TimesheetEntryId is Guid entryId)
        {
            entry = await _timesheets.GetByIdAsync(entryId, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(TimesheetEntry), entryId);
        }
        else
        {
            var project = await ResolveProjectAsync(
                request.ProjectId,
                request.WorkspacePath,
                request.RepositoryPath,
                request.RemoteUrl,
                request.ActiveFilePath,
                createIfMissing: false,
                cancellationToken).ConfigureAwait(false);

            entry = await _timesheets.GetLatestOpenByProjectAsync(project.Id, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new EntityNotFoundException(
                    nameof(TimesheetEntry),
                    $"No open timesheet entry for project {project.Id}.");
        }

        if (entry.EndedAtUtc is not null)
        {
            throw new DomainValidationException(
                nameof(TimesheetEntry.EndedAtUtc),
                "Timesheet entry is already ended.");
        }

        entry.End(request.EndedAtUtc ?? DateTimeOffset.UtcNow, request.AppendNotes);
        await _timesheets.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetEntryDto>> ListForProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        _ = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Project), projectId);

        var list = await _timesheets.ListByProjectAsync(projectId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<TimesheetEntryDto> CreateForProjectAsync(
        Guid projectId,
        CreateTimesheetEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Project), projectId);

        var started = (request.StartedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        await CloseAllOpenEntriesAsync(started, AutoclosedNotes, exceptEntryId: null, cancellationToken)
            .ConfigureAwait(false);

        var entry = TimesheetEntry.Start(projectId, started, request.Notes);
        if (request.EndedAtUtc is DateTimeOffset ended)
        {
            entry.ApplyAdminEdit(started, ended, request.Notes);
        }

        await _timesheets.AddAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry);
    }

    /// <inheritdoc />
    public async Task EnsureAutocreatedOpenEntryAsync(
        Guid projectId,
        DateTimeOffset? startedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            return;
        }

        var openForProject = await _timesheets.ListOpenByProjectAsync(projectId, cancellationToken)
            .ConfigureAwait(false);
        if (openForProject.Count > 0)
        {
            return;
        }

        var started = (startedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        await CloseAllOpenEntriesAsync(started, AutoclosedNotes, exceptEntryId: null, cancellationToken)
            .ConfigureAwait(false);

        var entry = TimesheetEntry.Start(projectId, started, AutocreatedNotes);
        await _timesheets.AddAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TimesheetEntryDto> UpdateAsync(
        Guid entryId,
        UpdateTimesheetEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var entry = await _timesheets.GetByIdAsync(entryId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TimesheetEntry), entryId);

        entry.ApplyAdminEdit(request.StartedAtUtc, request.EndedAtUtc, request.Notes);
        await _timesheets.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _timesheets.GetByIdAsync(entryId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TimesheetEntry), entryId);

        await _timesheets.DeleteAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CloseAllOpenEntriesAsync(
        DateTimeOffset endedAtUtc,
        string? appendNotes,
        Guid? exceptEntryId,
        CancellationToken cancellationToken)
    {
        var open = await _timesheets.ListOpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in open)
        {
            if (exceptEntryId is Guid keep && entry.Id == keep)
            {
                continue;
            }

            var endAt = endedAtUtc < entry.StartedAtUtc ? entry.StartedAtUtc : endedAtUtc;
            entry.End(endAt, appendNotes);
            await _timesheets.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Project> ResolveProjectAsync(
        Guid? projectId,
        string? workspacePath,
        string? repositoryPath,
        string? remoteUrl,
        string? activeFilePath,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        if (projectId is Guid id)
        {
            return await _projects.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(Project), id);
        }

        var detected = await _projectDetection
            .DetectAsync(
                workspacePath,
                repositoryPath,
                remoteUrl,
                activeFilePath,
                createIfMissing,
                cancellationToken)
            .ConfigureAwait(false);

        return detected
            ?? throw new DomainValidationException(
                "ProjectId",
                "Could not resolve a project from the current workspace. Register the project or pass projectId.");
    }

    private static TimesheetEntryDto ToDto(TimesheetEntry entry) => new()
    {
        Id = entry.Id,
        ProjectId = entry.ProjectId,
        StartedAtUtc = entry.StartedAtUtc,
        EndedAtUtc = entry.EndedAtUtc,
        Notes = entry.Notes,
        IsOpen = entry.EndedAtUtc is null,
        CreatedAtUtc = entry.CreatedAtUtc,
        UpdatedAtUtc = entry.UpdatedAtUtc
    };
}
