param([switch]$Run)
$ErrorActionPreference = 'Stop'

# 显式打包入口，输出完整 Flutter Windows 运行目录。
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
        throw '未找到 Flutter SDK，请安装 Flutter 并将 flutter/bin 加入 PATH。'
    }
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
    Write-Host '发布时请复制整个 dist\flutter 文件夹。'
    if ($Run) { Start-Process -FilePath $executablePath -WindowStyle Hidden }
}
finally { Pop-Location }
