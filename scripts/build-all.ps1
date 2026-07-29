<#
.SYNOPSIS
  Build all MCP Track Tokens components (backend, dashboard, hooks).
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RepoRoot

function Invoke-Native {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Command,
        [string]$Label
    )
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE"
    }
}

Write-Host "==> Restore & build .NET ($Configuration)"
Invoke-Native { dotnet restore McpTrackTokens.sln } "dotnet restore"
Invoke-Native { dotnet build McpTrackTokens.sln -c $Configuration --no-restore } "dotnet build"

if (-not $SkipTests) {
    Write-Host "==> Test"
    Invoke-Native { dotnet test McpTrackTokens.sln -c $Configuration --no-build --verbosity minimal } "dotnet test"
}

Write-Host "==> Dashboard"
Invoke-Native { npm --prefix src/McpTrackTokens.Dashboard ci } "dashboard npm ci"
Invoke-Native { npm --prefix src/McpTrackTokens.Dashboard run build } "dashboard build"
$wwwroot = "src/McpTrackTokens.Server/wwwroot"
if (Test-Path $wwwroot) { Remove-Item -Recurse -Force $wwwroot }
Copy-Item -Recurse src/McpTrackTokens.Dashboard/dist $wwwroot
Write-Host "Copied dashboard → $wwwroot"

Write-Host "==> Cursor hooks"
Invoke-Native { npm --prefix integrations/cursor-hooks ci } "hooks npm ci"
Invoke-Native { npm --prefix integrations/cursor-hooks run build } "hooks build"

Write-Host "Build-all complete." -ForegroundColor Green
