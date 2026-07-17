#!/usr/bin/env bash
# Build all MCP Track Tokens components.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${REPO_ROOT}"

PACK_EXTENSION=0
SKIP_TESTS=0
CONFIGURATION=Release

for arg in "$@"; do
  case "$arg" in
    --pack-extension) PACK_EXTENSION=1 ;;
    --skip-tests) SKIP_TESTS=1 ;;
    --debug) CONFIGURATION=Debug ;;
    -h|--help)
      echo "Usage: $0 [--pack-extension] [--skip-tests] [--debug]"
      exit 0
      ;;
  esac
done

echo "==> Restore & build .NET (${CONFIGURATION})"
dotnet restore McpTrackTokens.sln
dotnet build McpTrackTokens.sln -c "${CONFIGURATION}" --no-restore

if [[ "${SKIP_TESTS}" -eq 0 ]]; then
  echo "==> Test"
  dotnet test McpTrackTokens.sln -c "${CONFIGURATION}" --no-build --verbosity minimal
fi

echo "==> Dashboard"
npm --prefix src/McpTrackTokens.Dashboard ci
npm --prefix src/McpTrackTokens.Dashboard run build
rm -rf src/McpTrackTokens.Server/wwwroot
cp -R src/McpTrackTokens.Dashboard/dist src/McpTrackTokens.Server/wwwroot
echo "Copied dashboard → src/McpTrackTokens.Server/wwwroot"

echo "==> Cursor hooks"
npm --prefix integrations/cursor-hooks ci
npm --prefix integrations/cursor-hooks run build

echo "==> VS Code extension"
npm --prefix extensions/mcp-track-tokens-vscode ci
npm --prefix extensions/mcp-track-tokens-vscode run build
if [[ "${PACK_EXTENSION}" -eq 1 ]]; then
  npm --prefix extensions/mcp-track-tokens-vscode run package
fi

echo "Build-all complete."
