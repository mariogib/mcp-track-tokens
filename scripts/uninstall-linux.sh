#!/usr/bin/env bash
# Uninstall local MCP Track Tokens install artifacts on Linux/macOS.
# Never modifies editor settings.
set -euo pipefail

REMOVE_DATA=0
REMOVE_HOOKS=0
YES=0

for arg in "$@"; do
  case "$arg" in
    --remove-data) REMOVE_DATA=1 ;;
    --remove-hooks) REMOVE_HOOKS=1 ;;
    -y|--yes) YES=1 ;;
    -h|--help)
      echo "Usage: $0 [--remove-hooks] [--remove-data] [-y]"
      exit 0
      ;;
  esac
done

APP_DIR="${HOME}/.mcp-track-tokens"
BIN_DIR="${APP_DIR}/bin"
HOOKS_DIR="${HOME}/.cursor/mcp-track-tokens-hooks"
HOOKS_EXAMPLE="${HOME}/.cursor/mcp-track-tokens-hooks.example.json"

confirm() {
  if [[ "${YES}" -eq 1 ]]; then return 0; fi
  read -r -p "$1 [y/N] " answer
  [[ "${answer}" =~ ^[Yy]([Ee][Ss])?$ ]]
}

echo "This removes local MCP Track Tokens install files."
echo "Editor settings / MCP JSON are never modified by this script."
confirm "Continue?" || { echo "Cancelled."; exit 0; }

if [[ -d "${BIN_DIR}" ]]; then
  rm -rf "${BIN_DIR}"
  echo "Removed ${BIN_DIR}"
fi

for f in "${APP_DIR}/install-config.json" "${APP_DIR}/mcp.example.json"; do
  if [[ -f "${f}" ]]; then
    rm -f "${f}"
    echo "Removed ${f}"
  fi
done

if [[ "${REMOVE_HOOKS}" -eq 1 ]]; then
  confirm "Remove Cursor hooks at ${HOOKS_DIR}?" || true
  if [[ -d "${HOOKS_DIR}" ]]; then rm -rf "${HOOKS_DIR}"; echo "Removed ${HOOKS_DIR}"; fi
  if [[ -f "${HOOKS_EXAMPLE}" ]]; then rm -f "${HOOKS_EXAMPLE}"; echo "Removed ${HOOKS_EXAMPLE}"; fi
  echo "If Cursor hooks config still references these scripts, edit it manually."
fi

if [[ "${REMOVE_DATA}" -eq 1 ]]; then
  confirm "DELETE all data under ${APP_DIR}?" || { echo "Skipped data removal."; exit 0; }
  rm -rf "${APP_DIR}"
  echo "Removed ${APP_DIR}"
else
  echo "Data kept at ${APP_DIR} (use --remove-data to delete)."
fi

echo "Uninstall finished."
