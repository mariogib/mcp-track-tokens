using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Services;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IActivityEventRepository"/>.
/// </summary>
public sealed class ActivityEventRepository : IActivityEventRepository
{
    private readonly TrackingDbContext _db;

    public ActivityEventRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<PromptActivityEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.PromptActivityEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<PromptActivityEvent?> FindByExternalIdAsync(
        string externalEventId,
        EditorType editor,
        CancellationToken cancellationToken = default)
        => _db.PromptActivityEvents
            .FirstOrDefaultAsync(
                e => e.ExternalEventId == externalEventId && e.Editor == editor,
                cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(PromptActivityEvent activityEvent, CancellationToken cancellationToken = default)
        => await _db.PromptActivityEvents.AddAsync(activityEvent, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateAsync(PromptActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        var entry = _db.Entry(activityEvent);
        if (entry.State == EntityState.Detached)
        {
            _db.PromptActivityEvents.Update(activityEvent);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PromptActivityEvent>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        bool? unallocatedOnly = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();
        var query = _db.PromptActivityEvents.AsNoTracking().AsQueryable();

        if (projectId is Guid pid)
        {
            query = query.Where(e => e.ProjectId == pid);
        }

        if (unallocatedOnly == true)
        {
            query = query.Where(e =>
                e.ProjectId == null ||
                e.AttributionMethod == AttributionMethod.Unallocated);
        }

        return await SqliteDateTimeQuery.MaterializeAsync(
            query,
            e => e.TimestampUtc >= from && e.TimestampUtc <= to,
            items => items.OrderByDescending(e => e.TimestampUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(
        ActivityEventPageFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (where, args) = BuildBrowseWhere(filter);
        var sql = "SELECT COUNT(*) AS \"Value\" FROM PromptActivityEvents " + where;
        return await _db.Database
            .SqlQueryRaw<int>(sql, args.ToArray())
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PromptActivityEvent>> ListPagedAsync(
        ActivityEventPageFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (skip, take) = SqliteDateTimePaging.NormalizePage(pageIndex, pageSize);
        var (where, args) = BuildBrowseWhere(filter);
        var sql = new StringBuilder()
            .Append("SELECT * FROM PromptActivityEvents ")
            .Append(where)
            .Append(CultureInfo.InvariantCulture, $" ORDER BY TimestampUtc DESC, Id DESC LIMIT {{{args.Count}}} OFFSET {{{args.Count + 1}}}");
        args.Add(take);
        args.Add(skip);

        return await _db.PromptActivityEvents
            .FromSqlRaw(sql.ToString(), args.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PromptFacetsDto> GetPromptFacetsAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var filter = new ActivityEventPageFilter
        {
            ProjectId = projectId,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            PromptSubmittedOnly = true
        };
        var (where, args) = BuildBrowseWhere(filter);
        var argsArray = args.ToArray();

        var models = await _db.Database
            .SqlQueryRaw<string>(
                "SELECT DISTINCT Model AS \"Value\" FROM PromptActivityEvents " + where
                + " AND Model IS NOT NULL AND TRIM(Model) <> '' ORDER BY Model",
                argsArray)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var branches = await _db.Database
            .SqlQueryRaw<string>(
                "SELECT DISTINCT Branch AS \"Value\" FROM PromptActivityEvents " + where
                + " AND Branch IS NOT NULL AND TRIM(Branch) <> '' ORDER BY Branch",
                argsArray)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var eventTypes = await _db.Database
            .SqlQueryRaw<string>(
                "SELECT DISTINCT EventType AS \"Value\" FROM PromptActivityEvents " + where
                + " AND EventType IS NOT NULL AND TRIM(EventType) <> '' ORDER BY EventType",
                argsArray)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var days = await _db.Database
            .SqlQueryRaw<string>(
                "SELECT DISTINCT substr(TimestampUtc, 1, 10) AS \"Value\" FROM PromptActivityEvents "
                + where + " ORDER BY 1 DESC",
                argsArray)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PromptFacetsDto
        {
            Models = models,
            Branches = branches,
            EventTypes = eventTypes,
            Days = days
        };
    }

    private static (string WhereSql, List<object> Args) BuildBrowseWhere(ActivityEventPageFilter filter)
    {
        var where = new StringBuilder("WHERE 1=1");
        var args = new List<object>();

        if (filter.ProjectId is Guid projectId)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND ProjectId = {{{args.Count}}}");
            args.Add(projectId);
        }

        SqliteDateTimePaging.AppendUnixRange(where, args, "TimestampUtc", filter.FromUtc, filter.ToUtc);

        if (filter.PromptSubmittedOnly)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND EventType = {{{args.Count}}}");
            args.Add(nameof(ActivityEventType.PromptSubmitted));
        }
        else if (!string.IsNullOrWhiteSpace(filter.EventType))
        {
            where.Append(CultureInfo.InvariantCulture, $" AND EventType = {{{args.Count}}}");
            args.Add(filter.EventType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            where.Append(CultureInfo.InvariantCulture, $" AND Status = {{{args.Count}}}");
            args.Add(filter.Status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Model))
        {
            where.Append(CultureInfo.InvariantCulture, $" AND Model = {{{args.Count}}}");
            args.Add(filter.Model.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Branch))
        {
            where.Append(CultureInfo.InvariantCulture, $" AND Branch = {{{args.Count}}}");
            args.Add(filter.Branch.Trim());
        }

        var search = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = "%" + SqliteDateTimePaging.EscapeLike(search) + "%";
            where.Append(CultureInfo.InvariantCulture,
                $" AND (IFNULL(Model,'') LIKE {{{args.Count}}} ESCAPE '\\' OR IFNULL(Branch,'') LIKE {{{args.Count}}} ESCAPE '\\' OR IFNULL(RepositoryPath,'') LIKE {{{args.Count}}} ESCAPE '\\' OR IFNULL(EventType,'') LIKE {{{args.Count}}} ESCAPE '\\' OR IFNULL(Editor,'') LIKE {{{args.Count}}} ESCAPE '\\' OR IFNULL(Status,'') LIKE {{{args.Count}}} ESCAPE '\\')");
            args.Add(pattern);
        }

        return (where.ToString(), args);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PromptActivityEvent>> ListBySessionAsync(
        Guid editorSessionId,
        CancellationToken cancellationToken = default)
        => await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking().Where(e => e.EditorSessionId == editorSessionId),
            orderBy: items => items.OrderByDescending(e => e.TimestampUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLatestPromptTimestampAsync(
        Guid editorSessionId,
        CancellationToken cancellationToken = default)
    {
        var matches = await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking()
                .Where(e =>
                    e.EditorSessionId == editorSessionId &&
                    e.EventType == ActivityEventType.PromptSubmitted),
            orderBy: items => items.OrderByDescending(e => e.TimestampUtc),
            take: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return matches.FirstOrDefault()?.TimestampUtc;
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLatestPromptTimestampForProjectAsync(
        Guid projectId,
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtcInclusive.ToUniversalTime();
        var to = toUtcExclusive.ToUniversalTime();
        var matches = await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking()
                .Where(e =>
                    e.ProjectId == projectId &&
                    e.EventType == ActivityEventType.PromptSubmitted),
            e => e.TimestampUtc >= from && e.TimestampUtc < to,
            items => items.OrderByDescending(e => e.TimestampUtc),
            take: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return matches.FirstOrDefault()?.TimestampUtc;
    }

    /// <inheritdoc />
    public Task<PromptActivityEvent?> FindByExternalRequestIdAsync(
        string externalRequestId,
        CancellationToken cancellationToken = default)
        => _db.PromptActivityEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ExternalRequestId == externalRequestId, cancellationToken);

    /// <inheritdoc />
    public async Task<PromptActivityEvent?> FindByExternalConversationIdAsync(
        string externalConversationId,
        CancellationToken cancellationToken = default)
    {
        var matches = await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking()
                .Where(e => e.ExternalConversationId == externalConversationId),
            orderBy: items => items.OrderByDescending(e => e.TimestampUtc),
            take: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return matches.FirstOrDefault();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Does not exclude prompts that already have usage attributions.
    /// Only prompts whose model matches <paramref name="model"/> are considered.
    /// </remarks>
    public async Task<PromptActivityEvent?> FindClosestPriorPromptWithProjectAsync(
        DateTimeOffset timestampUtc,
        string? model,
        CancellationToken cancellationToken = default)
    {
        var at = TimestampPrecision.RoundToSecond(timestampUtc);
        // Scan far enough back for long gaps between prompt and later usage rows.
        var from = at - PromptActiveWindow.MaxLookback;
        var to = at.AddSeconds(1);

        var candidates = await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking()
                .Where(e =>
                    e.EventType == ActivityEventType.PromptSubmitted &&
                    e.ProjectId != null),
            e => e.TimestampUtc >= from && e.TimestampUtc <= to,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return candidates
            .Select(e => (Event: e, Second: TimestampPrecision.RoundToSecond(e.TimestampUtc)))
            .Where(x => x.Second <= at && ModelsMatch(x.Event.Model, model))
            .OrderByDescending(x => x.Second)
            .ThenBy(x => x.Event.Id)
            .Select(x => x.Event)
            .FirstOrDefault();
    }

    private static bool ModelsMatch(string? promptModel, string? usageModel)
    {
        var left = CursorTokenCostCalculator.NormalizeModelName(promptModel) ?? string.Empty;
        var right = CursorTokenCostCalculator.NormalizeModelName(usageModel) ?? string.Empty;
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        return string.Equals(
            CursorTokenCostCalculator.NormalizeModelKey(left),
            CursorTokenCostCalculator.NormalizeModelKey(right),
            StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<int> CountUnallocatedAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc?.ToUniversalTime();
        var to = toUtc?.ToUniversalTime();
        var items = await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking()
                .Where(e => e.ProjectId == null || e.AttributionMethod == AttributionMethod.Unallocated),
            e => (from is null || e.TimestampUtc >= from) && (to is null || e.TimestampUtc <= to),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return items.Count;
    }

    /// <inheritdoc />
    public async Task AssignProjectAsync(
        IReadOnlyList<Guid> eventIds,
        Guid projectId,
        AttributionMethod method,
        AttributionConfidence confidence,
        CancellationToken cancellationToken = default)
    {
        if (eventIds.Count == 0)
        {
            return;
        }

        var events = await _db.PromptActivityEvents
            .Where(e => eventIds.Contains(e.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var activityEvent in events)
        {
            activityEvent.ProjectId = projectId;
            activityEvent.AttributionMethod = method;
            activityEvent.AttributionConfidence = confidence;
        }
    }
}
