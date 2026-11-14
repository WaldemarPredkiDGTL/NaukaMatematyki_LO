@echo off
cd /d "%~dp0"
chcp 1250 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Wydanie.ps1"
echo.
pause
