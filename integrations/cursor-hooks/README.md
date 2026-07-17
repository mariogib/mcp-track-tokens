# Cursor hooks — MCP Track Tokens

Lightweight Node.js hook scripts that read Cursor JSON from **stdin**, POST sanitized activity events to the local tracking API, and **never block** the editor when the server is offline.

Full documentation: [`docs/cursor-hooks.md`](../../docs/cursor-hooks.md)

## Build

```bash
npm install
npm run lint
npm run test
npm run build
```

Entrypoints land in `dist/`.

## Install

```powershell
dotnet run --project src/McpTrackTokens.Cli -- install-cursor-hooks --yes
```

Or use `scripts/install-windows.ps1 -InstallHooks`. The CLI copies this package into `~/.cursor/mcp-track-tokens-hooks` and writes an **example** config — merge hook paths into your Cursor hooks settings yourself (names are version-dependent).

## Behaviour

1. Parse stdin JSON via tolerant adapters (`src/adapters.ts`).
2. Resolve workspace / git root / remote / branch when available.
3. Build a tracking event (**no prompt content by default**).
4. `POST /api/v1/events` with ≤ 2s timeout and one retry.
5. On failure, append to `~/.mcp-track-tokens/queue/cursor-events.jsonl` and flush later.
6. Exit `0` by default so Cursor is never interrupted.

## Environment

| Variable | Default |
| --- | --- |
| `MCP_TRACK_TOKENS_SERVER_URL` | `http://127.0.0.1:5187` |
| `MCP_TRACK_TOKENS_API_KEY` | (required) |
| `MCP_TRACK_TOKENS_STORE_PROMPT_CONTENT` | `false` |
| `MCP_TRACK_TOKENS_ENABLE_PROMPT_HASHING` | `false` (hooks client) |

## Payload assumptions

- Cursor supplies a JSON object on stdin; exact hook **names** and field names vary by Cursor version.
- Common aliases are accepted (`workspace_roots` / `workspaceRoots`, `session_id` / `sessionId`, etc.).
- Unknown properties are preserved in `metadata` (sanitized).
- Token/cost amounts are **not** expected on hook payloads — import Cursor CSV/JSON exports for billing data.

See `example-hooks-config.json` for a sample mapping. Adapt hook names to your Cursor version.
