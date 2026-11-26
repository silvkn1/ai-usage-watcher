<#
  Lepas AI Usage Watcher untuk user saat ini.
    powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1 [-RemoveData] [-InstallDir <path>]

  Selalu: hentikan app, lepas pembungkus statusline (kembalikan perintah lama), hapus autostart, shortcut, dan file program.
  -RemoveData : juga hapus data di %LOCALAPPDATA%\AIUsageWatcher dan ClaudeUsageWidget.
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\AIUsageWatcher'),
    [switch]$RemoveData
)
$ErrorActionPreference = 'Continue'
$appDir = Join-Path $env:LOCALAPPDATA 'AIUsageWatcher'
$legacyAppDir = Join-Path $env:LOCALAPPDATA 'ClaudeUsageWidget'

Get-Process AIUsageWatcher, ClaudeUsageWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Statusline: lepas hanya bila memang terpasang (ada chain file atau perintah wrapper kita).
$claudeDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$settings = Join-Path $claudeDir 'settings.json'
$installed = (Test-Path (Join-Path $appDir 'statusline-chain.txt')) -or (Test-Path (Join-Path $legacyAppDir 'statusline-chain.txt'))
if (-not $installed -and (Test-Path $settings)) { $installed = (Get-Content $settings -Raw) -match 'cuw-statusline\.(sh|ps1)' }
$inst = Join-Path $InstallDir 'install-statusline.ps1'
if (-not (Test-Path $inst)) { $inst = Join-Path $PSScriptRoot 'install-statusline.ps1' }
if ($installed -and (Test-Path $inst)) { & $inst -Uninstall }

# Hook Claude Code milik widget (notifikasi): lepas dulu agar tidak tersisa entri yang menunjuk ke exe yang akan dihapus.
# Hanya entri milik widget yang dibuang; settings.json dicadangkan dan hasilnya diverifikasi oleh exe.
$exeForHooks = Join-Path $InstallDir 'AIUsageWatcher.exe'
if (-not (Test-Path $exeForHooks)) { $exeForHooks = Join-Path $InstallDir 'ClaudeUsageWidget.exe' }
if (Test-Path $exeForHooks) { Start-Process $exeForHooks -ArgumentList '--remove-hooks' -Wait -WindowStyle Hidden }

Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name AIUsageWatcher -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClaudeUsageWidget -ErrorAction SilentlyContinue
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Usage Watcher.lnk'
if (Test-Path $lnk) { Remove-Item $lnk -Force }
$legacyLnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Claude Usage Widget.lnk'
if (Test-Path $legacyLnk) { Remove-Item $legacyLnk -Force }

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "File program dihapus: $InstallDir"
}
$legacyInstallDir = Join-Path $env:LOCALAPPDATA 'Programs\ClaudeUsageWidget'
if (Test-Path $legacyInstallDir) {
    Remove-Item $legacyInstallDir -Recurse -Force -ErrorAction SilentlyContinue
}

foreach ($extParent in @((Join-Path $env:USERPROFILE '.vscode\extensions'), (Join-Path $env:USERPROFILE '.cursor\extensions'))) {
    $extDir = Join-Path $extParent 'ai-usage-watcher'
    if (Test-Path $extDir) {
        Remove-Item $extDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Ekstensi editor dihapus: $extDir"
    }
}

if ($RemoveData) {
    if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force; Write-Host "Data dihapus: $appDir" }
    if (Test-Path $legacyAppDir) { Remove-Item $legacyAppDir -Recurse -Force; Write-Host "Data legacy dihapus: $legacyAppDir" }
} elseif ((Test-Path $appDir) -or (Test-Path $legacyAppDir)) {
    Write-Host "Data dipertahankan (pakai -RemoveData untuk menghapus)"
}
Write-Host 'AI Usage Watcher dilepas.'
