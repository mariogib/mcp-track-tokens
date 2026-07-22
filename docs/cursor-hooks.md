# Cursor hooks

Integration package: `integrations/cursor-hooks` (`@mcp-track-tokens/cursor-hooks` **0.1.0**).

Hooks receive **version-dependent JSON on stdin** from Cursor, normalize it, sanitize privacy fields, and POST to the local tracking API.

## Install locations

| Item | Path |
| --- | --- |
| Scripts (CLI install) | `~/.cursor/mcp-track-tokens-hooks/` |
| Example config | `~/.cursor/mcp-track-tokens-hooks.example.json` |
| Repo source | `integrations/cursor-hooks/` |
| Built entrypoints | `integrations/cursor-hooks/dist/*.js` |

```powershell
dotnet run --project src/McpTrackTokens.Cli -- install-cursor-hooks --yes
dotnet run --project src/McpTrackTokens.Cli -- remove-cursor-hooks --yes
```

Install copies the hooks package (including `dist/`) into `~/.cursor/mcp-track-tokens-hooks` and writes an **example** config. You must merge hook paths into your actual Cursor hooks configuration — the CLI does not silently rewrite editor settings.

## Built scripts

| File | Cursor hook event(s) |
| --- | --- |
| `dist/prompt-submitted.js` | `beforeSubmitPrompt` |
| `dist/agent-started.js` | `subagentStart` |
| `dist/agent-completed.js` | `stop`, `subagentStop` (optional: `afterAgentResponse`) |
| `dist/agent-failed.js` | Optional (status-based completion often covers this) |
| `dist/agent-cancelled.js` | Optional (status-based completion often covers this) |
| `dist/session-started.js` | `sessionStart` |
| `dist/session-ended.js` | `sessionEnd` |
| `dist/diagnostics.js` | Local diagnostics helper (not a Cursor hook) |

Example mapping for `~/.cursor/hooks.json`: `integrations/cursor-hooks/example-hooks-config.json`.

## Compatibility check

Call MCP tool **`check_cursor_hooks`** (also listed under Dashboard → MCP Help → Tools) to verify:

- Installed Cursor version
- Hook scripts under `~/.cursor/mcp-track-tokens-hooks`
- `~/.cursor/hooks.json` schema (`"version": 1`) and current event names
- Command paths on disk
- Optional Node smoke test against a modern stdin payload
- **Ingests a `Heartbeat` probe event** stamped with the detected Cursor version (completes the end-to-end ingest check)
- Recent Cursor activity ingest (including the probe)

Status is `compatible`, `degraded`, or `incompatible`, with per-check details and recommendations. The report includes `probeEventId` / `probeIngestedAtUtc` when the probe succeeds.

## Runtime flow

1. Cursor invokes the hook with a JSON payload on stdin.
2. `adaptCursorPayload` accepts snake_case / camelCase aliases and folds unknown properties into `metadata` (never raw prompt text).
3. `privacySanitize` keeps prompt **length**; hash/content only when env flags allow.
4. Git metadata is resolved from workspace roots when available.
5. `POST {serverUrl}/api/v1/events` with `Authorization: Bearer …`, timeout ≤ 2s, one retry.
6. On failure, append to `~/.mcp-track-tokens/queue/cursor-events.jsonl`.
7. Exit code **0** unless `MCP_TRACK_TOKENS_STRICT_EXIT=1`.

## Environment variables

| Variable | Purpose |
| --- | --- |
| `MCP_TRACK_TOKENS_SERVER_URL` or `MCP_TRACK_TOKENS_URL` | Base URL (default `http://127.0.0.1:5187`) |
| `MCP_TRACK_TOKENS_API_KEY` | Bearer token (required for API) |
| `MCP_TRACK_TOKENS_TIMEOUT_MS` | HTTP timeout |
| `MCP_TRACK_TOKENS_ENABLE_PROMPT_HASHING` | Default false on hooks client |
| `MCP_TRACK_TOKENS_STORE_PROMPT_CONTENT` | Default false |
| `MCP_TRACK_TOKENS_QUEUE_PATH` | Offline queue directory |
| `MCP_TRACK_TOKENS_DEBUG` | Verbose stderr logging |
| `MCP_TRACK_TOKENS_STRICT_EXIT` | Non-zero exit on send failure |

`apiKeyEnv` in the example config names the env var Cursor should expose to hooks (`MCP_TRACK_TOKENS_API_KEY`).

## Version-dependent payloads

Cursor hook schemas evolve. The adapter is intentionally tolerant. Prefer the **Compatibility check** section (`check_cursor_hooks`) when validating a Cursor upgrade.

### Current Cursor event names

Wire scripts in `~/.cursor/hooks.json` using Cursor’s current names (not the older aliases):

| Cursor event | Script |
| --- | --- |
| `beforeSubmitPrompt` | `prompt-submitted.js` |
| `sessionStart` | `session-started.js` |
| `sessionEnd` | `session-ended.js` |
| `subagentStart` | `agent-started.js` |
| `subagentStop` / `stop` | `agent-completed.js` |

Require top-level `"version": 1`. Commands are objects: `{ "command": "...", "timeout": 5 }`.

### Assumptions

- Payload is a single JSON object on stdin.
- Timestamps may be ISO-8601 strings or epoch millis.
- Workspace / root paths may appear as `workspaceRoots`, `workspace_roots`, `cwd`, or similar aliases.
- Prompt text may appear under `prompt`, `text`, `content`, or `promptContent`.
- Agent/session identifiers may use `sessionId` / `session_id`, `conversationId`, `requestId`, etc.
- Missing fields are omitted; hooks still emit a minimal valid activity event when possible.

### Adaptation rules

- Prefer known canonical fields after alias resolution (`src/adapters.ts`).
- Unknown keys → `metadata` object, size-capped by server `MaxMetadataBytes` (default 16 KiB).
- Diagnostics redacts sensitive keys as `[redacted length=N]` unless store-content is enabled.
- Do not fail the user’s Cursor flow: default exit 0 even when the server is down (queued).

### What we do not assume

- That every Cursor version emits token counts on the hook payload (they usually do not).
- That prompt submission hooks fire for every UI surface (Composer vs Chat vs Agent may differ by version).
- That hooks alone equal complete cost telemetry — **import usage exports** for costs.

## Building hooks

```bash
cd integrations/cursor-hooks
npm ci
npm run build
npm test
```

## Security notes

- Hooks run with your user privileges and can read workspace paths.
- Keep the API key in the environment, not committed config.
- Leave prompt content storage disabled unless you need it and understand encryption requirements.
