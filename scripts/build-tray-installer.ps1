#Requires -Version 7.0
<#
.SYNOPSIS
  Publishes tray + desktop, stages Cursor hooks, and builds the WiX MSI.

.EXAMPLE
  pwsh ./scripts/build-tray-installer.ps1
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

$publishDir = Join-Path $root 'artifacts\tray-publish'
$desktopPublishDir = Join-Path $root 'artifacts\desktop-publish'
$integrationsContentDir = Join-Path $root 'artifacts\integrations-content'
$integrationsHelperDir = Join-Path $root 'artifacts\integrations-helper'
$setupProject = Join-Path $root 'setup\McpTrackTokens.Tray.Setup\McpTrackTokens.Tray.Setup.wixproj'
$helperProject = Join-Path $root 'setup\McpTrackTokens.Setup.Integrations\McpTrackTokens.Setup.Integrations.csproj'
$dashboardDir = Join-Path $root 'src\McpTrackTokens.Dashboard'
$hooksDir = Join-Path $root 'integrations\cursor-hooks'
$trayProject = Join-Path $root 'src\McpTrackTokens.Tray\McpTrackTokens.Tray.csproj'
$desktopProject = Join-Path $root 'src\McpTrackTokens.Desktop\McpTrackTokens.Desktop.csproj'
$msiOut = Join-Path $root 'artifacts\installer'

function Invoke-AppPublish {
    param(
        [Parameter(Mandatory)]
        [string]$Project,

        [Parameter(Mandatory)]
        [string]$Output,

        [Parameter(Mandatory)]
        [string]$Label,

        [switch]$SingleFile
    )

    Write-Host "==> Publishing $Label ($Configuration / $Runtime)"
    if (Test-Path $Output) {
        Remove-Item -Recurse -Force $Output
    }

    $publishArgs = @(
        'publish', $Project,
        '-c', $Configuration,
        '-r', $Runtime,
        '-o', $Output,
        '/p:IncludeNativeLibrariesForSelfExtract=true'
    )

    if ($SingleFile) {
        $publishArgs += '/p:PublishSingleFile=true'
    }
    else {
        $publishArgs += '/p:PublishSingleFile=false'
    }

    if ($FrameworkDependent) {
        $publishArgs += '/p:SelfContained=false'
    }
    else {
        $publishArgs += '/p:SelfContained=true'
    }

    dotnet @publishArgs

    Get-ChildItem -Path $Output -Recurse -Filter '*.pdb' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host "==> Building dashboard wwwroot"
Push-Location $dashboardDir
try {
    if (-not (Test-Path 'node_modules')) {
        npm ci
    }
    npm run build
}
finally {
    Pop-Location
}

Write-Host "==> Building Cursor hooks"
Push-Location $hooksDir
try {
    if (-not (Test-Path 'node_modules')) {
        npm ci
    }
    npm run build
}
finally {
    Pop-Location
}

Write-Host "==> Staging integrations content"
if (Test-Path $integrationsContentDir) {
    Remove-Item -Recurse -Force $integrationsContentDir
}
$hooksOut = Join-Path $integrationsContentDir 'cursor-hooks'
New-Item -ItemType Directory -Force -Path $hooksOut | Out-Null
Copy-Item -Recurse -Force (Join-Path $hooksDir 'dist') (Join-Path $hooksOut 'dist')
foreach ($name in @('package.json', 'README.md', 'example-hooks-config.json')) {
    $src = Join-Path $hooksDir $name
    if (Test-Path $src) {
        Copy-Item -Force $src (Join-Path $hooksOut $name)
    }
}

$mcpHttpSample = Join-Path $root 'samples\cursor-config\mcp.http.json'
if (Test-Path $mcpHttpSample) {
    # Bundled for post-install helper + user reference (HTTP MCP against the tray host).
    $sampleText = Get-Content -Raw $mcpHttpSample
    $sampleText = $sampleText -replace 'YOUR_API_KEY', 'OverTheMoon'
    Set-Content -Path (Join-Path $integrationsContentDir 'mcp.http.json') -Value $sampleText -NoNewline
}

Invoke-AppPublish -Project $trayProject -Output $publishDir -Label 'tray'
Invoke-AppPublish -Project $desktopProject -Output $desktopPublishDir -Label 'desktop'
Invoke-AppPublish -Project $helperProject -Output $integrationsHelperDir -Label 'integrations helper' -SingleFile

if (-not (Test-Path (Join-Path $publishDir 'mcp-track-tokens-tray.exe'))) {
    throw "Tray publish failed — mcp-track-tokens-tray.exe not found in $publishDir"
}

$serverDll = Join-Path $publishDir 'McpTrackTokens.Server.dll'
if (-not (Test-Path $serverDll)) {
    throw "McpTrackTokens.Server.dll missing from tray publish ($publishDir). API/MCP host must ship in the MSI."
}

$wwwrootIndex = Join-Path $publishDir 'wwwroot\index.html'
if (-not (Test-Path $wwwrootIndex)) {
    throw "Dashboard wwwroot missing from publish output. Ensure the dashboard build succeeded."
}

$trayAppSettings = Join-Path $publishDir 'appsettings.json'
if (-not (Test-Path $trayAppSettings)) {
    throw "Tray appsettings.json missing from $publishDir"
}
$appSettingsText = Get-Content -Raw -Path $trayAppSettings
if ($appSettingsText -notmatch '5187') {
    throw "Tray appsettings.json must bind/serve on port 5187 (ServerUrl/BindAddress)."
}
if ($appSettingsText -notmatch '"ServerUrl"\s*:\s*"http://127\.0\.0\.1:5187"' -and
    $appSettingsText -notmatch '"BindAddress"\s*:\s*"http://127\.0\.0\.1:5187"') {
    throw "Tray appsettings.json must include ServerUrl or BindAddress http://127.0.0.1:5187 for MSI deployment."
}

if (-not (Test-Path (Join-Path $desktopPublishDir 'mcp-track-tokens-desktop.exe'))) {
    throw "Desktop publish failed — mcp-track-tokens-desktop.exe not found in $desktopPublishDir"
}

if (-not (Test-Path (Join-Path $integrationsHelperDir 'mcp-track-tokens-setup-integrations.exe'))) {
    throw "Integrations helper publish failed — exe not found in $integrationsHelperDir"
}

if (-not (Test-Path (Join-Path $integrationsContentDir 'cursor-hooks\dist'))) {
    throw "Hooks content missing under $integrationsContentDir"
}

Write-Host "==> Building WiX MSI"
New-Item -ItemType Directory -Force -Path $msiOut | Out-Null
dotnet build $setupProject -c $Configuration `
    -p:PublishDir="$publishDir\" `
    -p:DesktopPublishDir="$desktopPublishDir\" `
    -p:IntegrationsContentDir="$integrationsContentDir\" `
    -p:IntegrationsHelperDir="$integrationsHelperDir\"
if ($LASTEXITCODE -ne 0) {
    throw "WiX MSI build failed with exit code $LASTEXITCODE"
}
$builtMsi = Get-ChildItem -Path (Join-Path $root 'setup\McpTrackTokens.Tray.Setup\bin') -Recurse -Filter '*.msi' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $builtMsi) {
    throw 'MSI was not produced. Ensure the WiX .NET SDK packages restored successfully.'
}

$targetMsi = Join-Path $msiOut $builtMsi.Name
Copy-Item -Force $builtMsi.FullName $targetMsi

Write-Host ""
Write-Host "Installer ready:"
Write-Host "  $targetMsi"
Write-Host ""
Write-Host "Install with:"
Write-Host "  msiexec /i `"$targetMsi`""
