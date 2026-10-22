param([switch]$Run)

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw '未找到 .NET Framework 4.x 编译器，请启用 Windows 的 .NET Framework 4.x 功能。'
}

$outputDirectory = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$executablePath = Join-Path $outputDirectory 'winTool.exe'
# 最后一位作为打包序号，仅在编译成功后保存到版本文件。
$versionPath = Join-Path $PSScriptRoot 'version.txt'
$currentVersion = [version](Get-Content -LiteralPath $versionPath -Raw).Trim()
$nextVersion = '{0}.{1}.{2}.{3}' -f $currentVersion.Major, $currentVersion.Minor, $currentVersion.Build, ($currentVersion.Revision + 1)
$metadataDirectory = Join-Path $outputDirectory 'build'
New-Item -ItemType Directory -Path $metadataDirectory -Force | Out-Null
$assemblyInfoPath = Join-Path $metadataDirectory 'AssemblyInfo.cs'
$manifestPath = Join-Path $metadataDirectory 'app.manifest'
$assemblyInfo = @"
[assembly: System.Reflection.AssemblyVersion("$nextVersion")]
[assembly: System.Reflection.AssemblyFileVersion("$nextVersion")]
[assembly: System.Reflection.AssemblyInformationalVersion("$nextVersion")]
"@
[System.IO.File]::WriteAllText($assemblyInfoPath, $assemblyInfo, [System.Text.Encoding]::UTF8)
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'app.manifest') -Raw
$manifest.assembly.assemblyIdentity.SetAttribute('version', $nextVersion)
$manifest.Save($manifestPath)
$sources = @('Program.cs', 'NativeMethods.cs', 'HistoryStore.cs', 'AppSettings.cs', 'TranslationSettings.cs', 'DeepSeekTranslator.cs', 'TranslationForm.cs', 'FluentStyle.cs', 'FluentButton.cs', 'SoftControls.cs', 'ThumbnailCache.cs', 'TrayMenu.cs', 'RecordMenu.cs', 'OverlayListBox.cs', 'SettingsForm.cs', 'MainForm.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$sources += $assemblyInfoPath

# 使用 Windows 自带编译器生成可独立复制的桌面 EXE。
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$executablePath" "/win32manifest:$manifestPath" `
    "/win32icon:$(Join-Path $PSScriptRoot 'assets\clipboard.ico')" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll `
    /reference:System.Net.Http.dll /reference:System.Security.dll $sources
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码：$LASTEXITCODE" }
[System.IO.File]::WriteAllText($versionPath, $nextVersion + [Environment]::NewLine, [System.Text.Encoding]::UTF8)
Write-Host "版本：$nextVersion"
Write-Host "已生成：$executablePath"
if ($Run) { Start-Process -FilePath $executablePath -WindowStyle Hidden }
