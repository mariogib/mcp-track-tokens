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
    private readonly TimeZoneInfo _calendarTimeZone;

    public TimesheetReportService(
        IProjectRepository projects,
        ITimesheetEntryRepository timesheets,
        ITimesheetCategoryRepository categories,
        TimeZoneInfo? calendarTimeZone = null)
    {
        _projects = projects;
        _timesheets = timesheets;
        _categories = categories;
        _calendarTimeZone = calendarTimeZone ?? TimeZoneInfo.Local;
    }

    /// <inheritdoc />
    public async Task<TimesheetOverallReport> GetOverallReportAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? timeZoneOffsetMinutes = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var calendar = ResolveCalendarTimeZone(timeZoneOffsetMinutes);
        var entries = await _timesheets.ListAsync(null, from, to, cancellationToken).ConfigureAwait(false);
        var projects = await _projects.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var categories = await _categories.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var built = BuildReportData(entries, projects, categories, from, to, calendar);

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
        int? timeZoneOffsetMinutes = null,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Project), projectId);

        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var calendar = ResolveCalendarTimeZone(timeZoneOffsetMinutes);
        var entries = await _timesheets.ListAsync(projectId, from, to, cancellationToken).ConfigureAwait(false);
        var categories = await _categories.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var built = BuildReportData(entries, [project], categories, from, to, calendar);

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
        int? timeZoneOffsetMinutes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientName))
        {
            throw new Domain.Exceptions.ValidationException(nameof(clientName), "Client name is required.");
        }

        var normalizedClient = clientName.Trim();
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var calendar = ResolveCalendarTimeZone(timeZoneOffsetMinutes);
        var allProjects = await _projects.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var clientProjects = allProjects
            .Where(p => string.Equals(p.ClientName?.Trim(), normalizedClient, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var projectIds = clientProjects.Select(p => p.Id).ToHashSet();
        var entries = (await _timesheets.ListAsync(null, from, to, cancellationToken).ConfigureAwait(false))
            .Where(e => projectIds.Contains(e.ProjectId))
            .ToList();
        var categories = await _categories.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        var built = BuildReportData(entries, clientProjects, categories, from, to, calendar);

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

    private BuiltReport BuildReportData(
        IReadOnlyList<TimesheetEntry> entries,
        IReadOnlyList<Project> projects,
        IReadOnlyList<TimesheetCategory> categories,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeZoneInfo calendarTimeZone)
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

        var byDay = BuildDailyBreakdown(entries, fromUtc, toUtc, now, calendarTimeZone);

        return new BuiltReport(totals, byCategory, byProject, byClient, byDay);
    }

    /// <summary>
    /// Buckets entries by the calendar day they <em>started</em> on (viewer timezone), matching
    /// the dashboard timesheet day drill-down and project timesheet calendar.
    /// </summary>
    private static IReadOnlyList<TimesheetDailyBreakdownRow> BuildDailyBreakdown(
        IReadOnlyList<TimesheetEntry> entries,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTimeOffset now,
        TimeZoneInfo calendarTimeZone)
    {
        var dayStarts = new Dictionary<DateOnly, (long DurationSeconds, HashSet<Guid> EntryIds)>();
        var fromDay = ToCalendarDay(fromUtc, calendarTimeZone);
        var toDay = ToCalendarDay(toUtc, calendarTimeZone);

        for (var day = fromDay; day <= toDay; day = day.AddDays(1))
        {
            dayStarts[day] = (0, []);
        }

        foreach (var entry in entries)
        {
            var startDay = ToCalendarDay(entry.StartedAtUtc, calendarTimeZone);
            if (startDay < fromDay || startDay > toDay)
            {
                continue;
            }

            var seconds = OverlapSeconds(entry.StartedAtUtc, entry.EndedAtUtc, fromUtc, toUtc, now);
            var current = dayStarts[startDay];
            current.EntryIds.Add(entry.Id);
            dayStarts[startDay] = (current.DurationSeconds + Math.Max(0, seconds), current.EntryIds);
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

    private TimeZoneInfo ResolveCalendarTimeZone(int? timeZoneOffsetMinutes)
    {
        if (timeZoneOffsetMinutes is null)
        {
            return _calendarTimeZone;
        }

        var offset = TimeSpan.FromMinutes(timeZoneOffsetMinutes.Value);
        if (offset < TimeSpan.FromHours(-14) || offset > TimeSpan.FromHours(14))
        {
            return _calendarTimeZone;
        }

        var id = $"ClientOffset/{(int)offset.TotalMinutes}";
        try
        {
            return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
        }
        catch (Exception)
        {
            return _calendarTimeZone;
        }
    }

    private static DateOnly ToCalendarDay(DateTimeOffset instant, TimeZoneInfo calendarTimeZone)
    {
        var local = TimeZoneInfo.ConvertTime(instant.ToUniversalTime(), calendarTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
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
