# VS Code / Cursor extension

Package: `extensions/mcp-track-tokens-vscode`  
Extension id style: publisher `mabatar`, name `mcp-track-tokens`, version **0.1.0**  
Engines: VS Code `^1.85.0`  
Main: `dist/extension.js`

## What it provides

- Project registration and session start/stop against the local server
- Status bar + dashboard open helper
- Connection test and repo info copy
- Chat participant **`@track`** for guaranteed prompt activity recording in supported chat flows

The extension does **not** passively intercept every Copilot/Cursor model call. For ambient Cursor coverage, also install [hooks](cursor-hooks.md).

## Build and package

```powershell
npm --prefix extensions/mcp-track-tokens-vscode ci
npm --prefix extensions/mcp-track-tokens-vscode run build
npm --prefix extensions/mcp-track-tokens-vscode run package
# → extensions/mcp-track-tokens-vscode/mcp-track-tokens-0.1.0.vsix
```

Install:

```powershell
code --install-extension extensions/mcp-track-tokens-vscode/mcp-track-tokens-0.1.0.vsix
cursor --install-extension extensions/mcp-track-tokens-vscode/mcp-track-tokens-0.1.0.vsix
```

`scripts/install-windows.ps1 -InstallExtension` / `install-linux.sh --install-extension` print or run the install command after packaging; they do **not** rewrite `settings.json` silently.

## Commands

| Command ID | Title |
| --- | --- |
| `mcpTrackTokens.registerProject` | Register Current Project |
| `mcpTrackTokens.startSession` | Start Project Session |
| `mcpTrackTokens.stopSession` | Stop Project Session |
| `mcpTrackTokens.showStatus` | Show Current Tracking Status |
| `mcpTrackTokens.openDashboard` | Open Dashboard |
| `mcpTrackTokens.assignUnallocated` | Assign Unallocated Activity |
| `mcpTrackTokens.testConnection` | Test Server Connection |
| `mcpTrackTokens.copyRepoInfo` | Copy Repository Information |

## Settings (`mcpTrackTokens.*`)

| Setting | Default | Description |
| --- | --- | --- |
| `serverUrl` | `http://127.0.0.1:5187` | Tracking server base URL |
| `autoStartSession` | `true` | Auto-start session on supported prompt activity |
| `inactivityThresholdMinutes` | `15` | Pause after inactivity |
| `enableHeartbeat` | `true` | Send heartbeats while active |
| `heartbeatIntervalMinutes` | `5` | Heartbeat period |
| `enablePromptHashing` | `false` | Send prompt hash (length always considered) |
| `storePromptContent` | `false` | Send raw prompt content (server must allow + encrypt) |
| `showStatusBar` | `true` | Status bar item |
| `defaultProject` | `""` | Optional default project id/slug |
| `logLevel` | `info` | Extension log verbosity |

## `@track` participant

- Contribution id: `mabatar.mcp-track-tokens.track`
- Invoke in chat as `@track …`
- Records prompt activity with **length** (and optional hash) by default — not full prompt text

Use `@track` when you need reliable observability in VS Code chat. Hooks cover Cursor-native hook points; `@track` covers explicit chat participation.

## Server API usage

The extension talks to:

- `POST /api/v1/sessions/start|end|heartbeat`
- `POST /api/v1/events` (and related project helpers)
- Dashboard URL = `serverUrl` root

Requires a valid Bearer API key configured through the extension’s connection UX.

## Privacy defaults

Aligned with product defaults: no prompt content, hashing off in the extension until you enable it. See [privacy.md](privacy.md).
