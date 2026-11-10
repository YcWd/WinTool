param([switch]$Run)
$ErrorActionPreference = 'Stop'

# 显式打包入口，输出完整 Flutter Windows 运行目录。
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
        throw '未找到 Flutter SDK。请安装 Flutter stable，将其 bin 目录加入 PATH，然后重新打开打包窗口。'
    }
    Write-Host '正在打包 Flutter Windows 应用...'
    Write-Host '打包前请先从托盘退出正在运行的剪贴记录。'
    & flutter pub get
    if ($LASTEXITCODE -ne 0) { throw 'Flutter 依赖获取失败。' }
    & flutter build windows --release
    if ($LASTEXITCODE -ne 0) { throw 'Flutter Windows 构建失败。' }
    $releaseDirectory = Join-Path $PSScriptRoot 'build\windows\x64\runner\Release'
    $outputDirectory = Join-Path $PSScriptRoot 'dist\flutter'
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $releaseDirectory '*') -Destination $outputDirectory -Recurse -Force
    $executablePath = Join-Path $outputDirectory 'winTool.exe'
    Write-Host "已生成：$executablePath"
    Write-Host '发布时请复制整个 dist\flutter 文件夹，包含 DLL、资源和 WinTool.Native.exe。'
    if ($Run) { Start-Process -FilePath $executablePath -WindowStyle Hidden }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally { Pop-Location }
