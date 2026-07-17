#Requires -Version 5.1
<#
.SYNOPSIS
  Runs the mcp-track-tokens CLI against the Docker Compose database (shared with the dashboard).
.EXAMPLE
  .\scripts\mtt-docker.ps1 list-projects
  .\scripts\mtt-docker.ps1 register-project --name "Acme" --slug acme --repository "D:\Work\acme"
#>
param(
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]] $CliArgs
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

docker compose exec -T mcp-track-tokens /app/mcp-track-tokens @CliArgs
