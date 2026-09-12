@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pack-il2cpp.ps1" %*
exit /b %ERRORLEVEL%
