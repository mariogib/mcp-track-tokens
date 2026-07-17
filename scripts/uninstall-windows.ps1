<#
.SYNOPSIS
  Uninstall MCP Track Tokens local install artifacts on Windows.

.DESCRIPTION
  Removes published binaries under ~/.mcp-track-tokens/bin and optional hooks
  scaffold. Does not modify editor settings. Database deletion requires -RemoveData.
#>
[CmdletBinding()]
param(
    [switch]$RemoveData,
    [switch]$RemoveHooks,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
$AppDir = Join-Path $env:USERPROFILE ".mcp-track-tokens"
$BinDir = Join-Path $AppDir "bin"
$HooksDir = Join-Path $env:USERPROFILE ".cursor\mcp-track-tokens-hooks"
$HooksExample = Join-Path $env:USERPROFILE ".cursor\mcp-track-tokens-hooks.example.json"

function Confirm-OrExit([string]$Message) {
    if ($Yes) { return }
    $answer = Read-Host "$Message [y/N]"
    if ($answer -notmatch '^(y|yes)$') {
        Write-Host "Cancelled."
        exit 0
    }
}

Write-Host "This removes local MCP Track Tokens install files."
Write-Host "Editor settings / MCP JSON are never modified by this script."
Confirm-OrExit "Continue?"

if (Test-Path $BinDir) {
    Remove-Item -Recurse -Force $BinDir
    Write-Host "Removed $BinDir"
}

$config = Join-Path $AppDir "install-config.json"
$mcpExample = Join-Path $AppDir "mcp.example.json"
foreach ($f in @($config, $mcpExample)) {
    if (Test-Path $f) {
        Remove-Item -Force $f
        Write-Host "Removed $f"
    }
}

if ($RemoveHooks) {
    Confirm-OrExit "Remove Cursor hooks at $HooksDir?"
    if (Test-Path $HooksDir) { Remove-Item -Recurse -Force $HooksDir; Write-Host "Removed $HooksDir" }
    if (Test-Path $HooksExample) { Remove-Item -Force $HooksExample; Write-Host "Removed $HooksExample" }
    Write-Host "If Cursor hooks.json still references these scripts, edit it manually."
}

if ($RemoveData) {
    Confirm-OrExit "DELETE all data under $AppDir (database, logs, exports, keys)?"
    if (Test-Path $AppDir) {
        Remove-Item -Recurse -Force $AppDir
        Write-Host "Removed $AppDir"
    }
}
else {
    Write-Host "Data kept at $AppDir (use -RemoveData to delete)."
}

Write-Host "To remove the VS Code/Cursor extension, uninstall it from the editor UI or:"
Write-Host "  code --uninstall-extension mabatar.mcp-track-tokens"
Write-Host "Uninstall finished."
