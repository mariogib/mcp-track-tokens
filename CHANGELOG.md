# Changelog

All notable changes to MCP Track Tokens are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.35] — 2026-08-13

### Fixed
- Settings → Get Rates no longer fails with 502 when Cursor's docs CDN 404s `.md` downloads that send `Accept: text/markdown`. The client now requests `*/*`.
- Rate fetch requires the Cursor Models table (Grok/Composer) in addition to Other Models; Get Rates fails if those first-party rows are missing.
- When Auto Cost is no longer listed as a priced row, Get Rates keeps the built-in Auto/* fallback rates instead of dropping them.
- Project details Usage and Costs period filters use usage event time (`TimestampUtc`) instead of attribution import time (`CreatedAtUtc`), so past months and custom ranges match the When column.

## [1.0.34] — 2026-08-04

### Added
- Project Excel export writes numeric cells as real Excel numbers (2 decimals), adds SUM total rows under tables without corrupting Excel table XML, and drops synthetic Total/Summary data rows.
- Calculated cost by model detail lists show rate-card prices (per million) and average cost per token.
- Browse list column sorting via shared `DataTable` headers; lazy/remote pages re-fetch with allowlisted `sortBy`/`sortDirection` on prompts, sessions, and timesheets.

### Changed
- Project Settings no longer shows the separate JSON/CSV export card (use Overview → Export to Excel).

## [1.0.33] — 2026-08-04

### Changed
- SQLite date-range filters now use index-friendly UTC TEXT bounds instead of `unixepoch(substr(...))`, so time indexes are usable at high volume.
- High-volume tables gained composite indexes (prompts, sessions, usage attributions, timesheets, external usage) and dropped redundant single-column indexes.

## [1.0.32] — 2026-08-03

### Added
- Dashboard redirects to `/settings` when the Bearer API key is missing or rejected with 401 (desktop app and Vite/browser), and shows an error explaining why.

### Fixed
- Configured bootstrap API key (e.g. `OverTheMoon`) is reactivated on startup if it was previously revoked, so the default dashboard key keeps working.
- Revoking the last active API key is blocked so the dashboard cannot lock itself out.
- Creating an API key no longer requires a Bearer token, so Settings can recover from a missing/invalid local key.
- Revoked API keys can be permanently deleted from Settings → API keys.

## [1.0.31] — 2026-08-03

### Fixed
- MSI upgrade force-closes the tray/desktop hosts without a confirmation dialog: installer/session shutdown exits immediately (no cancel of end-session), and the MSI runs `taskkill /F` before replacing files if CloseApplication alone is not enough.

## [1.0.30] — 2026-08-03

### Fixed
- Autoclose of overnight open timesheets now applies a **day-boundary** close on the start calendar day (last session/prompt that day) instead of extending through idle days with `autoclosed` until the next project switch.
- MSI upgrade no longer hangs for hours when the tray host is running: CloseApplication `Timeout` is in **seconds** (was wrongly set to 8000), and the tray now exits on installer `WM_CLOSE` / end-session instead of only dropping the NotifyIcon.

## [1.0.29] — 2026-08-03

### Fixed
- Pressing F5 (or Ctrl+R) in the dashboard refreshes page content instead of doing nothing in the desktop WebView.
- Keyboard Back / Alt+← (and Forward / Alt+→) navigate React Router history in the desktop WebView host.

## [1.0.28] — 2026-07-30

### Fixed
- Database backup download no longer fails on Windows with “file is being used by another process” (SQLite connection pool held the temp backup file open).

## [1.0.27] — 2026-07-30

### Added
- Project details reporting, duration helpers, and Excel export on the dashboard.
- Enforce a single repository binding per project (DB unique constraint + detection/report updates).

### Fixed
- Store Cursor **cache-write** tokens as their own usage field (`CacheWriteTokens` / `Input (w/ Cache Write)`), and price them with the Settings **cache-write** rate in all calculated-cost paths (no longer folded into Input).

## [1.0.14] — 2026-07-27

### Added

- Database-backed `AppSettings` store so Tracking / Cursor token-rate preferences survive restarts.
- Shared `PopupForm` for dashboard add/edit dialogs (title bar, drag, close).
- Server-paged project sessions browse.

### Changed

- Auto-create projects for unknown repositories now defaults to **on**.
- Overview unallocated usage card deep-links to the Imported usage tab.

## [1.0.13] — 2026-07-24

### Added

- Dashboard **New project** form and `POST /api/v1/projects` (same fields as CLI/MCP register).
- Settings → Integrations: **Run Cursor hooks compatibility check** (`POST /api/v1/integrations/cursor-hooks/check`).
- Imported usage **Preview allocation** (dry-run) before **Apply allocation**.
- Per-row usage actions: allocate to closest prompt or to a selected project.
- Overview **Replay queue** for offline hook events (`POST /api/v1/integrations/offline-queue/replay`).
- MSI post-install additive merge of Cursor `hooks.json` (backup first; never deletes user entries).
- Import preview warning for aggregated Day/Requests/Usage Cost CSV shapes.

### Changed

- Hidden unfinished subscription methods (`ManualPercentage`, `TimeWindowMatch`, `ProportionalTimeAllocation`) from Settings until configured.
- README / architecture copy updated for Cursor-hooks-only ingest and version **1.0.13**.

### Fixed

- MSI upgrades close the tray/desktop hosts before replacing files and restart the tray after install (including silent upgrades).
- Bumped product/assembly version so upgrades replace server binaries after the 1.0.12 same-version reinstall skip.

## [1.0.12] — 2026-07-24

### Added

- Database-backed lazy paging for project prompts and timesheet entry browse lists (`pageIndex` / `pageSize`, prompt facets).
- Delete selected unallocated prompts (`POST /api/v1/activity/delete`).

### Removed

- VS Code / Cursor editor extension package (`extensions/mcp-track-tokens-vscode`) and MSI/setup options to install it. Use Cursor hooks for prompt ingest.

### Fixed

- **Agent (min)** now sums completed prompt durations (milliseconds) instead of empty agent-end rows, then converts to minutes for display.
- **Get Rates** (Settings → Cursor token costs): more resilient Cursor docs download (URL / User-Agent fallbacks) and parsing of current docs where Auto is listed as **Auto Cost** in the model table (maps to Auto / `*`).
- Closing timesheets uses the last ended editor session for that project on the timesheet start day when end time is omitted.
- MSI / assembly version bumped so upgrades replace `McpTrackTokens.Server.dll` (same-version reinstalls left an old API under a newer dashboard, causing delete unallocated prompts to return **Not found.**).

## [1.0.11] — 2026-07-23

### Added

- Database-backed lazy paging for project prompts and timesheet entry browse lists.

## [1.0.0] — 2026-07-17

### Added

- Initial release of MCP Track Tokens.
- Local .NET 8 tracking server with HTTP API (`http://127.0.0.1:5187`) and optional stdio MCP.
- SQLite persistence at `~/.mcp-track-tokens/mcp-track-tokens.db` (PostgreSQL supported via configuration).
- CLI (`mcp-track-tokens`) with commands: `serve`, `migrate`, `status`, `register-project`, `list-projects`, `import-cursor-usage`, `export`, `reconcile`, `create-api-key`, `install-cursor-hooks`, `remove-cursor-hooks`.
- Twenty MCP tools for project registration, sessions, activity, usage, cost, reconciliation, import, and reports.
- React dashboard served from Server `wwwroot`.
- VS Code / Cursor extension with `@track` chat participant and session commands.
- Cursor hooks integration with payload adaptation, privacy sanitization, and offline event queue.
- Cursor usage CSV/JSON import with flexible column aliases, attribution engine, and subscription allocation.
- Privacy defaults: no prompt/response content stored; optional hashing; content encryption only when explicitly enabled.
- Install scripts, Docker image, sample imports, and documentation under `docs/`.

[1.0.0]: https://github.com/lunarq/mcp-track-tokens/releases/tag/v1.0.0
