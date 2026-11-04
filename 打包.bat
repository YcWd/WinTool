@echo off
setlocal
chcp 65001 >nul
title 剪贴记录 - Flutter Windows 打包

rem 打包前检查 Flutter 命令，缺少 SDK 时给出明确提示。
where.exe /q flutter
if errorlevel 1 goto missing_flutter

rem 调用项目目录中的生成脚本，不受当前工作目录影响。
echo 正在打包 Flutter Windows 应用...
echo 打包前请先从托盘退出正在运行的剪贴记录。
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 goto failed

echo.
echo 打包完成。
echo 输出目录："%~dp0dist\flutter"
echo 主程序："%~dp0dist\flutter\winTool.exe"
echo 发布时请复制整个输出目录，包含 DLL、资源和 WinTool.Native.exe。
pause
exit /b 0

:missing_flutter
echo 未找到 Flutter SDK。
echo 请安装 Flutter stable，并将 Flutter SDK 的 bin 目录加入 PATH。
echo 配置完成后重新打开此 BAT 文件。
goto failed

:failed
echo.
echo 打包失败，请查看上面的错误信息。
echo 如果程序正在运行，请先从托盘退出，再重新打包。
pause
exit /b 1
