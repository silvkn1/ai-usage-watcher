<#
  Pasang Claude Usage Widget untuk user saat ini (tanpa hak admin).
  Jalankan dari folder hasil ekstrak zip:
    powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 [-Statusline] [-Autostart] [-NoLaunch] [-InstallDir <path>]

  -Statusline : pasang pembungkus statusline Claude Code (limit 5 jam/mingguan dari CLI). settings.json di-backup dulu.
  -Autostart  : jalankan widget saat Windows mulai (HKCU\...\Run).
  Data pengguna tetap di %LOCALAPPDATA%\ClaudeUsageWidget (tidak ikut zip, tidak ditimpa).
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\ClaudeUsageWidget'),
    [switch]$Statusline,
    [switch]$Autostart,
    [switch]$NoLaunch,
    [switch]$NoShortcut
)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$files = 'ClaudeUsageWidget.exe', 'cuw-statusline.sh', 'cuw-statusline.ps1', 'install-statusline.ps1', 'uninstall.ps1', 'README.md'

$rel = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
if (-not $rel -or $rel -lt 528040) { throw '.NET Framework 4.8 diperlukan (bawaan Windows 10 versi 1903 ke atas / Windows 11).' }
foreach ($f in $files) { if (-not (Test-Path (Join-Path $src $f))) { throw "File paket tidak lengkap: $f" } }

# Hentikan instance yang sedang berjalan agar exe bisa ditimpa.
Get-Process ClaudeUsageWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force $InstallDir | Out-Null
foreach ($f in $files) { Copy-Item (Join-Path $src $f) (Join-Path $InstallDir $f) -Force }
$exe = Join-Path $InstallDir 'ClaudeUsageWidget.exe'
Write-Host "Terpasang di: $InstallDir"

if (-not $NoShortcut) {
    $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Claude Usage Widget.lnk'
    $ws = New-Object -ComObject WScript.Shell
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath = $exe
    $s.WorkingDirectory = $InstallDir
    $s.Description = 'Pemakaian Claude (token, estimasi biaya, limit)'
    $s.Save()
    Write-Host "Shortcut Start Menu: $lnk"
}

# Autostart: aktifkan bila diminta, dan arahkan ulang nilai lama ke lokasi baru.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$existing = (Get-ItemProperty $runKey -Name ClaudeUsageWidget -ErrorAction SilentlyContinue).ClaudeUsageWidget
if ($Autostart -or $existing) {
    Set-ItemProperty $runKey -Name ClaudeUsageWidget -Value ('"' + $exe + '"')
    Write-Host 'Autostart aktif.'
}

if ($Statusline) { & (Join-Path $InstallDir 'install-statusline.ps1') }

if (-not $NoLaunch) { Start-Process $exe }
Write-Host 'Selesai. Agar ikon selalu terlihat: Settings > Personalization > Taskbar > Other system tray icons > aktifkan ClaudeUsageWidget.'
