<#
  Lepas Claude Usage Widget untuk user saat ini.
    powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1 [-RemoveData] [-InstallDir <path>]

  Selalu: hentikan app, lepas pembungkus statusline (kembalikan perintah lama), hapus autostart, shortcut, dan file program.
  -RemoveData : juga hapus %LOCALAPPDATA%\ClaudeUsageWidget (config, admin key terenkripsi, snapshot, log, tabel harga).
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\ClaudeUsageWidget'),
    [switch]$RemoveData
)
$ErrorActionPreference = 'Continue'
$appDir = Join-Path $env:LOCALAPPDATA 'ClaudeUsageWidget'

Get-Process ClaudeUsageWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Statusline: lepas hanya bila memang terpasang (ada chain file atau perintah wrapper kita).
$claudeDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$settings = Join-Path $claudeDir 'settings.json'
$installed = Test-Path (Join-Path $appDir 'statusline-chain.txt')
if (-not $installed -and (Test-Path $settings)) { $installed = (Get-Content $settings -Raw) -match 'cuw-statusline\.(sh|ps1)' }
$inst = Join-Path $InstallDir 'install-statusline.ps1'
if (-not (Test-Path $inst)) { $inst = Join-Path $PSScriptRoot 'install-statusline.ps1' }
if ($installed -and (Test-Path $inst)) { & $inst -Uninstall }

# Hook Claude Code milik widget (notifikasi): lepas dulu agar tidak tersisa entri yang menunjuk ke exe yang akan dihapus.
# Hanya entri milik widget yang dibuang; settings.json dicadangkan dan hasilnya diverifikasi oleh exe.
$exeForHooks = Join-Path $InstallDir 'ClaudeUsageWidget.exe'
if (Test-Path $exeForHooks) { Start-Process $exeForHooks -ArgumentList '--remove-hooks' -Wait -WindowStyle Hidden }

Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClaudeUsageWidget -ErrorAction SilentlyContinue
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Claude Usage Widget.lnk'
if (Test-Path $lnk) { Remove-Item $lnk -Force }

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "File program dihapus: $InstallDir"
}
if ($RemoveData -and (Test-Path $appDir)) {
    Remove-Item $appDir -Recurse -Force
    Write-Host "Data dihapus: $appDir"
} elseif (Test-Path $appDir) {
    Write-Host "Data dipertahankan di: $appDir (pakai -RemoveData untuk menghapus)"
}
Write-Host 'Claude Usage Widget dilepas.'
