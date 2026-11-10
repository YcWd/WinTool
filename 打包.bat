@echo off
setlocal
chcp 65001 >nul
title WinTool - Flutter Windows Build

rem Keep this launcher ASCII-only; localized messages are in build.ps1.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
set "buildExitCode=%errorlevel%"

echo.
if not "%buildExitCode%"=="0" echo Build failed. See the error above.
pause
exit /b %buildExitCode%
