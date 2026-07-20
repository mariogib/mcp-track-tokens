# Troubleshooting

## Server will not start

1. Confirm .NET 8 runtime/SDK: `dotnet --info`.
2. Check port **5187** is free: another `mcp-track-tokens` instance may be bound.
3. Inspect logs under `~/.mcp-track-tokens/logs/`.
4. Run migrations: `mcp-track-tokens migrate`.
5. Verify `MCP_TRACK_TOKENS_DATABASE_PATH` is writable.

## Health checks

```text
GET http://127.0.0.1:5187/health   → public liveness
GET http://127.0.0.1:5187/ready   → readiness (DB)
```

If `/health` fails, the process is down or bound elsewhere.  
If `/health` works but `/ready` fails, check SQLite path permissions or Postgres connection string.

## 401 Unauthorized

- API and `/mcp` require `Authorization: Bearer <key>`.
- Create a key: `mcp-track-tokens create-api-key --name local` (plaintext shown once).
- Set `MCP_TRACK_TOKENS_API_KEY` for hooks and MCP stdio env.
- Dashboard: Settings page → stores key in `localStorage` as `mcp-track-tokens-api-key`.

## Hooks not recording

1. Server running on `http://127.0.0.1:5187`.
2. `MCP_TRACK_TOKENS_API_KEY` visible to the hook process.
3. Hook scripts installed under `~/.cursor/mcp-track-tokens-hooks/dist/`.
4. Cursor hooks config points at those scripts (merge from `mcp-track-tokens-hooks.example.json`).
5. Check offline queue: `~/.mcp-track-tokens/queue/cursor-events.jsonl`.
6. Enable `MCP_TRACK_TOKENS_DEBUG=1` for stderr diagnostics.
7. Remember: hooks are version-dependent; not every UI action fires every event.

## Extension not tracking prompts

- Install and enable the VSIX; reload the window.
- `mcpTrackTokens.serverUrl` matches the running server.
- Use **`@track`** for guaranteed chat observability.
- `Test Server Connection` command.
- Auto-session settings only cover supported prompt paths — not all editor AI surfaces.

## Imports produce zero / unallocated rows

- Validate CSV headers against [usage-imports.md](usage-imports.md).
- Try `--dry-run` to see parse results without persisting.
- Register projects with matching repository paths/remotes before expecting Certain attribution.
- Use dashboard unallocated views or `get_unallocated_usage`.
- Run `reconcile --dry-run` then without dry-run.

## Costs look wrong

- Confirm the export currency and that `ReportedCost` / `Amount` / `Usage Cost` mapped correctly.
- Subscription amount is separate from usage cost — check `CursorAllocationMethod`.
- Do not expect live token capture without an import.

## Dashboard blank or CORS errors

- Production: open `http://127.0.0.1:5187/` (same origin as API).
- Dev Vite (`5173`) proxies to `5187` — start the API first.
- Ensure `wwwroot` was copied from `src/McpTrackTokens.Dashboard/dist`.
- CORS allowlist is localhost / 127.0.0.1 / ::1 only.

## MCP tools missing in Cursor

- Stdio config must point at `mcp-track-tokens serve --stdio` (or `dotnet run … -- serve --stdio`).
- HTTP MCP is off by default (`EnableHttpMcp`); enable only if you intentionally use `/mcp`.
- Restart Cursor after editing MCP config.
- Verify the process starts: run the same command in a terminal.
- If logs show `Failed to open SSE stream: Not Found`: `GET /mcp` must return **405** (SSE unsupported in stateless mode), not SPA-fallback **404**. Reinstall/restart the tray, then Reconnect MCP.
- If logs show `-32001 Session not found` / `Failed to start MCP session reinitialization`: the tray restarted and Cursor still holds a stateful session id. Prefer **stateless** HTTP MCP (current default). Reconnect MCP in Cursor (or reload the window) after upgrading.

## Docker

- Volume mount `/data` must be writable by uid 10001 (`mtt` user).
- Publish `5187:5187`.
- Create an API key inside the container or set env before clients connect.
- `SERVER_URL` for browser clients on the host remains `http://127.0.0.1:5187`.

## Still stuck

```powershell
mcp-track-tokens status
curl http://127.0.0.1:5187/health
curl http://127.0.0.1:5187/ready
```

Collect: CLI version/build, OS, anonymized hook payload shape (redacted), import header row, and whether activity vs usage is the failing dataset.
