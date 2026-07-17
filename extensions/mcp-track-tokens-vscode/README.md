# MCP Track Tokens — VS Code Extension

Companion extension for **MCP Track Tokens**. Records AI prompt activity through the `@track` chat participant, detects the active Git repository, and manages project sessions against a local tracking server.

Publisher: `mabatar` · Extension: `mcp-track-tokens`

## What it tracks

| Source | Observable? |
|--------|-------------|
| Prompts sent to **`@track`** | Yes — fully recorded |
| Built-in GitHub Copilot Chat / other chat UIs | **No** — VS Code does not expose a public catch-all prompt event |
| Cursor editor (when APIs are available) | Partial — depends on which VS Code APIs Cursor exposes |

## Limitations (important)

1. **Only `@track` prompts are guaranteed observable** in Visual Studio Code.
2. The extension **cannot inspect every Copilot Chat prompt**.
3. **Cursor compatibility is best-effort** and depends on exposed VS Code APIs (chat participants, `vscode.lm`, Git extension).
4. Prompt **content is not sent** unless `mcpTrackTokens.storePromptContent` is enabled. By default only length (and optional hash) are recorded.
5. API keys are stored in **SecretStorage**, never in `settings.json`.

## Commands

- MCP Track Tokens: Register Current Project
- MCP Track Tokens: Start Project Session
- MCP Track Tokens: Stop Project Session
- MCP Track Tokens: Show Current Tracking Status
- MCP Track Tokens: Open Dashboard
- MCP Track Tokens: Assign Unallocated Activity
- MCP Track Tokens: Test Server Connection
- MCP Track Tokens: Copy Repository Information

## Settings

| Setting | Default | Notes |
|---------|---------|--------|
| `mcpTrackTokens.serverUrl` | `http://127.0.0.1:5187` | Tracking server base URL |
| `mcpTrackTokens.autoStartSession` | `true` | Start session on `@track` prompt |
| `mcpTrackTokens.inactivityThresholdMinutes` | `15` | Pause after inactivity |
| `mcpTrackTokens.enableHeartbeat` | `true` | Session heartbeats |
| `mcpTrackTokens.heartbeatIntervalMinutes` | `5` | Heartbeat interval |
| `mcpTrackTokens.enablePromptHashing` | `false` | Optional SHA-256 hash |
| `mcpTrackTokens.storePromptContent` | `false` | Privacy-sensitive |
| `mcpTrackTokens.showStatusBar` | `true` | Status bar visibility |
| `mcpTrackTokens.defaultProject` | `""` | Optional project GUID |
| `mcpTrackTokens.logLevel` | `info` | Log verbosity |

API key: use **Test Server Connection** / first write command — stored via SecretStorage.

## Status bar

```text
$(record) Track: Project Name
```

States: **Tracking** · **Paused** · **Unallocated** · **Server Offline**

## Offline queue

When the server is unreachable, events are appended to:

```text
~/.mcp-track-tokens/queue/vscode-events.jsonl
```

Events are deduplicated by `externalEventId`, capped by max size, and flushed on later successful requests. Users are warned if oldest events must be dropped.

## Chat participant

```text
@track Create an EF Core repository for campaign allocation.
@track Explain this service.
```

Flow: record `PromptSubmitted` → optional session start → `vscode.lm.selectChatModels` / `sendRequest` stream → record `AgentCompleted` / `AgentCancelled` / `AgentFailed`.

## Develop

```bash
npm install
npm run lint
npm run test
npm run compile
npm run package
```

`npm run package` produces a `.vsix` via `@vscode/vsce`.

## Install from VSIX

```bash
code --install-extension mcp-track-tokens-*.vsix
```

Ensure the MCP Track Tokens server is running and you have created a tracking API key before recording events.
