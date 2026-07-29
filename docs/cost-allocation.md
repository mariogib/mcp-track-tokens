# Cost attribution and subscription allocation

MCP Track Tokens separates **usage-based cost** (from imports) and **subscription allocation** (flat Cursor plan share). Project reports can show both; they are not blended into a single opaque meter.

## Usage attribution

Implementation: `AttributionEngine` in Application.

Strategies are evaluated in order. Confidence is recorded; **Low is never silently promoted to Certain**.

| Order | Strategy | Typical confidence |
| --- | --- | --- |
| 1 | Repository path / remote URL match | Certain (`RepositoryReported`) |
| 2 | Explicit project id on the record | Certain (`ExplicitProject`) |
| 3 | External session id match | High |
| 4 | External request / conversation id | High |
| 5 | Single active project session at timestamp | High (`SingleActiveSession`) |
| 6 | Single covering activity window | Medium (`TimeWindowMatch`) |
| 7 | Multiple overlapping windows → proportional by seconds | Low (`ProportionalTimeAllocation`) |
| 8 | Else | Unallocated |

Manual override: MCP `allocate_usage` / application `AttributeManualAsync` → `Manual` / Certain.

### Reconciliation

```powershell
dotnet run --project src/McpTrackTokens.Cli -- reconcile --from 2026-07-01 --to 2026-07-17
dotnet run --project src/McpTrackTokens.Cli -- reconcile --dry-run --include-low-confidence
```

Default CLI range: last **7** days. MCP tool: `run_usage_reconciliation`.

## Subscription allocation

Configuration (`Tracking` / env):

| Key | Default | Meaning |
| --- | --- | --- |
| `CursorSubscriptionAmount` | `0` | Flat amount to distribute |
| `CursorSubscriptionCurrency` | `USD` | Currency label |
| `CursorAllocationMethod` | `NotAllocated` | Distribution rule |

### Methods (`AllocationRuleType`)

| Method | Behavior |
| --- | --- |
| `NotAllocated` | No subscription share assigned |
| `EqualAcrossActiveProjects` | Equal split among active projects in range |
| `ByActiveProjectTime` | Weight by editor session duration seconds |
| `ByPromptCount` | Weight by prompt events |
| `ByAgentDuration` | Weight by agent durations |
| `ManualPercentage` | Operator-defined percentages |
| `TimeWindowMatch` / `ProportionalTimeAllocation` | Weight by active time (shared calculator path) |

Proportional math (`CostAllocationCalculator`): weights normalize to 100%; remainder cents/percent go to the last non-zero weight to avoid drift.

## Project cost view

`get_project_cost` / `GET /api/v1/projects/{id}/cost`:

- `UsageBasedCursorCost` — sum of attributed imported costs
- `SubscriptionAllocation` — share of configured subscription
- Combined display for humans; keep semantics distinct for billing honesty

## Practical workflow

1. Register projects with accurate repo paths / remotes.
2. Run Cursor hooks so editor sessions are recorded.
3. Import Cursor exports regularly.
4. Review unallocated usage/activity.
5. Reconcile; set subscription method if you need plan cost distribution.
6. Export markdown/CSV for clients (`export --type project-cost`).

## Honest accounting

- Attribution is best-effort correlation, not proof of Cursor’s internal routing.
- Aggregated imports reduce precision.
- Subscription allocation is a **policy**, not a measured per-request charge.
