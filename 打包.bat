@echo off
setlocal
chcp 65001 >nul
title 剪贴记录 - 打包

rem 调用项目目录中的生成脚本，不受当前工作目录影响。
echo 正在生成 winTool.exe...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 goto failed

echo.
echo 打包完成，输出文件："%~dp0dist\winTool.exe"
pause
exit /b 0

:failed
echo.
echo 打包失败，请查看上面的错误信息。
echo 如果程序正在运行，请先从托盘退出，再重新打包。
pause
exit /b 1
