using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using DomainValidationException = McpTrackTokens.Domain.Exceptions.ValidationException;

namespace McpTrackTokens.Infrastructure.Import;

/// <summary>
/// Imports Cursor usage CSV/JSON exports with preview, hashing, and partial-import support.
/// </summary>
public sealed class CursorUsageImporter : ICursorUsageImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICursorUsageFormatDetector _formatDetector;
    private readonly ICursorUsageColumnMapper _columnMapper;
    private readonly IExternalUsageNormalizer _normalizer;
    private readonly IFileHashService _fileHash;
    private readonly IImportBatchRepository _batches;
    private readonly IExternalUsageRepository _usage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TrackingOptions _options;

    public CursorUsageImporter(
        ICursorUsageFormatDetector formatDetector,
        ICursorUsageColumnMapper columnMapper,
        IExternalUsageNormalizer normalizer,
        IFileHashService fileHash,
        IImportBatchRepository batches,
        IExternalUsageRepository usage,
        IUnitOfWork unitOfWork,
        IOptions<TrackingOptions> options)
    {
        _formatDetector = formatDetector;
        _columnMapper = columnMapper;
        _normalizer = normalizer;
        _fileHash = fileHash;
        _batches = batches;
        _usage = usage;
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ImportPreviewDto> PreviewAsync(
        ImportCursorUsageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var parsed = await ParseFileAsync(request, cancellationToken).ConfigureAwait(false);
        var duplicateFile = !string.IsNullOrWhiteSpace(parsed.FileHash) &&
            await _batches.FindByFileHashAsync(parsed.FileHash, cancellationToken).ConfigureAwait(false) is not null;

        var warnings = new List<string>(parsed.Warnings);
        if (duplicateFile)
        {
            warnings.Add("A previous import batch with the same file hash already exists.");
        }

        var duplicateRows = 0;
        foreach (var record in parsed.Records.Where(r => !string.IsNullOrWhiteSpace(r.ExternalRecordId)))
        {
            var existing = await _usage
                .FindByExternalRecordIdAsync(parsed.Source, record.ExternalRecordId!, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                duplicateRows++;
            }
        }

        return new ImportPreviewDto
        {
            FileName = parsed.FileName,
            FileHash = parsed.FileHash,
            DetectedFormat = parsed.Source.ToString(),
            Source = parsed.Source,
            Columns = parsed.Columns,
            ColumnMappings = parsed.ColumnMappings,
            ReceivedCount = parsed.Records.Count + parsed.InvalidCount,
            ValidCount = parsed.Records.Count,
            DuplicateCount = duplicateRows,
            InvalidCount = parsed.InvalidCount,
            Warnings = warnings,
            SampleRecords = parsed.Records.Take(25).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<ImportResultDto> ImportAsync(
        ImportCursorUsageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var parsed = await ParseFileAsync(request, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(parsed.FileHash))
        {
            var existingBatch = await _batches
                .FindByFileHashAsync(parsed.FileHash, cancellationToken)
                .ConfigureAwait(false);
            if (existingBatch is not null)
            {
                if (!request.Force)
                {
                    return new ImportResultDto
                    {
                        ImportBatchId = existingBatch.Id,
                        DryRun = request.DryRun,
                        FileName = parsed.FileName,
                        FileHash = parsed.FileHash,
                        Source = parsed.Source,
                        Status = ImportStatus.Completed,
                        ReceivedCount = existingBatch.ReceivedCount,
                        ImportedCount = 0,
                        DuplicateCount = existingBatch.ReceivedCount,
                        FailedCount = 0,
                        ErrorSummary = "Duplicate file hash; import skipped. Pass Force=true to re-import.",
                        StartedAtUtc = started,
                        CompletedAtUtc = DateTimeOffset.UtcNow
                    };
                }

                // Allow re-import under Force by releasing the unique hash on the prior batch.
                existingBatch.FileHash = $"{existingBatch.FileHash}#superseded:{existingBatch.Id:N}";
                await _batches.UpdateAsync(existingBatch, cancellationToken).ConfigureAwait(false);
                await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (request.DryRun)
        {
            return new ImportResultDto
            {
                DryRun = true,
                FileName = parsed.FileName,
                FileHash = parsed.FileHash,
                Source = parsed.Source,
                Status = ImportStatus.Completed,
                ReceivedCount = parsed.Records.Count + parsed.InvalidCount,
                ImportedCount = parsed.Records.Count,
                DuplicateCount = 0,
                FailedCount = parsed.InvalidCount,
                ErrorSummary = parsed.InvalidCount > 0 ? string.Join("; ", parsed.RowErrors.Take(20)) : null,
                StartedAtUtc = started,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        var batch = ImportBatch.Create(parsed.Source, parsed.FileName, parsed.FileHash, started);
        batch.MarkInProgress();
        await _batches.AddAsync(batch, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var imported = 0;
        var duplicates = 0;
        var failed = parsed.InvalidCount;
        var errors = new List<string>(parsed.RowErrors);

        try
        {
            var entities = await _normalizer
                .NormalizeAsync(parsed.Source, parsed.Records, batch.Id, cancellationToken)
                .ConfigureAwait(false);

            var toInsert = new List<ExternalUsageRecord>();
            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(entity.ExternalRecordId))
                {
                    var existing = await _usage
                        .FindByExternalRecordIdAsync(entity.Source, entity.ExternalRecordId, cancellationToken)
                        .ConfigureAwait(false);
                    if (existing is not null)
                    {
                        duplicates++;
                        continue;
                    }
                }

                toInsert.Add(entity);
            }

            if (toInsert.Count > 0)
            {
                await _usage.AddRangeAsync(toInsert, cancellationToken).ConfigureAwait(false);
                imported = toInsert.Count;
            }

            batch.Complete(
                parsed.Records.Count + parsed.InvalidCount,
                imported,
                duplicates,
                failed);
            if (errors.Count > 0)
            {
                batch.ErrorSummary = string.Join("; ", errors.Take(50));
            }

            await _batches.UpdateAsync(batch, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            batch.Fail(ex.Message);
            await _batches.UpdateAsync(batch, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new ImportResultDto
        {
            ImportBatchId = batch.Id,
            DryRun = false,
            FileName = parsed.FileName,
            FileHash = parsed.FileHash,
            Source = parsed.Source,
            Status = batch.Status,
            ReceivedCount = batch.ReceivedCount,
            ImportedCount = batch.ImportedCount,
            DuplicateCount = batch.DuplicateCount,
            FailedCount = batch.FailedCount,
            ErrorSummary = batch.ErrorSummary,
            StartedAtUtc = batch.StartedAtUtc,
            CompletedAtUtc = batch.CompletedAtUtc
        };
    }

    private async Task<ParsedUsageFile> ParseFileAsync(
        ImportCursorUsageRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new DomainValidationException(nameof(request.FilePath), "FilePath is required.");
        }

        var path = TrackingOptions.ExpandPath(request.FilePath);
        if (!File.Exists(path))
        {
            throw new DomainValidationException(nameof(request.FilePath), $"File not found: {path}");
        }

        var fileName = Path.GetFileName(path);
        var hash = await _fileHash.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);

        UsageSource source;
        if (!string.IsNullOrWhiteSpace(request.Format) &&
            Enum.TryParse(request.Format, ignoreCase: true, out UsageSource forced))
        {
            source = forced;
        }
        else
        {
            source = await _formatDetector.DetectAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var timeZone = ResolveTimeZone(request.Timezone);
        await using var stream = File.OpenRead(path);

        return source is UsageSource.CursorJson
            ? await ParseJsonAsync(stream, fileName, hash, source, request.ColumnMappings, timeZone, cancellationToken)
                .ConfigureAwait(false)
            : await ParseCsvAsync(stream, fileName, hash, source, request.ColumnMappings, timeZone, cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<ParsedUsageFile> ParseCsvAsync(
        Stream stream,
        string fileName,
        string hash,
        UsageSource source,
        IReadOnlyDictionary<string, string>? overrides,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim
        };

        using var csv = new CsvReader(reader, config);
        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();
        var headers = csv.HeaderRecord?.ToList() ?? [];
        var mappings = _columnMapper.MapColumns(headers, overrides);

        var records = new List<NormalizedUsageRecordDto>();
        var errors = new List<string>();
        var invalid = 0;
        var rowNumber = 1;

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            try
            {
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in headers)
                {
                    row[header] = csv.GetField(header);
                }

                records.Add(MapRow(row, mappings, headers, timeZone));
            }
            catch (Exception ex)
            {
                invalid++;
                errors.Add($"Row {rowNumber}: {ex.Message}");
            }
        }

        var warnings = new List<string>();
        if (!mappings.ContainsKey("TimestampUtc"))
        {
            warnings.Add("No timestamp/date column was detected.");
        }

        return new ParsedUsageFile(fileName, hash, source, headers, mappings, records, invalid, errors, warnings);
    }

    private async Task<ParsedUsageFile> ParseJsonAsync(
        Stream stream,
        string fileName,
        string hash,
        UsageSource source,
        IReadOnlyDictionary<string, string>? overrides,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var records = new List<NormalizedUsageRecordDto>();
        var errors = new List<string>();
        var invalid = 0;
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        List<JsonElement> elements;
        try
        {
            using var document = JsonDocument.Parse(text);
            elements = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList(),
                JsonValueKind.Object when document.RootElement.TryGetProperty("data", out var data) &&
                                          data.ValueKind == JsonValueKind.Array
                    => data.EnumerateArray().Select(e => e.Clone()).ToList(),
                JsonValueKind.Object when document.RootElement.TryGetProperty("usage", out var usage) &&
                                          usage.ValueKind == JsonValueKind.Array
                    => usage.EnumerateArray().Select(e => e.Clone()).ToList(),
                JsonValueKind.Object => [document.RootElement.Clone()],
                _ => []
            };
        }
        catch (JsonException)
        {
            // JSONL fallback
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            elements = [];
            foreach (var line in lines)
            {
                try
                {
                    using var lineDoc = JsonDocument.Parse(line);
                    elements.Add(lineDoc.RootElement.Clone());
                }
                catch (JsonException ex)
                {
                    invalid++;
                    errors.Add($"JSONL line: {ex.Message}");
                }
            }
        }

        var sampleKeys = new List<string>();
        var index = 0;
        foreach (var element in elements)
        {
            index++;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    invalid++;
                    errors.Add($"Item {index}: expected a JSON object.");
                    continue;
                }

                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    columns.Add(property.Name);
                    row[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.ToString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText()
                    };
                    if (sampleKeys.Count < 64 && !sampleKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        sampleKeys.Add(property.Name);
                    }
                }

                var mappings = _columnMapper.MapColumns(sampleKeys, overrides);
                records.Add(MapRow(row, mappings, sampleKeys, timeZone));
            }
            catch (Exception ex)
            {
                invalid++;
                errors.Add($"Item {index}: {ex.Message}");
            }
        }

        var columnList = columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        var finalMappings = _columnMapper.MapColumns(columnList, overrides);
        if (!finalMappings.ContainsKey("TimestampUtc"))
        {
            warnings.Add("No timestamp/date field was detected.");
        }

        return new ParsedUsageFile(
            fileName,
            hash,
            source,
            columnList,
            finalMappings,
            records,
            invalid,
            errors,
            warnings);
    }

    private NormalizedUsageRecordDto MapRow(
        IReadOnlyDictionary<string, string?> row,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyList<string> allColumns,
        TimeZoneInfo timeZone)
    {
        string? GetMapped(string canonical)
            => mappings.TryGetValue(canonical, out var column) && row.TryGetValue(column, out var value)
                ? value
                : null;

        var timestampRaw = GetMapped("TimestampUtc");
        if (string.IsNullOrWhiteSpace(timestampRaw))
        {
            throw new InvalidOperationException("Missing timestamp/date value.");
        }

        var timestamp = ParseTimestamp(timestampRaw, timeZone);
        var inputTokens = ParseLong(GetMapped("InputTokens"));
        var outputTokens = ParseLong(GetMapped("OutputTokens"));
        var totalTokens = ParseLong(GetMapped("TotalTokens"));
        if (totalTokens is null && inputTokens is null && outputTokens is null)
        {
            // "Tokens" alone may have mapped to TotalTokens; already handled.
        }

        var knownColumns = new HashSet<string>(mappings.Values, StringComparer.OrdinalIgnoreCase);
        var unknown = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in allColumns)
        {
            if (knownColumns.Contains(column))
            {
                continue;
            }

            if (row.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                unknown[column] = value;
            }
        }

        string? metadataJson = null;
        if (unknown.Count > 0)
        {
            metadataJson = JsonSerializer.Serialize(unknown, JsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(metadataJson);
            if (bytes > _options.MaxMetadataBytes)
            {
                metadataJson = JsonSerializer.Serialize(
                    unknown.Take(10).ToDictionary(k => k.Key, v => v.Value),
                    JsonOptions);
            }
        }

        return new NormalizedUsageRecordDto
        {
            ExternalRecordId = NullIfWhiteSpace(GetMapped("ExternalRecordId")),
            TimestampUtc = timestamp,
            PeriodStartUtc = ParseOptionalTimestamp(GetMapped("PeriodStartUtc"), timeZone),
            PeriodEndUtc = ParseOptionalTimestamp(GetMapped("PeriodEndUtc"), timeZone),
            UserIdentifier = NullIfWhiteSpace(GetMapped("UserIdentifier")),
            Model = NullIfWhiteSpace(GetMapped("Model")),
            Provider = NullIfWhiteSpace(GetMapped("Provider")) ?? "Cursor",
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CachedInputTokens = ParseLong(GetMapped("CachedInputTokens")),
            ReasoningTokens = ParseLong(GetMapped("ReasoningTokens")),
            TotalTokens = totalTokens,
            ReportedCost = ParseDecimal(GetMapped("ReportedCost")),
            Currency = NullIfWhiteSpace(GetMapped("Currency")) ?? _options.DefaultCurrency,
            RequestCount = ParseInt(GetMapped("RequestCount")),
            ExternalSessionId = NullIfWhiteSpace(GetMapped("ExternalSessionId")),
            ExternalRequestId = NullIfWhiteSpace(GetMapped("ExternalRequestId")),
            ExternalConversationId = NullIfWhiteSpace(GetMapped("ExternalConversationId")),
            RepositoryPath = NullIfWhiteSpace(GetMapped("RepositoryPath")),
            RemoteUrl = NullIfWhiteSpace(GetMapped("RemoteUrl")),
            MetadataJson = metadataJson
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static DateTimeOffset ParseTimestamp(string value, TimeZoneInfo timeZone)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto.ToUniversalTime();
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            if (dt.Kind == DateTimeKind.Unspecified)
            {
                var unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
            }

            return new DateTimeOffset(dt.ToUniversalTime());
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return unix > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                : DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        throw new FormatException($"Unable to parse timestamp '{value}'.");
    }

    private static DateTimeOffset? ParseOptionalTimestamp(string? value, TimeZoneInfo timeZone)
        => string.IsNullOrWhiteSpace(value) ? null : ParseTimestamp(value, timeZone);

    private static long? ParseLong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return long.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new FormatException($"Unable to parse integer '{value}'.");
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new FormatException($"Unable to parse integer '{value}'.");
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimStart('$');
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new FormatException($"Unable to parse decimal '{value}'.");
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ParsedUsageFile(
        string FileName,
        string FileHash,
        UsageSource Source,
        IReadOnlyList<string> Columns,
        IReadOnlyDictionary<string, string> ColumnMappings,
        IReadOnlyList<NormalizedUsageRecordDto> Records,
        int InvalidCount,
        IReadOnlyList<string> RowErrors,
        IReadOnlyList<string> Warnings);
}
