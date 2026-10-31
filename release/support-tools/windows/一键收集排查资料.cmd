@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0OhMyMods-CollectDiagnostics.ps1"
set "rc=%errorlevel%"
echo.
if "%rc%"=="2" echo Result is partial or live-best-effort. See manifest.json in the ZIP.
if not "%rc%"=="0" if not "%rc%"=="2" echo Collection failed. No usable ZIP was produced.
pause
exit /b %rc%
