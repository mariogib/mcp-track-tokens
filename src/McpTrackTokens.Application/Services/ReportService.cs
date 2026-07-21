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
        var sessions = await ListProjectSessionsAsync(projectId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        var tokensByDay = await GetAttributedTokensByDayAsync(fromUtc, toUtc, projectId, cancellationToken)
            .ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var dayKeys = events
            .Select(e => DateOnly.FromDateTime(e.TimestampUtc.UtcDateTime))
            .Concat(tokensByDay.Keys)
            .Concat(sessions.SelectMany(SessionDayKeys))
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        var rows = dayKeys
            .Select(day =>
            {
                var dayEvents = events.Where(e => DateOnly.FromDateTime(e.TimestampUtc.UtcDateTime) == day);
                var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                var dayEnd = new DateTimeOffset(day.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
                return new DailyActivityRow
                {
                    Day = day,
                    ProjectId = projectId,
                    PromptCount = dayEvents.Count(e => e.EventType == ActivityEventType.PromptSubmitted),
                    AgentRuns = dayEvents.Count(e => e.EventType == ActivityEventType.AgentStarted),
                    AgentDurationMilliseconds = SumAgentDuration(dayEvents),
                    ActiveProjectTimeSeconds = sessions.Sum(s =>
                        IntervalOverlap.Seconds(s.StartedAtUtc, s.EndedAtUtc, dayStart, dayEnd, now)),
                    SessionCount = dayEvents.Select(e => e.EditorSessionId).Where(id => id is not null).Distinct().Count(),
                    TotalTokens = tokensByDay.GetValueOrDefault(day)
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
        var sessions = await ListProjectSessionsAsync(projectId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        var summary = BuildActivitySummary(events, sessions, fromUtc, toUtc);

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

        var allocated = attributions
            .Where(a => a.AttributionMethod != AttributionMethod.Unallocated)
            .ToList();
        var tokens = attributions.Sum(a => a.AllocatedTotalTokens);

        var usageById = await LoadUsageByIdsAsync(
                allocated.Select(a => a.ExternalUsageRecordId),
                cancellationToken)
            .ConfigureAwait(false);

        decimal usageBasedCursor = 0m;
        decimal otherProvider = 0m;
        foreach (var attribution in allocated)
        {
            usageById.TryGetValue(attribution.ExternalUsageRecordId, out var usage);
            if (IsCursorProvider(usage?.Provider))
            {
                usageBasedCursor += attribution.AllocatedCost;
            }
            else
            {
                otherProvider += attribution.AllocatedCost;
            }
        }

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

        var unallocatedCost = await GetProjectUnallocatedCostAsync(projectId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        var byModel = BuildProjectModelCostRows(allocated, usageById, subscription);
        var tokenCost = await GetProjectTokenCostEstimateAsync(projectId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        var tokenCostByModel = tokenCost.ByModel.ToDictionary(
            r => r.Model,
            r => r.EstimatedCost,
            StringComparer.OrdinalIgnoreCase);
        byModel = byModel
            .Select(row => row with
            {
                CalculatedTokenCost = tokenCostByModel.TryGetValue(row.Name, out var estimated)
                    ? estimated
                    : 0m
            })
            .ToList();

        return new ProjectCostReport
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            ClientName = project.ClientName,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = project.Currency,
            ActiveProjectTimeSeconds = activity.ActiveProjectTimeSeconds,
            AgentDurationMilliseconds = activity.AgentDurationMilliseconds,
            PromptCount = activity.PromptCount,
            ImportedTotalTokens = tokens,
            UsageBasedCursorCost = usageBasedCursor,
            SubscriptionAllocation = subscription,
            OtherProviderCost = otherProvider,
            UnallocatedCost = unallocatedCost,
            TotalAiCost = usageBasedCursor + subscription + otherProvider,
            CalculatedTokenCost = tokenCost.EstimatedCost,
            HasRateCard = tokenCost.HasRateCard,
            ByModel = byModel
        };
    }

    /// <inheritdoc />
    public async Task<ProjectTokenCostEstimate> GetProjectTokenCostEstimateAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var project = await RequireProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var attributions = await _attributions
            .ListAsync(fromUtc, toUtc, projectId, cancellationToken)
            .ConfigureAwait(false);

        var allocated = attributions
            .Where(a => a.AttributionMethod != AttributionMethod.Unallocated)
            .ToList();

        var rates = _options.CursorTokenRates.Count > 0
            ? _options.CursorTokenRates
            : CursorTokenCostCalculator.CreateDefaultRates();

        var usageById = await LoadUsageByIdsAsync(
                allocated.Select(a => a.ExternalUsageRecordId),
                cancellationToken)
            .ConfigureAwait(false);

        var aggregates = new Dictionary<string, TokenCostAggregate>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribution in allocated)
        {
            if (!usageById.TryGetValue(attribution.ExternalUsageRecordId, out var usage))
            {
                continue;
            }

            var modelName = string.IsNullOrWhiteSpace(usage.Model) ? "unknown" : usage.Model.Trim();
            var rate = CursorTokenCostCalculator.ResolveRate(rates, modelName);
            if (rate is null)
            {
                continue;
            }

            if (!aggregates.TryGetValue(modelName, out var agg))
            {
                agg = new TokenCostAggregate(modelName, rate);
                aggregates[modelName] = agg;
            }

            var input = ScaleByAllocation(usage.InputTokens ?? 0, attribution.AllocationPercentage);
            var output = ScaleByAllocation(usage.OutputTokens ?? 0, attribution.AllocationPercentage);
            var cached = ScaleByAllocation(usage.CachedInputTokens ?? 0, attribution.AllocationPercentage);
            var reasoning = ScaleByAllocation(usage.ReasoningTokens ?? 0, attribution.AllocationPercentage);
            var total = ScaleByAllocation(usage.TotalTokens ?? 0, attribution.AllocationPercentage);
            var accounted = input + output + cached + reasoning;
            if (total > accounted)
            {
                input += total - accounted;
            }

            agg.InputTokens += input;
            agg.OutputTokens += output;
            agg.CachedInputTokens += cached;
            agg.ReasoningTokens += reasoning;
            agg.TotalTokens += total > 0 ? total : accounted;
            agg.ReportedCost += attribution.AllocatedCost;
            agg.EstimatedCost += CursorTokenCostCalculator.Estimate(
                usage,
                attribution.AllocationPercentage,
                rate);
        }

        var byModel = aggregates.Values
            .Select(a => new TokenCostModelRow
            {
                Model = a.Model,
                RateSource = a.Rate.Model,
                InputTokens = a.InputTokens,
                OutputTokens = a.OutputTokens,
                CachedInputTokens = a.CachedInputTokens,
                ReasoningTokens = a.ReasoningTokens,
                TotalTokens = a.TotalTokens,
                EstimatedCost = Math.Round(a.EstimatedCost, 4, MidpointRounding.AwayFromZero),
                ReportedCost = Math.Round(a.ReportedCost, 4, MidpointRounding.AwayFromZero),
                InputPerMillion = a.Rate.InputPerMillion,
                OutputPerMillion = a.Rate.OutputPerMillion,
                CacheReadPerMillion = a.Rate.CacheReadPerMillion,
                ReasoningPerMillion = a.Rate.ReasoningPerMillion
            })
            .OrderByDescending(r => r.EstimatedCost)
            .ThenBy(r => r.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProjectTokenCostEstimate
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = project.Currency,
            InputTokens = byModel.Sum(r => r.InputTokens),
            OutputTokens = byModel.Sum(r => r.OutputTokens),
            CachedInputTokens = byModel.Sum(r => r.CachedInputTokens),
            ReasoningTokens = byModel.Sum(r => r.ReasoningTokens),
            TotalTokens = byModel.Sum(r => r.TotalTokens),
            EstimatedCost = Math.Round(byModel.Sum(r => r.EstimatedCost), 4, MidpointRounding.AwayFromZero),
            ReportedCost = Math.Round(byModel.Sum(r => r.ReportedCost), 4, MidpointRounding.AwayFromZero),
            RateCardModelCount = rates.Count,
            HasRateCard = rates.Count > 0,
            ByModel = byModel
        };
    }

    /// <inheritdoc />
    public async Task<UsageSummaryDto> GetProjectUsageSummaryAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var project = await RequireProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var attributions = await _attributions
            .ListAsync(fromUtc, toUtc, projectId, cancellationToken)
            .ConfigureAwait(false);

        var allocated = attributions
            .Where(a => a.AttributionMethod != AttributionMethod.Unallocated && a.ProjectId == projectId)
            .ToList();

        long cachedInputTokens = 0;
        long reasoningTokens = 0;
        foreach (var attribution in allocated)
        {
            var usage = await _usage.GetByIdAsync(attribution.ExternalUsageRecordId, cancellationToken)
                .ConfigureAwait(false);
            if (usage is null)
            {
                continue;
            }

            cachedInputTokens += ScaleByAllocation(usage.CachedInputTokens ?? 0, attribution.AllocationPercentage);
            reasoningTokens += ScaleByAllocation(usage.ReasoningTokens ?? 0, attribution.AllocationPercentage);
        }

        return new UsageSummaryDto
        {
            InputTokens = allocated.Sum(a => a.AllocatedInputTokens),
            OutputTokens = allocated.Sum(a => a.AllocatedOutputTokens),
            CachedInputTokens = cachedInputTokens,
            ReasoningTokens = reasoningTokens,
            TotalTokens = allocated.Sum(a => a.AllocatedTotalTokens),
            RequestCount = allocated.Select(a => a.ExternalUsageRecordId).Distinct().Count(),
            ReportedCost = allocated.Sum(a => a.AllocatedCost),
            Currency = project.Currency,
            FromUtc = fromUtc,
            ToUtc = toUtc
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
        var projects = (await _projects.ListByClientAsync(clientName, cancellationToken).ConfigureAwait(false))
            .Where(p => p.IsActive)
            .ToList();
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
            CalculatedTokenCost = Math.Round(
                projectReports.Sum(p => p.CalculatedTokenCost),
                4,
                MidpointRounding.AwayFromZero),
            HasRateCard = projectReports.Any(p => p.HasRateCard),
            Projects = projectReports
        };
    }

    /// <inheritdoc />
    public async Task<ClientTokenCostEstimate> GetClientTokenCostEstimateAsync(
        string clientName,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        var projects = (await _projects.ListByClientAsync(clientName, cancellationToken).ConfigureAwait(false))
            .Where(p => p.IsActive)
            .ToList();
        var projectReports = new List<ProjectTokenCostEstimate>();
        foreach (var project in projects)
        {
            projectReports.Add(
                await GetProjectTokenCostEstimateAsync(project.Id, fromUtc, toUtc, cancellationToken)
                    .ConfigureAwait(false));
        }

        var byModel = MergeTokenCostModelRows(projectReports.SelectMany(p => p.ByModel));
        var rates = _options.CursorTokenRates.Count > 0
            ? _options.CursorTokenRates
            : CursorTokenCostCalculator.CreateDefaultRates();

        return new ClientTokenCostEstimate
        {
            ClientName = clientName.Trim(),
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = projectReports.FirstOrDefault()?.Currency ?? _options.DefaultCurrency,
            ProjectCount = projectReports.Count,
            InputTokens = projectReports.Sum(p => p.InputTokens),
            OutputTokens = projectReports.Sum(p => p.OutputTokens),
            CachedInputTokens = projectReports.Sum(p => p.CachedInputTokens),
            ReasoningTokens = projectReports.Sum(p => p.ReasoningTokens),
            TotalTokens = projectReports.Sum(p => p.TotalTokens),
            EstimatedCost = Math.Round(projectReports.Sum(p => p.EstimatedCost), 4, MidpointRounding.AwayFromZero),
            ReportedCost = Math.Round(projectReports.Sum(p => p.ReportedCost), 4, MidpointRounding.AwayFromZero),
            RateCardModelCount = rates.Count,
            HasRateCard = rates.Count > 0,
            ByModel = byModel,
            Projects = projectReports
                .OrderByDescending(p => p.EstimatedCost)
                .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToList()
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

        var rates = _options.CursorTokenRates.Count > 0
            ? _options.CursorTokenRates
            : CursorTokenCostCalculator.CreateDefaultRates();
        var rows = new List<UsageAttributionRow>();
        decimal calculatedTokenCost = 0m;
        foreach (var attribution in attributions)
        {
            projects.TryGetValue(attribution.ProjectId ?? Guid.Empty, out var project);
            var usage = await _usage.GetByIdAsync(attribution.ExternalUsageRecordId, cancellationToken)
                .ConfigureAwait(false);
            decimal rowCalculated = 0m;
            if (usage is not null &&
                CursorTokenCostCalculator.ResolveRate(rates, usage.Model) is { } rate)
            {
                rowCalculated = CursorTokenCostCalculator.Estimate(
                    usage,
                    attribution.AllocationPercentage > 0m
                        ? attribution.AllocationPercentage
                        : 100m,
                    rate);
                if (attribution.AttributionMethod != AttributionMethod.Unallocated)
                {
                    calculatedTokenCost += rowCalculated;
                }
            }

            rows.Add(new UsageAttributionRow
            {
                UsageRecordId = attribution.ExternalUsageRecordId,
                AttributionId = attribution.Id,
                ProjectId = attribution.ProjectId,
                ProjectName = project?.Name,
                ActivityEventId = attribution.ActivityEventId,
                TimestampUtc = usage?.TimestampUtc ?? attribution.CreatedAtUtc,
                Model = usage?.Model,
                Provider = usage?.Provider?.ToString(),
                AllocatedCost = attribution.AllocatedCost,
                CalculatedTokenCost = rowCalculated,
                AllocationPercentage = attribution.AllocationPercentage,
                AllocatedTotalTokens = attribution.AllocatedTotalTokens,
                AttributionMethod = attribution.AttributionMethod.ToString(),
                Confidence = attribution.Confidence.ToString(),
                Reason = attribution.Reason
            });
        }

        rows.Sort((a, b) => b.TimestampUtc.CompareTo(a.TimestampUtc));

        var unallocated = await _usage.ListUnallocatedAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new UsageAttributionReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Rows = rows,
            TotalAllocatedCost = rows.Sum(r => r.AllocatedCost),
            TotalUnallocatedCost = unallocated.Sum(u => u.ReportedCost ?? 0m),
            TotalCalculatedTokenCost = Math.Round(calculatedTokenCost, 4, MidpointRounding.AwayFromZero),
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
        var rates = _options.CursorTokenRates.Count > 0
            ? _options.CursorTokenRates
            : CursorTokenCostCalculator.CreateDefaultRates();
        var items = records.Select(r =>
        {
            var rate = CursorTokenCostCalculator.ResolveRate(rates, r.Model);
            var calculated = rate is null
                ? 0m
                : CursorTokenCostCalculator.Estimate(r, 100m, rate);
            return new UnallocatedItemDto
            {
                Id = r.Id,
                Kind = "usage",
                TimestampUtc = r.TimestampUtc,
                Model = r.Model,
                Provider = r.Provider?.ToString(),
                TotalTokens = AttributionEngine.ResolveTotalTokens(r),
                ReportedCost = r.ReportedCost ?? 0m,
                CalculatedTokenCost = calculated,
                Currency = r.Currency ?? _options.DefaultCurrency,
                Reason = "No attribution row with a project."
            };
        }).ToList();

        return new UnallocatedUsageReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Count = items.Count,
            TotalCalculatedTokenCost = Math.Round(
                items.Sum(i => i.CalculatedTokenCost),
                4,
                MidpointRounding.AwayFromZero),
            TotalCost = items.Sum(i => i.ReportedCost ?? 0m),
            Currency = _options.DefaultCurrency,
            Items = items
        };
    }

    /// <inheritdoc />
    public async Task<ImportedUsageReport> GetImportedUsageAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var records = await _usage
            .ListAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var attributions = await _attributions
            .ListByUsageRecordIdsAsync(records.Select(r => r.Id).ToList(), cancellationToken)
            .ConfigureAwait(false);
        var attributionByUsage = attributions
            .Where(a => a.AttributionMethod != AttributionMethod.Unallocated && a.ProjectId is not null)
            .GroupBy(a => a.ExternalUsageRecordId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AllocationPercentage).First());

        var projects = (await _projects.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => p.Id);

        IEnumerable<ExternalUsageRecord> ordered = records.OrderByDescending(r => r.TimestampUtc);
        if (limit is > 0)
        {
            ordered = ordered.Take(limit.Value);
        }

        var rates = _options.CursorTokenRates.Count > 0
            ? _options.CursorTokenRates
            : CursorTokenCostCalculator.CreateDefaultRates();

        var items = ordered.Select(r =>
        {
            attributionByUsage.TryGetValue(r.Id, out var attribution);
            projects.TryGetValue(attribution?.ProjectId ?? Guid.Empty, out var project);
            var rate = CursorTokenCostCalculator.ResolveRate(rates, r.Model);
            var calculated = rate is null
                ? 0m
                : CursorTokenCostCalculator.Estimate(r, 100m, rate);
            return new ImportedUsageItemDto
            {
                Id = r.Id,
                TimestampUtc = r.TimestampUtc,
                Source = r.Source.ToString(),
                ExternalRecordId = r.ExternalRecordId,
                Model = r.Model,
                Provider = r.Provider?.ToString(),
                InputTokens = r.InputTokens,
                OutputTokens = r.OutputTokens,
                CachedInputTokens = r.CachedInputTokens,
                TotalTokens = AttributionEngine.ResolveTotalTokens(r),
                ReportedCost = r.ReportedCost ?? 0m,
                CalculatedTokenCost = calculated,
                Currency = r.Currency ?? _options.DefaultCurrency,
                RequestCount = r.RequestCount,
                ImportBatchId = r.ImportBatchId,
                ImportedAtUtc = r.ImportedAtUtc,
                ProjectId = attribution?.ProjectId,
                ProjectName = project?.Name,
                ActivityEventId = attribution?.ActivityEventId,
                AttributionMethod = attribution?.AttributionMethod.ToString()
            };
        }).ToList();

        return new ImportedUsageReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Count = items.Count,
            TotalTokens = items.Sum(i => i.TotalTokens),
            TotalCost = items.Sum(i => i.ReportedCost),
            TotalCalculatedTokenCost = Math.Round(
                items.Sum(i => i.CalculatedTokenCost),
                4,
                MidpointRounding.AwayFromZero),
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
                CalculatedTokenCost = Math.Round(
                    projectCosts.Sum(p => p.CalculatedTokenCost),
                    4,
                    MidpointRounding.AwayFromZero),
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
        var rates = _options.CursorTokenRates.Count > 0
            ? _options.CursorTokenRates
            : CursorTokenCostCalculator.CreateDefaultRates();

        var models = usage
            .GroupBy(u => u.Model ?? "unknown")
            .Select(g =>
            {
                var allocated = attributions
                    .Where(a => g.Any(u => u.Id == a.ExternalUsageRecordId))
                    .Sum(a => a.AllocatedCost);
                var unallocated = g.Where(u => !attributedIds.Contains(u.Id)).Sum(u => u.ReportedCost ?? 0m);
                var calculated = g.Sum(u =>
                {
                    var rate = CursorTokenCostCalculator.ResolveRate(rates, u.Model);
                    return rate is null ? 0m : CursorTokenCostCalculator.Estimate(u, 100m, rate);
                });
                return new ModelCostRow
                {
                    Model = g.Key,
                    Provider = g.Select(x => x.Provider?.ToString()).FirstOrDefault(p => p is not null),
                    TotalTokens = g.Sum(u => u.TotalTokens ?? 0),
                    RequestCount = g.Sum(u => u.RequestCount ?? 0),
                    UsageBasedCost = g.Sum(u => u.ReportedCost ?? 0m),
                    AllocatedCost = allocated,
                    UnallocatedCost = unallocated,
                    CalculatedTokenCost = Math.Round(calculated, 4, MidpointRounding.AwayFromZero)
                };
            })
            .OrderByDescending(m => m.CalculatedTokenCost)
            .ThenByDescending(m => m.UsageBasedCost)
            .ToList();

        return new ModelCostReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = _options.DefaultCurrency,
            CalculatedTokenCost = Math.Round(models.Sum(m => m.CalculatedTokenCost), 4, MidpointRounding.AwayFromZero),
            HasRateCard = rates.Count > 0,
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
        var sessions = await ListProjectSessionsAsync(projectId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        return BuildActivitySummary(events, sessions, fromUtc, toUtc);
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
            RepositoryPath = e.RepositoryPath ?? e.WorkspacePath,
            RemoteUrl = e.RemoteUrl,
            ExternalRequestId = e.ExternalRequestId,
            WorkspacePath = e.WorkspacePath,
            EventType = e.EventType.ToString(),
            DurationMilliseconds = e.DurationMilliseconds,
            Reason = "Activity event has no project attribution."
        }).ToList();
    }

    private async Task<Dictionary<DateOnly, long>> GetAttributedTokensByDayAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<DateOnly, long>();
        var usageRecords = await _usage
            .ListAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (usageRecords.Count == 0)
        {
            return result;
        }

        var attributions = await _attributions
            .ListByUsageRecordIdsAsync(usageRecords.Select(u => u.Id).ToList(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in usageRecords)
        {
            var dayAttrs = attributions
                .Where(a =>
                    a.ExternalUsageRecordId == record.Id &&
                    a.AttributionMethod != AttributionMethod.Unallocated &&
                    a.ProjectId is not null &&
                    (projectId is null || a.ProjectId == projectId))
                .ToList();
            if (dayAttrs.Count == 0)
            {
                continue;
            }

            var tokens = dayAttrs.Sum(a => a.AllocatedTotalTokens);
            if (tokens <= 0)
            {
                tokens = AttributionEngine.ResolveTotalTokens(record);
            }

            if (tokens <= 0)
            {
                continue;
            }

            var day = DateOnly.FromDateTime(record.TimestampUtc.UtcDateTime);
            result[day] = result.GetValueOrDefault(day) + tokens;
        }

        return result;
    }

    private async Task<Dictionary<Guid, ExternalUsageRecord>> LoadUsageByIdsAsync(
        IEnumerable<Guid> usageIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ExternalUsageRecord>();
        foreach (var id in usageIds.Distinct())
        {
            var usage = await _usage.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (usage is not null)
            {
                result[id] = usage;
            }
        }

        return result;
    }

    private async Task<decimal> GetProjectUnallocatedCostAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var windows = await _windows.ListAsync(fromUtc, toUtc, projectId, cancellationToken)
            .ConfigureAwait(false);
        var merged = _windowService.MergeOverlappingSameProjectWindows(windows);
        if (merged.Count == 0)
        {
            return 0m;
        }

        var unallocated = await _usage
            .ListUnallocatedAsync(fromUtc, toUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return unallocated
            .Where(u => merged.Any(w =>
                u.TimestampUtc >= w.StartedAtUtc && u.TimestampUtc <= w.EndedAtUtc))
            .Sum(u => u.ReportedCost ?? 0m);
    }

    private static IReadOnlyList<NamedMetricRow> BuildProjectModelCostRows(
        IReadOnlyList<UsageAttribution> allocated,
        IReadOnlyDictionary<Guid, ExternalUsageRecord> usageById,
        decimal subscriptionAllocation)
    {
        if (allocated.Count == 0)
        {
            return [];
        }

        var groups = allocated
            .GroupBy(a =>
            {
                usageById.TryGetValue(a.ExternalUsageRecordId, out var usage);
                return string.IsNullOrWhiteSpace(usage?.Model) ? "unknown" : usage!.Model!;
            })
            .ToList();

        var weights = groups
            .Select(g =>
            {
                var cost = g.Sum(a => a.AllocatedCost);
                var tokens = g.Sum(a => a.AllocatedTotalTokens);
                // Prefer cost weights; fall back to tokens when Included/Free rows are $0.
                var weight = cost > 0m ? cost : tokens;
                return (Group: g, Cost: cost, Tokens: tokens, Weight: weight);
            })
            .ToList();

        var totalWeight = weights.Sum(w => w.Weight);
        var remainingSubscription = subscriptionAllocation;
        var rows = new List<NamedMetricRow>(weights.Count);

        for (var i = 0; i < weights.Count; i++)
        {
            var entry = weights[i];
            decimal modelSubscription;
            if (subscriptionAllocation <= 0m || totalWeight <= 0m)
            {
                modelSubscription = 0m;
            }
            else if (i == weights.Count - 1)
            {
                modelSubscription = remainingSubscription;
            }
            else
            {
                modelSubscription = Math.Round(
                    subscriptionAllocation * (entry.Weight / totalWeight),
                    2,
                    MidpointRounding.AwayFromZero);
                remainingSubscription -= modelSubscription;
            }

            var promptCount = entry.Group.Sum(a =>
            {
                usageById.TryGetValue(a.ExternalUsageRecordId, out var usage);
                var requests = usage?.RequestCount ?? 1;
                return Math.Max(1, ScaleByAllocation(requests, a.AllocationPercentage));
            });

            rows.Add(new NamedMetricRow
            {
                Name = entry.Group.Key,
                PromptCount = (int)Math.Min(int.MaxValue, promptCount),
                UsageBasedCost = entry.Cost,
                SubscriptionAllocation = modelSubscription
            });
        }

        return rows
            .OrderByDescending(r => r.UsageBasedCost)
            .ThenByDescending(r => r.SubscriptionAllocation)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCursorProvider(AIProvider? provider)
        => provider is null or AIProvider.Cursor;

    private async Task<Project> RequireProjectAsync(Guid projectId, CancellationToken cancellationToken)
        => await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(nameof(Project), projectId);

    private static IReadOnlyList<TokenCostModelRow> MergeTokenCostModelRows(
        IEnumerable<TokenCostModelRow> rows)
    {
        var merged = new Dictionary<string, TokenCostModelRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = string.IsNullOrWhiteSpace(row.Model) ? "unknown" : row.Model.Trim();
            if (!merged.TryGetValue(key, out var existing))
            {
                merged[key] = row with { Model = key };
                continue;
            }

            merged[key] = existing with
            {
                InputTokens = existing.InputTokens + row.InputTokens,
                OutputTokens = existing.OutputTokens + row.OutputTokens,
                CachedInputTokens = existing.CachedInputTokens + row.CachedInputTokens,
                ReasoningTokens = existing.ReasoningTokens + row.ReasoningTokens,
                TotalTokens = existing.TotalTokens + row.TotalTokens,
                EstimatedCost = Math.Round(
                    existing.EstimatedCost + row.EstimatedCost,
                    4,
                    MidpointRounding.AwayFromZero),
                ReportedCost = Math.Round(
                    existing.ReportedCost + row.ReportedCost,
                    4,
                    MidpointRounding.AwayFromZero)
            };
        }

        return merged.Values
            .OrderByDescending(r => r.EstimatedCost)
            .ThenBy(r => r.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long ScaleByAllocation(long value, decimal allocationPercentage)
    {
        if (value <= 0 || allocationPercentage <= 0m)
        {
            return 0;
        }

        if (allocationPercentage >= 100m)
        {
            return value;
        }

        return (long)Math.Round(value * (allocationPercentage / 100m), MidpointRounding.AwayFromZero);
    }

    private async Task<IReadOnlyList<EditorSession>> ListProjectSessionsAsync(
        Guid? projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var sessions = await _sessions.ListAsync(projectId, fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);
        return sessions.Where(s => s.ProjectId is not null).ToList();
    }

    private static IEnumerable<DateOnly> SessionDayKeys(EditorSession session)
    {
        var start = DateOnly.FromDateTime(session.StartedAtUtc.UtcDateTime);
        var end = DateOnly.FromDateTime((session.EndedAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime);
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    private static ActivitySummaryDto BuildActivitySummary(
        IReadOnlyList<PromptActivityEvent> events,
        IReadOnlyList<EditorSession> sessions,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        var now = DateTimeOffset.UtcNow;
        return new ActivitySummaryDto
        {
            PromptCount = events.Count(e => e.EventType == ActivityEventType.PromptSubmitted),
            AgentRuns = events.Count(e => e.EventType == ActivityEventType.AgentStarted),
            AgentDurationMilliseconds = SumAgentDuration(events),
            ActiveProjectTimeSeconds = sessions.Sum(s =>
                IntervalOverlap.Seconds(s.StartedAtUtc, s.EndedAtUtc, fromUtc, toUtc, now)),
            SessionCount = events.Select(e => e.EditorSessionId).Where(id => id is not null).Distinct().Count(),
            FailureCount = events.Count(e => e.EventType == ActivityEventType.AgentFailed),
            CancellationCount = events.Count(e => e.EventType == ActivityEventType.AgentCancelled),
            FromUtc = fromUtc,
            ToUtc = toUtc
        };
    }

    private static long SumAgentDuration(IEnumerable<PromptActivityEvent> events)
        => events
            .Where(e => e.EventType is ActivityEventType.AgentCompleted
                or ActivityEventType.AgentFailed
                or ActivityEventType.AgentCancelled)
            .Sum(e => e.DurationMilliseconds ?? 0);

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

    private sealed class TokenCostAggregate
    {
        public TokenCostAggregate(string model, CursorModelTokenRate rate)
        {
            Model = model;
            Rate = rate;
        }

        public string Model { get; }

        public CursorModelTokenRate Rate { get; }

        public long InputTokens { get; set; }

        public long OutputTokens { get; set; }

        public long CachedInputTokens { get; set; }

        public long ReasoningTokens { get; set; }

        public long TotalTokens { get; set; }

        public decimal EstimatedCost { get; set; }

        public decimal ReportedCost { get; set; }
    }
}
