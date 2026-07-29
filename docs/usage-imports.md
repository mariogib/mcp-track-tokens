# Usage imports

Cursor **does not** stream authoritative token/cost meters into MCP Track Tokens automatically. Costs and token totals come from **usage export files** you import.

## CLI

```powershell
dotnet run --project src/McpTrackTokens.Cli -- import-cursor-usage --file .\export.csv
dotnet run --project src/McpTrackTokens.Cli -- import-cursor-usage --file .\export.csv --dry-run
dotnet run --project src/McpTrackTokens.Cli -- import-cursor-usage --file .\export.csv --force
dotnet run --project src/McpTrackTokens.Cli -- import-cursor-usage --file .\export.json --format json
```

Also available via HTTP:

- `POST /api/v1/imports/cursor` (file path on server machine)
- `POST /api/v1/imports/cursor/upload` (multipart upload)
- MCP tool `import_cursor_usage`

## Formats

- CSV (most common Cursor exports)
- JSON (single object or array of row-like objects)

Duplicate protection uses a content hash of the file unless `--force` is set.

## Column aliases

Mapper: `src/McpTrackTokens.Infrastructure/Import/CursorUsageColumnMapper.cs`.

Headers are normalized (case, spaces, underscores, hyphens). Examples:

| Canonical | Accepted headers (examples) |
| --- | --- |
| TimestampUtc | `Date`, `Timestamp`, `Day`, `DateTime`, `Usage Date` |
| Model | `Model`, `Model Name` |
| InputTokens | `Input Tokens`, `Prompt Tokens` |
| OutputTokens | `Output Tokens`, `Completion Tokens` |
| TotalTokens | `Total Tokens`, `Tokens`, `Token Count` |
| CachedInputTokens | `Cached Input Tokens`, `Cache Tokens`, `Cache Read` |
| CacheWriteTokens | `Cache Write Tokens`, `Input (w/ Cache Write)` |
| ReasoningTokens | `Reasoning Tokens` |
| ReportedCost | `Cost`, `Amount`, `Usage Cost`, `Price` |
| Currency | `Currency`, `CCY` |
| RequestCount | `Requests`, `Request Count`, `Qty` |
| ExternalSessionId | `Session ID`, `Session` |
| ExternalRequestId | `Request ID` |
| RepositoryPath / RemoteUrl | `Repo`, `Remote`, `Repository` |

### Sample variations

| File | Shape |
| --- | --- |
| `samples/imports/cursor-usage-sample.csv` | `Date,Model,Input Tokens,Output Tokens,...` |
| `samples/imports/cursor-usage-tokens-amount.csv` | `Timestamp,Model,Tokens,Amount` |
| `samples/imports/cursor-usage-day-requests.csv` | `Day,Requests,Usage Cost` |
| `samples/imports/cursor-usage-sample.json` | JSON object with spaced headers |

## After import

1. Rows land as `ExternalUsage` under an `ImportBatch`.
2. `AttributionEngine` attempts project linkage (see [cost-allocation.md](cost-allocation.md)).
3. Unallocated usage appears in dashboard / `get_unallocated_usage`.
4. Run `reconcile` to propose or apply additional attributions.

```powershell
dotnet run --project src/McpTrackTokens.Cli -- reconcile --dry-run
```

## Limitations

- Export schemas change; unknown columns are ignored (or kept only if mapped).
- Aggregated daily rows (`Day,Requests,Usage Cost`) have less session-level attribution precision than per-request exports.
- Importing does not create editor activity; it only adds the usage dataset.
