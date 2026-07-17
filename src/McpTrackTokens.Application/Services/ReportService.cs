using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Builds deterministic report DTOs separating agent duration, active project time,
/// imported usage, and subscription allocation.
/// </summary>
public sealed class ReportService : IReportService
{
    private readonly IProjectRepository _projects;
    private readonly ISessionRepository _sessions;
    private readonly IActivityEventRepository _events;
    private readonly IActivityWindowRepository _windows;
    private readonly IActivityWindowService _windowService;
    private readonly IExternalUsageRepository _usage;
    private readonly IUsageAttributionRepository _attributions;
    private readonly IImportBatchRepository _imports;
    private readonly ISubscriptionAllocationService _subscription;
    private readonly TrackingOptions _options;

    public ReportService(
        IProjectRepository projects,
        ISessionRepository sessions,
        IActivityEventRepository events,
        IActivityWindowRepository windows,
        IActivityWindowService windowService,
        IExternalUsageRepository usage,
        IUsageAttributionRepository attributions,
        IImportBatchRepository imports,
        ISubscriptionAllocationService subscription,
        IOptions<TrackingOptions> options)
    {
        _projects = projects;
        _sessions = sessions;
        _events = events;
        _windows = windows;
        _windowService = windowService;
        _usage = usage;
        _attributions = attributions;
        _imports = imports;
        _subscription = subscription;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<DailyActivityReport> GetDailyActivityAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var events = await _events.ListAsync(fromUtc, toUtc, projectId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var windows = await _windows.ListAsync(fromUtc, toUtc, projectId, cancellationToken)
            .ConfigureAwait(false);
        var merged = _windowService.MergeOverlappingSameProjectWindows(windows);

        var rows = events
            .GroupBy(e => DateOnly.FromDateTime(e.TimestampUtc.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dayStart = g.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var dayEnd = g.Key.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
                var dayWindows = merged.Where(w =>
                    w.StartedAtUtc < dayEnd && w.EndedAtUtc > dayStart);
                return new DailyActivityRow
                {
                    Day = g.Key,
                    ProjectId = projectId,
                    PromptCount = g.Count(e => e.EventType == ActivityEventType.PromptSubmitted),
                    AgentRuns = g.Count(e => e.EventType == ActivityEventType.AgentStarted),
                    AgentDurationMilliseconds = SumAgentDuration(g),
                    ActiveProjectTimeSeconds = dayWindows.Sum(w => OverlapSeconds(w, dayStart, dayEnd)),
                    SessionCount = g.Select(e => e.EditorSessionId).Where(id => id is not null).Distinct().Count()
                };
            })
            .ToList();

        return new DailyActivityReport
        {
            Day = DateOnly.FromDateTime(fromUtc.UtcDateTime),
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Rows = rows,
            Totals = await GetActivitySummaryAsync(projectId, fromUtc, toUtc, cancellationToken).ConfigureAwait(false)
        };
    }

    /// <inheritdoc />
    public async Task<ProjectActivityReport> GetProjectActivityAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var project = await RequireProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var events = await _events.ListAsync(fromUtc, toUtc, projectId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var windows = await _windows.ListAsync(fromUtc, toUtc, projectId, cancellationToken).ConfigureAwait(false);
        var merged = _windowService.MergeOverlappingSameProjectWindows(windows);
        var summary = BuildActivitySummary(events, merged, fromUtc, toUtc);

        var daily = await GetDailyActivityAsync(fromUtc, toUtc, projectId, cancellationToken).ConfigureAwait(false);

        return new ProjectActivityReport
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            ProjectSlug = project.Slug,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            PromptCount = summary.PromptCount,
            AgentRuns = summary.AgentRuns,
            AgentDurationMilliseconds = summary.AgentDurationMilliseconds,
            ActiveProjectTimeSeconds = summary.ActiveProjectTimeSeconds,
            SessionCount = summary.SessionCount,
            FailureCount = summary.FailureCount,
            CancellationCount = summary.CancellationCount,
            ByDay = daily.Rows,
            ByEditor = events
                .GroupBy(e => e.Editor.ToString())
                .Select(g => new NamedMetricRow
                {
                    Name = g.Key,
                    PromptCount = g.Count(e => e.EventType == ActivityEventType.PromptSubmitted),
                    AgentRuns = g.Count(e => e.EventType == ActivityEventType.AgentStarted),
                    AgentDurationMilliseconds = SumAgentDuration(g)
                })
                .OrderByDescending(r => r.PromptCount)
                .ToList(),
            ByBranch = events
                .Where(e => !string.IsNullOrWhiteSpace(e.Branch))
                .GroupBy(e => e.Branch!)
                .Select(g => new NamedMetricRow
                {
                    Name = g.Key,
                    PromptCount = g.Count(e => e.EventType == ActivityEventType.PromptSubmitted),
                    AgentRuns = g.Count(e => e.EventType == ActivityEventType.AgentStarted),
                    AgentDurationMilliseconds = SumAgentDuration(g)
                })
                .OrderByDescending(r => r.PromptCount)
                .ToList()
        };
    }

    /// <inheritdoc />
    public async Task<ProjectCostReport> GetProjectCostAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        bool includeSubscriptionAllocation = true,
        CancellationToken cancellationToken = default)
    {
        var project = await RequireProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var activity = await GetActivitySummaryAsync(projectId, fromUtc, toUtc, cancellationToken).ConfigureAwait(false);
        var attributions = await _attributions
            .ListAsync(fromUtc, toUtc, projectId, cancellationToken)
            .ConfigureAwait(false);

        var usageBased = attributions
            .Where(a => a.AttributionMethod != AttributionMethod.Unallocated)
            .Sum(a => a.AllocatedCost);
        var tokens = attributions.Sum(a => a.AllocatedTotalTokens);

        decimal subscription = 0m;
        if (includeSubscriptionAllocation)
        {
            var shares = await _subscription
                .AllocateAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var match = shares.FirstOrDefault(s => s.ProjectId == projectId);
            if (match is not null)
            {
                subscription = Math.Round(
                    (_options.CursorSubscriptionAmount * match.Percentage) / 100m,
                    2,
                    MidpointRounding.AwayFromZero);
            }
        }

        var currency = project.Currency;
        var otherProvider = attributions
            .Where(a => a.AttributionMethod != AttributionMethod.Unallocated)
            .Sum(a => 0m); // provider split is available via model report

        return new ProjectCostReport
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            ClientName = project.ClientName,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = currency,
            ActiveProjectTimeSeconds = activity.ActiveProjectTimeSeconds,
            AgentDurationMilliseconds = activity.AgentDurationMilliseconds,
            PromptCount = activity.PromptCount,
            ImportedTotalTokens = tokens,
            UsageBasedCursorCost = usageBased,
            SubscriptionAllocation = subscription,
            OtherProviderCost = otherProvider,
            UnallocatedCost = 0m,
            TotalAiCost = usageBased + subscription + otherProvider,
            ByModel = []
        };
    }

    /// <inheritdoc />
    public async Task<ClientCostReport> GetClientCostAsync(
        string clientName,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        var projects = await _projects.ListByClientAsync(clientName, cancellationToken).ConfigureAwait(false);
        var projectReports = new List<ProjectCostReport>();
        foreach (var project in projects)
        {
            projectReports.Add(
                await GetProjectCostAsync(project.Id, fromUtc, toUtc, true, cancellationToken).ConfigureAwait(false));
        }

        return new ClientCostReport
        {
            ClientName = clientName,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = projectReports.FirstOrDefault()?.Currency ?? _options.DefaultCurrency,
            ProjectCount = projectReports.Count,
            ActiveProjectTimeSeconds = projectReports.Sum(p => p.ActiveProjectTimeSeconds),
            AgentDurationMilliseconds = projectReports.Sum(p => p.AgentDurationMilliseconds),
            PromptCount = projectReports.Sum(p => p.PromptCount),
            UsageBasedCost = projectReports.Sum(p => p.UsageBasedCursorCost),
            SubscriptionAllocation = projectReports.Sum(p => p.SubscriptionAllocation),
            OtherProviderCost = projectReports.Sum(p => p.OtherProviderCost),
            TotalAiCost = projectReports.Sum(p => p.TotalAiCost),
            Projects = projectReports
        };
    }

    /// <inheritdoc />
    public async Task<UsageAttributionReport> GetUsageAttributionAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var attributions = await _attributions
            .ListAsync(fromUtc, toUtc, projectId, cancellationToken)
            .ConfigureAwait(false);
        var projects = (await _projects.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => p.Id);

        var rows = new List<UsageAttributionRow>();
        foreach (var attribution in attributions)
        {
            projects.TryGetValue(attribution.ProjectId ?? Guid.Empty, out var project);
            var usage = await _usage.GetByIdAsync(attribution.ExternalUsageRecordId, cancellationToken)
                .ConfigureAwait(false);
            rows.Add(new UsageAttributionRow
            {
                UsageRecordId = attribution.ExternalUsageRecordId,
                AttributionId = attribution.Id,
                ProjectId = attribution.ProjectId,
                ProjectName = project?.Name,
                TimestampUtc = usage?.TimestampUtc ?? attribution.CreatedAtUtc,
                Model = usage?.Model,
                Provider = usage?.Provider?.ToString(),
                AllocatedCost = attribution.AllocatedCost,
                AllocationPercentage = attribution.AllocationPercentage,
                AllocatedTotalTokens = attribution.AllocatedTotalTokens,
                AttributionMethod = attribution.AttributionMethod.ToString(),
                Confidence = attribution.Confidence.ToString(),
                Reason = attribution.Reason
            });
        }

        var unallocated = await _usage.ListUnallocatedAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new UsageAttributionReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Rows = rows,
            TotalAllocatedCost = rows.Sum(r => r.AllocatedCost),
            TotalUnallocatedCost = unallocated.Sum(u => u.ReportedCost ?? 0m),
            Currency = _options.DefaultCurrency
        };
    }

    /// <inheritdoc />
    public async Task<UnallocatedUsageReport> GetUnallocatedUsageAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var records = await _usage.ListUnallocatedAsync(fromUtc, toUtc, limit, cancellationToken)
            .ConfigureAwait(false);
        var items = records.Select(r => new UnallocatedItemDto
        {
            Id = r.Id,
            Kind = "usage",
            TimestampUtc = r.TimestampUtc,
            Model = r.Model,
            Provider = r.Provider?.ToString(),
            ReportedCost = r.ReportedCost,
            Currency = r.Currency ?? _options.DefaultCurrency,
            Reason = "No attribution row with a project."
        }).ToList();

        return new UnallocatedUsageReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Count = items.Count,
            TotalCost = items.Sum(i => i.ReportedCost ?? 0m),
            Currency = _options.DefaultCurrency,
            Items = items
        };
    }

    /// <inheritdoc />
    public async Task<MonthlySummaryReport> GetMonthlySummaryAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var toUtc = fromUtc.AddMonths(1);
        var projects = await _projects.ListAsync(activeOnly: true, cancellationToken).ConfigureAwait(false);
        var projectCosts = new List<ProjectCostReport>();
        foreach (var project in projects)
        {
            projectCosts.Add(
                await GetProjectCostAsync(project.Id, fromUtc, toUtc, true, cancellationToken).ConfigureAwait(false));
        }

        var activity = await GetActivitySummaryAsync(null, fromUtc, toUtc, cancellationToken).ConfigureAwait(false);
        var usageRecords = await _usage.ListAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new MonthlySummaryReport
        {
            Year = year,
            Month = month,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = _options.DefaultCurrency,
            Activity = activity,
            Usage = new UsageSummaryDto
            {
                InputTokens = usageRecords.Sum(u => u.InputTokens ?? 0),
                OutputTokens = usageRecords.Sum(u => u.OutputTokens ?? 0),
                CachedInputTokens = usageRecords.Sum(u => u.CachedInputTokens ?? 0),
                ReasoningTokens = usageRecords.Sum(u => u.ReasoningTokens ?? 0),
                TotalTokens = usageRecords.Sum(u => u.TotalTokens ?? 0),
                RequestCount = usageRecords.Sum(u => u.RequestCount ?? 0),
                ReportedCost = usageRecords.Sum(u => u.ReportedCost ?? 0),
                Currency = _options.DefaultCurrency,
                FromUtc = fromUtc,
                ToUtc = toUtc
            },
            Cost = new CostSummaryDto
            {
                UsageBasedCost = projectCosts.Sum(p => p.UsageBasedCursorCost),
                SubscriptionAllocation = projectCosts.Sum(p => p.SubscriptionAllocation),
                OtherProviderCost = projectCosts.Sum(p => p.OtherProviderCost),
                UnallocatedCost = (await GetUnallocatedUsageAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)).TotalCost,
                TotalAiCost = projectCosts.Sum(p => p.TotalAiCost),
                Currency = _options.DefaultCurrency,
                FromUtc = fromUtc,
                ToUtc = toUtc
            },
            Projects = projectCosts
        };
    }

    /// <inheritdoc />
    public async Task<EditorComparisonReport> GetEditorComparisonAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var events = await _events.ListAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var editors = events
            .GroupBy(e => e.Editor.ToString())
            .Select(g => new NamedMetricRow
            {
                Name = g.Key,
                PromptCount = g.Count(e => e.EventType == ActivityEventType.PromptSubmitted),
                AgentRuns = g.Count(e => e.EventType == ActivityEventType.AgentStarted),
                AgentDurationMilliseconds = SumAgentDuration(g)
            })
            .OrderByDescending(r => r.PromptCount)
            .ToList();

        return new EditorComparisonReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Editors = editors
        };
    }

    /// <inheritdoc />
    public async Task<ModelCostReport> GetModelCostAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var usage = await _usage.ListAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var attributions = await _attributions.ListAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var attributedIds = attributions
            .Where(a => a.ProjectId is not null)
            .Select(a => a.ExternalUsageRecordId)
            .ToHashSet();

        var models = usage
            .GroupBy(u => u.Model ?? "unknown")
            .Select(g =>
            {
                var allocated = attributions
                    .Where(a => g.Any(u => u.Id == a.ExternalUsageRecordId))
                    .Sum(a => a.AllocatedCost);
                var unallocated = g.Where(u => !attributedIds.Contains(u.Id)).Sum(u => u.ReportedCost ?? 0m);
                return new ModelCostRow
                {
                    Model = g.Key,
                    Provider = g.Select(x => x.Provider?.ToString()).FirstOrDefault(p => p is not null),
                    TotalTokens = g.Sum(u => u.TotalTokens ?? 0),
                    RequestCount = g.Sum(u => u.RequestCount ?? 0),
                    UsageBasedCost = g.Sum(u => u.ReportedCost ?? 0m),
                    AllocatedCost = allocated,
                    UnallocatedCost = unallocated
                };
            })
            .OrderByDescending(m => m.UsageBasedCost)
            .ToList();

        return new ModelCostReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = _options.DefaultCurrency,
            Models = models
        };
    }

    /// <inheritdoc />
    public async Task<TrackingStatusDto> GetTrackingStatusAsync(CancellationToken cancellationToken = default)
    {
        var activeSessions = await _sessions.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var active = activeSessions.OrderByDescending(s => s.LastActivityAtUtc).FirstOrDefault();
        ProjectDto? currentProject = null;
        if (active?.ProjectId is Guid projectId)
        {
            var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (project is not null)
            {
                currentProject = MapProject(project);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var lastEvents = await _events.ListAsync(todayStart.AddDays(-7), now, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var last = lastEvents.OrderByDescending(e => e.TimestampUtc).FirstOrDefault();
        var latestImport = await _imports.GetLatestAsync(UsageSource.CursorCsv, cancellationToken).ConfigureAwait(false)
            ?? await _imports.GetLatestAsync(UsageSource.CursorJson, cancellationToken).ConfigureAwait(false);

        return new TrackingStatusDto
        {
            IsHealthy = true,
            DatabasePath = _options.GetResolvedDatabasePath(),
            DatabaseProvider = _options.DatabaseProvider,
            CurrentProject = currentProject,
            ActiveSessionId = active?.Id,
            ActiveSessionEditor = active?.Editor.ToString(),
            LastEventAtUtc = last?.TimestampUtc,
            LastEventType = last?.EventType.ToString(),
            QueuedEventCount = 0,
            UnallocatedEventCount = await _events.CountUnallocatedAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            UnallocatedUsageCount = (await _usage
                .ListUnallocatedAsync(todayStart.AddDays(-30), now, cancellationToken: cancellationToken)
                .ConfigureAwait(false)).Count,
            LastCursorImportAtUtc = latestImport?.CompletedAtUtc ?? latestImport?.StartedAtUtc,
            LastCursorImportStatus = latestImport?.Status.ToString()
        };
    }

    /// <inheritdoc />
    public async Task<ActivitySummaryDto> GetActivitySummaryAsync(
        Guid? projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var events = await _events.ListAsync(fromUtc, toUtc, projectId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var windows = await _windows.ListAsync(fromUtc, toUtc, projectId, cancellationToken).ConfigureAwait(false);
        var merged = _windowService.MergeOverlappingSameProjectWindows(windows);
        return BuildActivitySummary(events, merged, fromUtc, toUtc);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnallocatedItemDto>> GetUnallocatedActivityAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var events = await _events
            .ListAsync(fromUtc, toUtc, unallocatedOnly: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        IEnumerable<PromptActivityEvent> query = events.OrderByDescending(e => e.TimestampUtc);
        if (limit is int take)
        {
            query = query.Take(take);
        }

        return query.Select(e => new UnallocatedItemDto
        {
            Id = e.Id,
            Kind = "activity",
            TimestampUtc = e.TimestampUtc,
            Editor = e.Editor.ToString(),
            Model = e.Model,
            Provider = e.Provider?.ToString(),
            RepositoryPath = e.RepositoryPath,
            RemoteUrl = e.RemoteUrl,
            ExternalRequestId = e.ExternalRequestId,
            Reason = "Activity event has no project attribution."
        }).ToList();
    }

    private async Task<Project> RequireProjectAsync(Guid projectId, CancellationToken cancellationToken)
        => await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(nameof(Project), projectId);

    private static ActivitySummaryDto BuildActivitySummary(
        IReadOnlyList<PromptActivityEvent> events,
        IReadOnlyList<ActivityWindow> windows,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
        => new()
        {
            PromptCount = events.Count(e => e.EventType == ActivityEventType.PromptSubmitted),
            AgentRuns = events.Count(e => e.EventType == ActivityEventType.AgentStarted),
            AgentDurationMilliseconds = SumAgentDuration(events),
            ActiveProjectTimeSeconds = windows.Sum(w => w.DurationSeconds),
            SessionCount = events.Select(e => e.EditorSessionId).Where(id => id is not null).Distinct().Count(),
            FailureCount = events.Count(e => e.EventType == ActivityEventType.AgentFailed),
            CancellationCount = events.Count(e => e.EventType == ActivityEventType.AgentCancelled),
            FromUtc = fromUtc,
            ToUtc = toUtc
        };

    private static long SumAgentDuration(IEnumerable<PromptActivityEvent> events)
        => events
            .Where(e => e.EventType is ActivityEventType.AgentCompleted
                or ActivityEventType.AgentFailed
                or ActivityEventType.AgentCancelled)
            .Sum(e => e.DurationMilliseconds ?? 0);

    private static long OverlapSeconds(ActivityWindow window, DateTime dayStart, DateTime dayEnd)
    {
        var start = window.StartedAtUtc > dayStart ? window.StartedAtUtc : new DateTimeOffset(dayStart);
        var end = window.EndedAtUtc < dayEnd ? window.EndedAtUtc : new DateTimeOffset(dayEnd);
        if (end <= start)
        {
            return 0;
        }

        return (long)Math.Round((end - start).TotalSeconds, MidpointRounding.AwayFromZero);
    }

    private static ProjectDto MapProject(Project project) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Slug = project.Slug,
        ClientName = project.ClientName,
        BillingCode = project.BillingCode,
        Currency = project.Currency,
        PrimaryRepositoryPath = project.PrimaryRepositoryPath,
        PrimaryRemoteUrl = project.PrimaryRemoteUrl,
        IsActive = project.IsActive,
        CreatedAtUtc = project.CreatedAtUtc,
        UpdatedAtUtc = project.UpdatedAtUtc
    };
}
