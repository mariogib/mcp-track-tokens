#!/usr/bin/env bash
# Build and install MCP Track Tokens on Linux/macOS.
# Never silently modifies Cursor or VS Code settings.
set -euo pipefail

INSTALL_HOOKS=0
SKIP_TESTS=0

for arg in "$@"; do
  case "$arg" in
    --install-hooks) INSTALL_HOOKS=1 ;;
    --skip-tests) SKIP_TESTS=1 ;;
    -h|--help)
      echo "Usage: $0 [--install-hooks] [--skip-tests]"
      exit 0
      ;;
    *)
      echo "Unknown option: $arg" >&2
      exit 1
      ;;
  esac
done

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_DIR="${HOME}/.mcp-track-tokens"
BIN_DIR="${APP_DIR}/bin"
CONFIG_PATH="${APP_DIR}/install-config.json"

step() { printf '\n==> %s\n' "$*"; }

need() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Required command '$1' not found. $2" >&2
    exit 1
  }
}

step "Checking prerequisites"
need dotnet "Install .NET 8 SDK."
need node "Install Node.js 20+."
need npm "npm should ship with Node.js."

DOTNET_VER="$(dotnet --version)"
MAJOR="${DOTNET_VER%%.*}"
if [[ "${MAJOR}" -lt 8 ]]; then
  echo ".NET SDK 8+ required (found: ${DOTNET_VER})" >&2
  exit 1
fi
echo "dotnet ${DOTNET_VER}"
echo "node $(node --version)"

step "Creating application directory ${APP_DIR}"
mkdir -p "${BIN_DIR}" "${APP_DIR}/exports" "${APP_DIR}/logs" "${APP_DIR}/queue"

cd "${REPO_ROOT}"

step "Building .NET solution"
dotnet restore McpTrackTokens.sln
dotnet build McpTrackTokens.sln -c Release --no-restore
if [[ "${SKIP_TESTS}" -eq 0 ]]; then
  step "Running tests"
  dotnet test McpTrackTokens.sln -c Release --no-build --verbosity minimal
fi

step "Publishing CLI"
dotnet publish src/McpTrackTokens.Cli/McpTrackTokens.Cli.csproj -c Release -o "${BIN_DIR}" --no-build

step "Building dashboard and copying wwwroot"
npm --prefix src/McpTrackTokens.Dashboard ci
npm --prefix src/McpTrackTokens.Dashboard run build
rm -rf src/McpTrackTokens.Server/wwwroot
cp -R src/McpTrackTokens.Dashboard/dist src/McpTrackTokens.Server/wwwroot
rm -rf "${BIN_DIR}/wwwroot"
cp -R src/McpTrackTokens.Server/wwwroot "${BIN_DIR}/wwwroot"

step "Building Cursor hooks"
npm --prefix integrations/cursor-hooks ci
npm --prefix integrations/cursor-hooks run build

CLI="${BIN_DIR}/mcp-track-tokens"
if [[ ! -x "${CLI}" && -f "${BIN_DIR}/mcp-track-tokens.dll" ]]; then
  CLI="dotnet ${BIN_DIR}/mcp-track-tokens.dll"
fi

export MCP_TRACK_TOKENS_DATABASE_PATH="${APP_DIR}/mcp-track-tokens.db"
export MCP_TRACK_TOKENS_EXPORT_PATH="${APP_DIR}/exports"
export MCP_TRACK_TOKENS_LOG_PATH="${APP_DIR}/logs"
export MCP_TRACK_TOKENS_QUEUE_PATH="${APP_DIR}/queue"

step "Migrating database and creating API key"
# shellcheck disable=SC2086
${CLI} migrate
# shellcheck disable=SC2086
KEY_JSON="$(${CLI} create-api-key --name linux-install)"
echo "${KEY_JSON}"

API_KEY=""
if command -v python3 >/dev/null 2>&1; then
  API_KEY="$(printf '%s' "${KEY_JSON}" | python3 -c 'import json,sys; o=json.load(sys.stdin); print(o.get("apiKey") or o.get("ApiKey") or "")' 2>/dev/null || true)"
fi
if [[ -z "${API_KEY}" ]]; then
  API_KEY="$(printf '%s' "${KEY_JSON}" | sed -n 's/.*"apiKey"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')"
fi

cat > "${CONFIG_PATH}" <<EOF
{
  "installedAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "appDir": "${APP_DIR}",
  "binDir": "${BIN_DIR}",
  "cliPath": "${BIN_DIR}/mcp-track-tokens",
  "serverUrl": "http://127.0.0.1:5187",
  "databasePath": "${MCP_TRACK_TOKENS_DATABASE_PATH}",
  "apiKeyName": "linux-install",
  "apiKey": "${API_KEY}"
}
EOF
echo "Wrote ${CONFIG_PATH}"

if [[ "${INSTALL_HOOKS}" -eq 1 ]]; then
  step "Installing Cursor hooks scaffold"
  # shellcheck disable=SC2086
  ${CLI} install-cursor-hooks --yes
  echo "Merge ~/.cursor/mcp-track-tokens-hooks.example.json into your Cursor hooks config manually."
fi

step "MCP configuration (copy into Cursor MCP settings — not auto-applied)"
MCP_OUT="${APP_DIR}/mcp.example.json"
cat > "${MCP_OUT}" <<EOF
{
  "mcpServers": {
    "mcp-track-tokens": {
      "command": "${BIN_DIR}/mcp-track-tokens",
      "args": ["serve", "--stdio"],
      "env": {
        "MCP_TRACK_TOKENS_API_KEY": "${API_KEY:-YOUR_API_KEY}",
        "MCP_TRACK_TOKENS_DATABASE_PATH": "${APP_DIR}/mcp-track-tokens.db"
      }
    }
  }
}
EOF
cat "${MCP_OUT}"

step "Next steps"
echo "1. Start HTTP server: ${BIN_DIR}/mcp-track-tokens serve --http --migrate"
echo "2. Open dashboard: http://127.0.0.1:5187/"
echo "3. Paste MCP JSON into Cursor MCP config manually."
if [[ -n "${API_KEY}" ]]; then
  echo ""
  echo "API key: ${API_KEY}"
fi
echo "Install complete."
