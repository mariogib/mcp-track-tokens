# Windows MSI deployment

On Windows, the **primary** way to deploy MCP Track Tokens is the MSI installer. It installs and starts the full local stack in one process:

| Surface | How it is provided |
| --- | --- |
| HTTP API | `http://127.0.0.1:5187/api/v1` |
| HTTP MCP | `http://127.0.0.1:5187/mcp` |
| Dashboard | Static `wwwroot` served by the same host |
| Desktop shell | WebView2 app that opens the dashboard URL |
| Optional integrations | Cursor hooks + VS Code/Cursor VSIX (setup options) |

You do **not** need Docker for normal Windows desktop use.

## Build the installer

```powershell
pwsh ./scripts/build-tray-installer.ps1
```

Output:

`artifacts/installer/MCP-Track-Tokens-Setup.msi`

The script publishes the tray host (embeds `McpTrackTokens.Server` + dashboard `wwwroot`), the desktop shell, Cursor hooks, the extension VSIX, and the post-install helper, then builds the WiX MSI. It fails the build if `McpTrackTokens.Server.dll`, `wwwroot/index.html`, or the `5187` bind/server URL is missing from the tray publish output.

## Install

```powershell
msiexec /i "artifacts\installer\MCP-Track-Tokens-Setup.msi"
```

Typical options (defaults are ON):

- Start MCP Track Tokens when Windows starts
- Install Cursor hooks
- Install VS Code / Cursor extension
- **Upgrade / keep existing SQLite database** (recommended) — leaves `%USERPROFILE%\.mcp-track-tokens\` intact when upgrading or reinstalling. Uncheck only for a clean database reset.
- Start MCP Track Tokens now (exit dialog)

Install layout (under `Program Files\MCP Track Tokens\`):

- Tray host: `mcp-track-tokens-tray.exe` (API + MCP + dashboard)
- Desktop: `Desktop\mcp-track-tokens-desktop.exe`
- Integrations: `integrations\` (hooks, VSIX, HTTP MCP sample)

## Upgrades

Running a newer MSI with the same `UpgradeCode` performs a major upgrade: Program Files binaries are replaced. After accepting the license you will see **Setup options**, including **Keep existing SQLite database** (on by default). Tracking data under `%USERPROFILE%\.mcp-track-tokens\` is kept unless you uncheck that option. Uninstall also leaves that folder in place.

Wizard path on upgrade: Welcome → License → **Setup options** → Ready to install (install folder is not re-prompted).
## After install

1. Confirm the tray icon is running.
2. Open `http://127.0.0.1:5187/health` — expect healthy.
3. Open the dashboard from the tray (**Open dashboard**) or Start Menu desktop app.
4. Configure Cursor MCP against `http://127.0.0.1:5187/mcp` using the sample from the post-install helper / `WINDOWS-HOST.txt` (default API key `OverTheMoon` unless you change it under Settings).
5. Merge Cursor hooks from `%USERPROFILE%\.cursor\mcp-track-tokens-hooks.example.json` into your Cursor hooks config.

SQLite and local data live under `%USERPROFILE%\.mcp-track-tokens\` (not inside Program Files).

## Docker (optional)

[`docker-compose.yml`](../docker-compose.yml) is an **alternate** path for containerized development or shared machines. MSI users should not need Compose for API, MCP, or the dashboard.

## Related

- [Architecture](architecture.md) — hosting modes
- [Cursor hooks](cursor-hooks.md)
- Dashboard **Help → Windows setup** for the in-app walkthrough
