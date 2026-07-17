<#
.SYNOPSIS
  Build and install MCP Track Tokens on Windows.

.DESCRIPTION
  Checks for .NET 8 and Node.js, builds backend/dashboard/hooks/extension,
  publishes the CLI to ~/.mcp-track-tokens/bin, migrates the database,
  creates an API key, and writes a local config file.

  Never silently modifies Cursor or VS Code settings. Optional switches only
  install hooks scaffold / print VSIX install commands when requested.

.PARAMETER InstallHooks
  Run `mcp-track-tokens install-cursor-hooks --yes` after build.

.PARAMETER InstallExtension
  Package the VSIX and attempt `code`/`cursor --install-extension` when available.
  Does not rewrite settings.json.
#>
[CmdletBinding()]
param(
    [switch]$InstallHooks,
    [switch]$InstallExtension,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$AppDir = Join-Path $env:USERPROFILE ".mcp-track-tokens"
$BinDir = Join-Path $AppDir "bin"
$ConfigPath = Join-Path $AppDir "install-config.json"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Command([string]$Name, [string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' not found. $Hint"
    }
}

function Get-DotNetMajor {
    $line = (& dotnet --version 2>$null)
    if (-not $line) { return 0 }
    $major = 0
    [void][int]::TryParse(($line.Split(".")[0]), [ref]$major)
    return $major
}

Write-Step "Checking prerequisites"
Assert-Command "dotnet" "Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
Assert-Command "node" "Install Node.js 20+ from https://nodejs.org/"
Assert-Command "npm" "npm should ship with Node.js."

$dotnetMajor = Get-DotNetMajor
if ($dotnetMajor -lt 8) {
    throw ".NET SDK 8+ required (found: $(dotnet --version))"
}
Write-Host "dotnet $(dotnet --version)"
Write-Host "node $(node --version)"

Write-Step "Creating application directory $AppDir"
New-Item -ItemType Directory -Force -Path $AppDir, $BinDir, (Join-Path $AppDir "exports"), (Join-Path $AppDir "logs"), (Join-Path $AppDir "queue") | Out-Null

Write-Step "Building .NET solution"
Push-Location $RepoRoot
try {
    dotnet restore McpTrackTokens.sln
    dotnet build McpTrackTokens.sln -c Release --no-restore
    if (-not $SkipTests) {
        Write-Step "Running tests"
        dotnet test McpTrackTokens.sln -c Release --no-build --verbosity minimal
    }

    Write-Step "Publishing CLI"
    dotnet publish src/McpTrackTokens.Cli/McpTrackTokens.Cli.csproj -c Release -o $BinDir --no-build

    Write-Step "Building dashboard and copying wwwroot"
    npm --prefix src/McpTrackTokens.Dashboard ci
    npm --prefix src/McpTrackTokens.Dashboard run build
    $wwwroot = Join-Path $RepoRoot "src/McpTrackTokens.Server/wwwroot"
    if (Test-Path $wwwroot) { Remove-Item -Recurse -Force $wwwroot }
    Copy-Item -Recurse (Join-Path $RepoRoot "src/McpTrackTokens.Dashboard/dist") $wwwroot
    # Also ship dashboard with published CLI (Server content)
    $pubWww = Join-Path $BinDir "wwwroot"
    if (Test-Path $pubWww) { Remove-Item -Recurse -Force $pubWww }
    Copy-Item -Recurse $wwwroot $pubWww

    Write-Step "Building Cursor hooks"
    npm --prefix integrations/cursor-hooks ci
    npm --prefix integrations/cursor-hooks run build

    Write-Step "Building VS Code extension"
    npm --prefix extensions/mcp-track-tokens-vscode ci
    npm --prefix extensions/mcp-track-tokens-vscode run build
}
finally {
    Pop-Location
}

$cli = Join-Path $BinDir "mcp-track-tokens.exe"
if (-not (Test-Path $cli)) {
    $cli = Join-Path $BinDir "mcp-track-tokens.dll"
}

Write-Step "Migrating database and creating API key"
$env:MCP_TRACK_TOKENS_DATABASE_PATH = Join-Path $AppDir "mcp-track-tokens.db"
$env:MCP_TRACK_TOKENS_EXPORT_PATH = Join-Path $AppDir "exports"
$env:MCP_TRACK_TOKENS_LOG_PATH = Join-Path $AppDir "logs"
$env:MCP_TRACK_TOKENS_QUEUE_PATH = Join-Path $AppDir "queue"

if ($cli.EndsWith(".exe")) {
    & $cli migrate
    $keyJson = & $cli create-api-key --name "windows-install" | Out-String
}
else {
    & dotnet $cli migrate
    $keyJson = & dotnet $cli create-api-key --name "windows-install" | Out-String
}

$apiKey = $null
try {
    $parsed = $keyJson | ConvertFrom-Json
    # ApiKeyCreateResultDto serializes as camelCase: apiKey
    $apiKey = $parsed.apiKey
    if (-not $apiKey) { $apiKey = $parsed.ApiKey }
}
catch {
    Write-Warning "Could not parse API key JSON. Raw output:`n$keyJson"
}

$config = [ordered]@{
    installedAt     = (Get-Date).ToUniversalTime().ToString("o")
    appDir          = $AppDir
    binDir          = $BinDir
    cliPath         = $cli
    serverUrl       = "http://127.0.0.1:5187"
    databasePath    = $env:MCP_TRACK_TOKENS_DATABASE_PATH
    apiKeyName      = "windows-install"
    apiKeyHint      = if ($apiKey) { ($apiKey.Substring(0, [Math]::Min(8, $apiKey.Length)) + "…") } else { "see create-api-key output" }
    # Full key stored locally for operator convenience; protect this file.
    apiKey          = $apiKey
}
$config | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $ConfigPath
Write-Host "Wrote $ConfigPath"

if ($InstallHooks) {
    Write-Step "Installing Cursor hooks scaffold"
    if ($cli.EndsWith(".exe")) {
        & $cli install-cursor-hooks --yes
    }
    else {
        & dotnet $cli install-cursor-hooks --yes
    }
    Write-Host "Merge ~/.cursor/mcp-track-tokens-hooks.example.json into your Cursor hooks config manually."
}

$vsix = Join-Path $RepoRoot "extensions/mcp-track-tokens-vscode/mcp-track-tokens-0.1.0.vsix"
if ($InstallExtension) {
    Write-Step "Packaging and installing VS Code extension"
    Push-Location (Join-Path $RepoRoot "extensions/mcp-track-tokens-vscode")
    try {
        npm run package
    }
    finally {
        Pop-Location
    }
    if (-not (Test-Path $vsix)) {
        $vsix = Get-ChildItem (Join-Path $RepoRoot "extensions/mcp-track-tokens-vscode") -Filter "*.vsix" | Select-Object -First 1 -ExpandProperty FullName
    }
    $installed = $false
    foreach ($editor in @("cursor", "code")) {
        if (Get-Command $editor -ErrorAction SilentlyContinue) {
            & $editor --install-extension $vsix
            $installed = $true
            Write-Host "Installed VSIX via $editor"
            break
        }
    }
    if (-not $installed) {
        Write-Host "VSIX built at: $vsix"
        Write-Host "Install manually: code --install-extension `"$vsix`""
    }
    Write-Host "Extension settings were NOT modified. Set mcpTrackTokens.serverUrl if needed."
}

Write-Step "MCP configuration (copy into Cursor MCP settings)"
$username = $env:USERNAME
$mcpSnippet = @"
{
  "mcpServers": {
    "mcp-track-tokens": {
      "command": "C:\\Users\\$username\\.mcp-track-tokens\\bin\\mcp-track-tokens.exe",
      "args": ["serve", "--stdio"],
      "env": {
        "MCP_TRACK_TOKENS_API_KEY": "$(if ($apiKey) { $apiKey } else { "YOUR_API_KEY" })",
        "MCP_TRACK_TOKENS_DATABASE_PATH": "C:\\Users\\$username\\.mcp-track-tokens\\mcp-track-tokens.db"
      }
    }
  }
}
"@
Write-Host $mcpSnippet
$mcpOut = Join-Path $AppDir "mcp.example.json"
$mcpSnippet | Set-Content -Encoding UTF8 $mcpOut
Write-Host "Also wrote $mcpOut (not applied to editor settings)."

Write-Step "Next steps"
Write-Host "1. Start HTTP server:"
Write-Host "   & `"$cli`" serve --http --migrate"
Write-Host "2. Open dashboard: http://127.0.0.1:5187/"
Write-Host "3. Paste the MCP snippet into Cursor MCP config (manual)."
Write-Host "4. Set env MCP_TRACK_TOKENS_API_KEY for hooks."
if ($apiKey) {
    Write-Host ""
    Write-Host "API key (shown once here; also in $ConfigPath):" -ForegroundColor Yellow
    Write-Host $apiKey
}
Write-Host ""
Write-Host "Install complete." -ForegroundColor Green
