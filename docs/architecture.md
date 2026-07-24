# Architecture

MCP Track Tokens is a local-first stack that records **editor activity**, imports **Cursor usage exports**, and exposes **MCP tools** plus a **dashboard** for project-level time and cost views.

## Components

| Component | Location | Role |
| --- | --- | --- |
| Domain | `src/McpTrackTokens.Domain` | Entities, enums, privacy defaults |
| Application | `src/McpTrackTokens.Application` | Services, DTOs, options, attribution orchestration |
| Infrastructure | `src/McpTrackTokens.Infrastructure` | EF Core, SQLite/PostgreSQL, importers, repositories |
| Server | `src/McpTrackTokens.Server` | HTTP host, `/api/v1`, MCP tools, wwwroot dashboard |
| CLI | `src/McpTrackTokens.Cli` | `mcp-track-tokens` commands; hosts Server via `TrackingHost` |
| Dashboard | `src/McpTrackTokens.Dashboard` | React + Vite UI → copied to Server `wwwroot` |
| Hooks | `integrations/cursor-hooks` | Cursor stdin hooks → `POST /api/v1/events` |
| Windows MSI | `setup/McpTrackTokens.Tray.Setup` | Deploys tray host (API + HTTP MCP + dashboard), desktop shell, optional Cursor hooks |

## Runtime defaults

| Item | Value |
| --- | --- |
| Bind / URL | `http://127.0.0.1:5187` |
| Database | `~/.mcp-track-tokens/mcp-track-tokens.db` |
| Exports | `~/.mcp-track-tokens/exports/` |
| Logs | `~/.mcp-track-tokens/logs/` |
| Hook queue | `~/.mcp-track-tokens/queue/` |
| MCP server name | `mcp-track-tokens` `1.0.12` |

Configuration section: `Tracking` in `src/McpTrackTokens.Server/appsettings.json` (tray uses `src/McpTrackTokens.Tray/appsettings.json`).  
Environment overrides: `MCP_TRACK_TOKENS_*` via `TrackingEnvironmentVariables`.

## Request paths

```mermaid
flowchart LR
  H[Cursor hooks] -->|Bearer| E["/api/v1/events* /sessions*"]
  I[CLI import] --> Imp[CursorUsageImporter]
  M[MCP client] -->|stdio or /mcp| T[TrackingTools]
  UI[Dashboard] -->|Bearer| R["/api/v1/projects* /reports*"]
  E --> S[Application services]
  Imp --> S
  T --> S
  R --> S
  S --> DB[(DbContext)]
```

## Data model (conceptual)

- **Project** — named tracking target with optional repo path / remote URL / billing metadata.
- **Session** — editor session with heartbeats and inactivity threshold (default 15 minutes).
- **ActivityEvent** — prompt/agent/session events (length/hash; optional encrypted content).
- **ActivityWindow** — derived active-time windows for allocation.
- **ExternalUsage** — normalized rows from Cursor exports.
- **UsageAttribution** — links usage to projects with confidence + strategy.
- **ImportBatch** — file hash, format, dry-run/force semantics.
- **ApiKey** — hashed secrets for Bearer auth.

## Dual dataset model

| Dataset | Produced by | Answers |
| --- | --- | --- |
| Activity | Hooks, MCP session tools | What happened in the editor? |
| Usage | CSV/JSON import | What tokens/cost did Cursor report? |

Attribution **correlates** them; it does not prove a 1:1 mapping to internal Cursor meters.

## Hosting modes

1. **Windows MSI (recommended on Windows)** — tray host (`mcp-track-tokens-tray.exe`) runs `TrackingHost` in-process with HTTP API, HTTP MCP (`/mcp`), and dashboard `wwwroot` at `http://127.0.0.1:5187`. Desktop shell opens that URL. See [windows-msi.md](windows-msi.md).
2. **HTTP CLI** (`serve --http`) — same stack via `mcp-track-tokens` for development.
3. **Stdio MCP** (`serve --stdio`) — tool surface spawned by the editor when not using HTTP MCP.
4. **Docker Compose** — optional containerized HTTP host (not required for MSI users).
5. **CLI utilities** — short-lived migrate/import/export without a long-running HTTP process.

Typical Windows desktop setup: install the MSI, point Cursor at `http://127.0.0.1:5187/mcp`, and merge Cursor hooks. Do not run a second stdio MCP process against a different database.

## Layering rules

- Domain has no infrastructure dependencies.
- Application defines interfaces (`IReportService`, `ICursorUsageImporter`, …).
- Infrastructure implements persistence and CSV mapping.
- Server/CLI/Tray compose DI via `AddApplication()` + `AddInfrastructure()`.

## Related docs

- [Windows MSI](windows-msi.md)
- [Privacy](privacy.md)
- [Cursor hooks](cursor-hooks.md)
- [Usage imports](usage-imports.md)
- [Cost allocation](cost-allocation.md)
