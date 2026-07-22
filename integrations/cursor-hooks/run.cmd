@echo off
setlocal
REM Capture script home BEFORE shift — shift reassigns %0 and breaks %~dp0.
set "HOOKS_HOME=%~dp0"
if exist "%USERPROFILE%\.mcp-track-tokens\hooks.env.cmd" call "%USERPROFILE%\.mcp-track-tokens\hooks.env.cmd"
set "SCRIPT=%~1"
if "%SCRIPT%"=="" (
  echo Usage: run.cmd ^<entrypoint^> [args...]
  echo Example: run.cmd healthcheck
  echo Example: run.cmd prompt-submitted --allow-prompt
  exit /b 1
)
shift
node "%HOOKS_HOME%dist\%SCRIPT%.js" %*
set "EXITCODE=%ERRORLEVEL%"
if /I "%SCRIPT%"=="prompt-submitted" if /I "%~1"=="--allow-prompt" (
  echo {"continue":true}
)
exit /b %EXITCODE%
