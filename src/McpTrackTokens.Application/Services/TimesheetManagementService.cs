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
    internal const string DayBoundaryNotes = "day-boundary";

    private readonly IProjectRepository _projects;
    private readonly IProjectDetectionService _projectDetection;
    private readonly ITimesheetEntryRepository _timesheets;
    private readonly ITimesheetCategoryRepository _categories;
    private readonly ISessionRepository _sessions;
    private readonly IActivityEventRepository _events;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateTimesheetEntryRequest> _createValidator;
    private readonly IValidator<UpdateTimesheetEntryRequest> _updateValidator;
    private readonly IValidator<StartTimesheetRequest> _startValidator;
    private readonly IValidator<EndTimesheetRequest> _endValidator;

    public TimesheetManagementService(
        IProjectRepository projects,
        IProjectDetectionService projectDetection,
        ITimesheetEntryRepository timesheets,
        ITimesheetCategoryRepository categories,
        ISessionRepository sessions,
        IActivityEventRepository events,
        IUnitOfWork unitOfWork,
        IValidator<CreateTimesheetEntryRequest> createValidator,
        IValidator<UpdateTimesheetEntryRequest> updateValidator,
        IValidator<StartTimesheetRequest> startValidator,
        IValidator<EndTimesheetRequest> endValidator)
    {
        _projects = projects;
        _projectDetection = projectDetection;
        _timesheets = timesheets;
        _categories = categories;
        _sessions = sessions;
        _events = events;
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

        var category = await ResolveCategoryAsync(
            request.CategoryId,
            request.Category,
            requireActive: true,
            cancellationToken).ConfigureAwait(false);

        var started = (request.StartedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        await CloseAllOpenEntriesAsync(started, AutoclosedNotes, exceptEntryId: null, cancellationToken)
            .ConfigureAwait(false);

        var entry = TimesheetEntry.Start(project.Id, category.Id, started, request.Notes);
        await _timesheets.AddAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, category.Name, project.Name);
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

        var fallback = (request.EndedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var endedAt = request.EndedAtUtc is not null
            ? fallback
            : await ResolveCloseAtAsync(entry, fallback, cancellationToken).ConfigureAwait(false);
        entry.End(endedAt, request.AppendNotes);
        await _timesheets.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TimesheetEntryDto>> ListForProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
        => ListAsync(projectId, fromUtc, toUtc, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetEntryDto>> ListAsync(
        Guid? projectId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId is Guid id)
        {
            _ = await _projects.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(Project), id);
        }

        var list = await _timesheets.ListAsync(projectId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        var categoryNames = await LoadCategoryNamesAsync(list.Select(e => e.CategoryId), cancellationToken)
            .ConfigureAwait(false);
        var projectNames = await LoadProjectNamesAsync(list.Select(e => e.ProjectId), cancellationToken)
            .ConfigureAwait(false);
        return list
            .Select(e => ToDto(
                e,
                categoryNames.GetValueOrDefault(e.CategoryId, string.Empty),
                projectNames.GetValueOrDefault(e.ProjectId, string.Empty)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PagedResultDto<TimesheetEntryDto>> ListPagedAsync(
        TimesheetEntryPageFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.ProjectId is Guid id)
        {
            _ = await _projects.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(Project), id);
        }

        var totalCount = await _timesheets.CountAsync(filter, cancellationToken).ConfigureAwait(false);
        var list = await _timesheets
            .ListPagedAsync(filter, pageIndex, pageSize, cancellationToken)
            .ConfigureAwait(false);
        var categoryNames = await LoadCategoryNamesAsync(list.Select(e => e.CategoryId), cancellationToken)
            .ConfigureAwait(false);
        var projectNames = await LoadProjectNamesAsync(list.Select(e => e.ProjectId), cancellationToken)
            .ConfigureAwait(false);
        var (skip, take) = NormalizePage(pageIndex, pageSize);
        _ = skip;

        return new PagedResultDto<TimesheetEntryDto>
        {
            Items = list
                .Select(e => ToDto(
                    e,
                    categoryNames.GetValueOrDefault(e.CategoryId, string.Empty),
                    projectNames.GetValueOrDefault(e.ProjectId, string.Empty)))
                .ToList(),
            PageIndex = Math.Max(0, pageIndex),
            PageSize = take,
            TotalCount = totalCount
        };
    }

    private static (int Skip, int Take) NormalizePage(int pageIndex, int pageSize)
    {
        var size = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, 100);
        var index = Math.Max(0, pageIndex);
        return (index * size, size);
    }

    /// <inheritdoc />
    public async Task<TimesheetEntryDto> CreateForProjectAsync(
        Guid projectId,
        CreateTimesheetEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Project), projectId);

        var category = await ResolveCategoryAsync(
            request.CategoryId,
            request.Category,
            requireActive: true,
            cancellationToken).ConfigureAwait(false);

        var started = (request.StartedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        await CloseAllOpenEntriesAsync(started, AutoclosedNotes, exceptEntryId: null, cancellationToken)
            .ConfigureAwait(false);

        var entry = TimesheetEntry.Start(projectId, category.Id, started, request.Notes);
        if (request.EndedAtUtc is DateTimeOffset ended)
        {
            entry.ApplyAdminEdit(category.Id, started, ended, request.Notes);
        }

        await _timesheets.AddAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, category.Name, project.Name);
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

        var activityAt = (startedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var activityDay = DateOnly.FromDateTime(activityAt.UtcDateTime);
        var openForProject = await _timesheets.ListOpenByProjectAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        if (openForProject.Count > 0)
        {
            var crossedDay = false;
            foreach (var open in openForProject.OrderBy(e => e.StartedAtUtc))
            {
                var startDay = DateOnly.FromDateTime(open.StartedAtUtc.UtcDateTime);
                if (startDay >= activityDay)
                {
                    continue;
                }

                var closeAt = await ResolveDayBoundaryCloseAsync(projectId, open, startDay, cancellationToken)
                    .ConfigureAwait(false);
                open.End(closeAt, DayBoundaryNotes);
                await _timesheets.UpdateAsync(open, cancellationToken).ConfigureAwait(false);
                crossedDay = true;
            }

            if (!crossedDay)
            {
                return;
            }
        }

        await CloseAllOpenEntriesAsync(activityAt, AutoclosedNotes, exceptEntryId: null, cancellationToken)
            .ConfigureAwait(false);

        var category = await ResolveCategoryAsync(
            TimesheetCategory.WorkId,
            categoryName: null,
            requireActive: false,
            cancellationToken).ConfigureAwait(false);

        var entry = TimesheetEntry.Start(projectId, category.Id, activityAt, AutocreatedNotes);
        await _timesheets.AddAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DateTimeOffset> ResolveDayBoundaryCloseAsync(
        Guid projectId,
        TimesheetEntry open,
        DateOnly startDay,
        CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(startDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var sessionEnd = await TryGetLastSessionEndedAtOnDayAsync(projectId, startDay, cancellationToken)
            .ConfigureAwait(false);
        if (sessionEnd is DateTimeOffset fromSession)
        {
            return ClampCloseAt(open.StartedAtUtc, fromSession, dayEnd);
        }

        var from = open.StartedAtUtc > dayStart ? open.StartedAtUtc : dayStart;
        var lastPrompt = await _events
            .GetLatestPromptTimestampForProjectAsync(projectId, from, dayEnd, cancellationToken)
            .ConfigureAwait(false);

        var closeAt = lastPrompt?.ToUniversalTime() ?? open.StartedAtUtc;
        return ClampCloseAt(open.StartedAtUtc, closeAt, dayEnd);
    }

    /// <summary>
    /// When ending a timesheet without an explicit end time, use the last ended editor session
    /// for the project on the timesheet's start calendar day (UTC); otherwise <paramref name="fallbackUtc"/>.
    /// </summary>
    private async Task<DateTimeOffset> ResolveCloseAtAsync(
        TimesheetEntry entry,
        DateTimeOffset fallbackUtc,
        CancellationToken cancellationToken)
    {
        var startDay = DateOnly.FromDateTime(entry.StartedAtUtc.UtcDateTime);
        var sessionEnd = await TryGetLastSessionEndedAtOnDayAsync(entry.ProjectId, startDay, cancellationToken)
            .ConfigureAwait(false);
        var closeAt = sessionEnd ?? fallbackUtc.ToUniversalTime();
        if (closeAt < entry.StartedAtUtc)
        {
            closeAt = entry.StartedAtUtc;
        }

        return closeAt;
    }

    private async Task<DateTimeOffset?> TryGetLastSessionEndedAtOnDayAsync(
        Guid projectId,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var sessions = await _sessions
            .ListByProjectAsync(projectId, dayStart, dayEnd, cancellationToken)
            .ConfigureAwait(false);

        var lastEnded = sessions
            .Where(s => s.EndedAtUtc is not null)
            .Where(s =>
            {
                var startedOnDay = s.StartedAtUtc >= dayStart && s.StartedAtUtc < dayEnd;
                var ended = s.EndedAtUtc!.Value.ToUniversalTime();
                var endedOnDay = ended >= dayStart && ended < dayEnd;
                return startedOnDay || endedOnDay;
            })
            .OrderByDescending(s => s.StartedAtUtc)
            .ThenByDescending(s => s.EndedAtUtc)
            .FirstOrDefault();

        return lastEnded?.EndedAtUtc?.ToUniversalTime();
    }

    private static DateTimeOffset ClampCloseAt(
        DateTimeOffset startedAtUtc,
        DateTimeOffset closeAtUtc,
        DateTimeOffset dayEndExclusive)
    {
        var closeAt = closeAtUtc.ToUniversalTime();
        if (closeAt < startedAtUtc)
        {
            closeAt = startedAtUtc;
        }

        if (closeAt >= dayEndExclusive)
        {
            closeAt = dayEndExclusive.AddTicks(-1);
            if (closeAt < startedAtUtc)
            {
                closeAt = startedAtUtc;
            }
        }

        return closeAt;
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

        var category = await ResolveCategoryAsync(
            request.CategoryId,
            categoryName: null,
            requireActive: false,
            cancellationToken).ConfigureAwait(false);

        entry.ApplyAdminEdit(category.Id, request.StartedAtUtc, request.EndedAtUtc, request.Notes);
        await _timesheets.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, category.Name);
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

            var endAt = await ResolveCloseAtAsync(entry, endedAtUtc, cancellationToken)
                .ConfigureAwait(false);
            entry.End(endAt, appendNotes);
            await _timesheets.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<TimesheetCategory> ResolveCategoryAsync(
        Guid? categoryId,
        string? categoryName,
        bool requireActive,
        CancellationToken cancellationToken)
    {
        TimesheetCategory? category = null;
        if (categoryId is Guid id && id != Guid.Empty)
        {
            category = await _categories.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(TimesheetCategory), id);
        }
        else if (!string.IsNullOrWhiteSpace(categoryName))
        {
            category = await _categories.GetByNameAsync(categoryName, cancellationToken).ConfigureAwait(false)
                ?? throw new DomainValidationException(
                    "Category",
                    $"Unknown timesheet category '{categoryName.Trim()}'.");
        }
        else
        {
            category = await _categories.GetByIdAsync(TimesheetCategory.WorkId, cancellationToken)
                .ConfigureAwait(false)
                ?? await _categories.GetByNameAsync("Work", cancellationToken).ConfigureAwait(false)
                ?? (await _categories.ListAsync(activeOnly: true, cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault();
        }

        if (category is null)
        {
            throw new DomainValidationException(
                "Category",
                "No timesheet category is available. Add one under Settings → Data.");
        }

        if (requireActive && !category.IsActive)
        {
            throw new DomainValidationException(
                "Category",
                $"Timesheet category '{category.Name}' is inactive.");
        }

        return category;
    }

    private async Task<Dictionary<Guid, string>> LoadCategoryNamesAsync(
        IEnumerable<Guid> categoryIds,
        CancellationToken cancellationToken)
    {
        var ids = categoryIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var all = await _categories.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        return all
            .Where(c => ids.Contains(c.Id))
            .ToDictionary(c => c.Id, c => c.Name);
    }

    private async Task<Dictionary<Guid, string>> LoadProjectNamesAsync(
        IEnumerable<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var ids = projectIds.Where(id => id != Guid.Empty).Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var all = await _projects.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        return all
            .Where(p => ids.Contains(p.Id))
            .ToDictionary(p => p.Id, p => p.Name);
    }

    private async Task<TimesheetEntryDto> ToDtoAsync(
        TimesheetEntry entry,
        CancellationToken cancellationToken)
    {
        var category = await _categories.GetByIdAsync(entry.CategoryId, cancellationToken)
            .ConfigureAwait(false);
        var project = await _projects.GetByIdAsync(entry.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        return ToDto(entry, category?.Name ?? string.Empty, project?.Name ?? string.Empty);
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

    private static TimesheetEntryDto ToDto(
        TimesheetEntry entry,
        string categoryName,
        string projectName = "") => new()
    {
        Id = entry.Id,
        ProjectId = entry.ProjectId,
        ProjectName = projectName,
        CategoryId = entry.CategoryId,
        CategoryName = categoryName,
        StartedAtUtc = entry.StartedAtUtc,
        EndedAtUtc = entry.EndedAtUtc,
        Notes = entry.Notes,
        IsOpen = entry.EndedAtUtc is null,
        CreatedAtUtc = entry.CreatedAtUtc,
        UpdatedAtUtc = entry.UpdatedAtUtc
    };
}
