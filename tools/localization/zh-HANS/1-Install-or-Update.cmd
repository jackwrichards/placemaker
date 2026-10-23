@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-Localization.ps1" -Action Install %*
set "result=%ERRORLEVEL%"
pause
exit /b %result%
