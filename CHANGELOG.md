# Changelog

All notable changes to MCP Track Tokens are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Database-backed lazy paging for project prompts and timesheet entry browse lists (`pageIndex` / `pageSize`, prompt facets).

### Removed

- VS Code / Cursor editor extension package (`extensions/mcp-track-tokens-vscode`) and MSI/setup options to install it. Use Cursor hooks for prompt ingest.

### Fixed

- **Agent (min)** now sums completed prompt durations (milliseconds) instead of empty agent-end rows, then converts to minutes for display.
- **Get Rates** (Settings → Cursor token costs): more resilient Cursor docs download (URL / User-Agent fallbacks) and parsing of current docs where Auto is listed as **Auto Cost** in the model table (maps to Auto / `*`).
- Closing timesheets uses the last ended editor session for that project on the timesheet start day when end time is omitted.

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
