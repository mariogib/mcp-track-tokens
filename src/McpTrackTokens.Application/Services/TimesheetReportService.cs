using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Aggregates timesheet entry durations for dashboard reports.
/// </summary>
public sealed class TimesheetReportService : ITimesheetReportService
{
    private readonly IProjectRepository _projects;
    private readonly ITimesheetEntryRepository _timesheets;
    private readonly ITimesheetCategoryRepository _categories;

    public TimesheetReportService(
        IProjectRepository projects,
        ITimesheetEntryRepository timesheets,
        ITimesheetCategoryRepository categories)
    {
        _projects = projects;
        _timesheets = timesheets;
        _categories = categories;
    }

    /// <inheritdoc />
    public async Task<TimesheetOverallReport> GetOverallReportAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var entries = await _timesheets.ListAsync(null, from, to, cancellationToken).ConfigureAwait(false);
        var projects = await _projects.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var categories = await _categories.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var built = BuildReportData(entries, projects, categories, from, to);

        return new TimesheetOverallReport
        {
            FromUtc = from,
            ToUtc = to,
            Totals = built.Totals,
            ByCategory = built.ByCategory,
            ByProject = built.ByProject,
            ByClient = built.ByClient,
            ByDay = built.ByDay
        };
    }

    /// <inheritdoc />
    public async Task<TimesheetProjectReport> GetProjectReportAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Project), projectId);

        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var entries = await _timesheets.ListAsync(projectId, from, to, cancellationToken).ConfigureAwait(false);
        var categories = await _categories.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var built = BuildReportData(entries, [project], categories, from, to);

        return new TimesheetProjectReport
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            ClientName = project.ClientName,
            FromUtc = from,
            ToUtc = to,
            Totals = built.Totals,
            ByCategory = built.ByCategory,
            ByDay = built.ByDay
        };
    }

    /// <inheritdoc />
    public async Task<TimesheetClientReport> GetClientReportAsync(
        string clientName,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientName))
        {
            throw new Domain.Exceptions.ValidationException(nameof(clientName), "Client name is required.");
        }

        var normalizedClient = clientName.Trim();
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var allProjects = await _projects.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var clientProjects = allProjects
            .Where(p => string.Equals(p.ClientName?.Trim(), normalizedClient, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var projectIds = clientProjects.Select(p => p.Id).ToHashSet();
        var entries = (await _timesheets.ListAsync(null, from, to, cancellationToken).ConfigureAwait(false))
            .Where(e => projectIds.Contains(e.ProjectId))
            .ToList();
        var categories = await _categories.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var built = BuildReportData(entries, clientProjects, categories, from, to);

        return new TimesheetClientReport
        {
            ClientName = normalizedClient,
            FromUtc = from,
            ToUtc = to,
            Totals = built.Totals,
            ByProject = built.ByProject,
            ByCategory = built.ByCategory,
            ByDay = built.ByDay
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TimesheetMonthAvailabilityDto>> ListMonthsWithEntriesAsync(
        Guid? projectId = null,
        string? clientName = null,
        CancellationToken cancellationToken = default)
        => _timesheets.ListMonthsWithEntriesAsync(projectId, clientName, cancellationToken);

    private static (DateTimeOffset From, DateTimeOffset To) NormalizeRange(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();
        if (to < from)
        {
            (from, to) = (to, from);
        }

        return (from, to);
    }

    private static BuiltReport BuildReportData(
        IReadOnlyList<TimesheetEntry> entries,
        IReadOnlyList<Project> projects,
        IReadOnlyList<TimesheetCategory> categories,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        var now = DateTimeOffset.UtcNow;
        var projectById = projects.ToDictionary(p => p.Id);
        var categoryById = categories.ToDictionary(c => c.Id);

        var slices = new List<EntrySlice>();
        foreach (var entry in entries)
        {
            var duration = OverlapSeconds(entry.StartedAtUtc, entry.EndedAtUtc, fromUtc, toUtc, now);
            if (duration <= 0)
            {
                continue;
            }

            projectById.TryGetValue(entry.ProjectId, out var project);
            categoryById.TryGetValue(entry.CategoryId, out var category);
            slices.Add(new EntrySlice(
                entry,
                duration,
                project?.Name ?? string.Empty,
                project?.ClientName,
                category?.Name ?? string.Empty));
        }

        var totals = new TimesheetReportTotals
        {
            TotalDurationSeconds = slices.Sum(s => s.DurationSeconds),
            EntryCount = slices.Count,
            OpenEntryCount = slices.Count(s => s.Entry.EndedAtUtc is null)
        };

        var byCategory = slices
            .GroupBy(s => s.Entry.CategoryId)
            .Select(g => new TimesheetCategoryBreakdownRow
            {
                CategoryId = g.Key,
                CategoryName = g.First().CategoryName,
                DurationSeconds = g.Sum(s => s.DurationSeconds),
                EntryCount = g.Count()
            })
            .OrderByDescending(r => r.DurationSeconds)
            .ThenBy(r => r.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byProject = slices
            .GroupBy(s => s.Entry.ProjectId)
            .Select(g => new TimesheetProjectBreakdownRow
            {
                ProjectId = g.Key,
                ProjectName = g.First().ProjectName,
                ClientName = g.First().ClientName,
                DurationSeconds = g.Sum(s => s.DurationSeconds),
                EntryCount = g.Count()
            })
            .ToList();

        // Include active registered projects with no overlapping timesheet time so client/project
        // rollups match the Projects list (zeros instead of omitting the project).
        var projectsWithTime = byProject.Select(r => r.ProjectId).ToHashSet();
        foreach (var project in projects
                     .Where(p => p.IsActive)
                     .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (projectsWithTime.Contains(project.Id))
            {
                continue;
            }

            byProject.Add(new TimesheetProjectBreakdownRow
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
                ClientName = project.ClientName,
                DurationSeconds = 0,
                EntryCount = 0
            });
        }

        byProject = byProject
            .OrderByDescending(r => r.DurationSeconds)
            .ThenBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projectCountByClient = projects
            .Where(p => p.IsActive && !string.IsNullOrWhiteSpace(p.ClientName))
            .GroupBy(p => p.ClientName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var byClient = slices
            .Where(s => !string.IsNullOrWhiteSpace(s.ClientName))
            .GroupBy(s => s.ClientName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var clientName = g.First().ClientName!.Trim();
                return new TimesheetClientBreakdownRow
                {
                    ClientName = clientName,
                    DurationSeconds = g.Sum(s => s.DurationSeconds),
                    EntryCount = g.Count(),
                    ProjectCount = projectCountByClient.TryGetValue(clientName, out var count)
                        ? count
                        : g.Select(s => s.Entry.ProjectId).Distinct().Count()
                };
            })
            .OrderByDescending(r => r.DurationSeconds)
            .ThenBy(r => r.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byDay = BuildDailyBreakdown(entries, fromUtc, toUtc, now);

        return new BuiltReport(totals, byCategory, byProject, byClient, byDay);
    }

    private static IReadOnlyList<TimesheetDailyBreakdownRow> BuildDailyBreakdown(
        IReadOnlyList<TimesheetEntry> entries,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTimeOffset now)
    {
        var dayStarts = new Dictionary<DateOnly, (long DurationSeconds, HashSet<Guid> EntryIds)>();
        var fromDay = DateOnly.FromDateTime(fromUtc.UtcDateTime);
        var toDay = DateOnly.FromDateTime(toUtc.UtcDateTime);

        for (var day = fromDay; day <= toDay; day = day.AddDays(1))
        {
            dayStarts[day] = (0, []);
        }

        foreach (var entry in entries)
        {
            var entryEnd = entry.EndedAtUtc ?? now;
            var cursorDay = DateOnly.FromDateTime(
                (entry.StartedAtUtc < fromUtc ? fromUtc : entry.StartedAtUtc).UtcDateTime);
            var lastDay = DateOnly.FromDateTime((entryEnd > toUtc ? toUtc : entryEnd).UtcDateTime);

            while (cursorDay <= lastDay)
            {
                if (cursorDay >= fromDay && cursorDay <= toDay)
                {
                    var dayStart = new DateTimeOffset(cursorDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                    var dayEnd = new DateTimeOffset(cursorDay.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
                    var seconds = OverlapSeconds(entry.StartedAtUtc, entry.EndedAtUtc, dayStart, dayEnd, now);
                    if (seconds > 0)
                    {
                        var current = dayStarts[cursorDay];
                        current.EntryIds.Add(entry.Id);
                        dayStarts[cursorDay] = (current.DurationSeconds + seconds, current.EntryIds);
                    }
                }

                cursorDay = cursorDay.AddDays(1);
            }
        }

        return dayStarts
            .Select(kvp => new TimesheetDailyBreakdownRow
            {
                Day = kvp.Key,
                DurationSeconds = kvp.Value.DurationSeconds,
                EntryCount = kvp.Value.EntryIds.Count
            })
            .OrderByDescending(r => r.Day)
            .ToList();
    }

    private static long OverlapSeconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTimeOffset now)
    {
        var effectiveStart = startedAtUtc.ToUniversalTime();
        if (effectiveStart < fromUtc)
        {
            effectiveStart = fromUtc;
        }

        var effectiveEnd = (endedAtUtc ?? now).ToUniversalTime();
        if (effectiveEnd > toUtc)
        {
            effectiveEnd = toUtc;
        }

        if (effectiveEnd <= effectiveStart)
        {
            return 0;
        }

        return (long)Math.Floor((effectiveEnd - effectiveStart).TotalSeconds);
    }

    private sealed record EntrySlice(
        TimesheetEntry Entry,
        long DurationSeconds,
        string ProjectName,
        string? ClientName,
        string CategoryName);

    private sealed record BuiltReport(
        TimesheetReportTotals Totals,
        IReadOnlyList<TimesheetCategoryBreakdownRow> ByCategory,
        IReadOnlyList<TimesheetProjectBreakdownRow> ByProject,
        IReadOnlyList<TimesheetClientBreakdownRow> ByClient,
        IReadOnlyList<TimesheetDailyBreakdownRow> ByDay);
}
