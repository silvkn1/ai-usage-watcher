<#
  Pasang AI Usage Watcher untuk user saat ini (tanpa hak admin).
  Jalankan dari folder hasil ekstrak zip:
    powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 [-Statusline] [-Autostart] [-NoLaunch] [-InstallDir <path>]

  -Statusline : pasang pembungkus statusline Claude Code (limit 5 jam/mingguan dari CLI). settings.json di-backup dulu.
  -Autostart  : jalankan widget saat Windows mulai (HKCU\...\Run).
  Data pengguna tetap di %LOCALAPPDATA%\AIUsageWatcher (tidak ikut zip, tidak ditimpa).
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\AIUsageWatcher'),
    [switch]$Statusline,
    [switch]$Autostart,
    [switch]$NoLaunch,
    [switch]$NoShortcut
)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$files = 'AIUsageWatcher.exe', 'cuw-statusline.sh', 'cuw-statusline.ps1', 'install-statusline.ps1', 'uninstall.ps1', 'README.md'

$rel = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
if (-not $rel -or $rel -lt 528040) { throw '.NET Framework 4.8 diperlukan (bawaan Windows 10 versi 1903 ke atas / Windows 11).' }
foreach ($f in $files) { if (-not (Test-Path (Join-Path $src $f))) { throw "File paket tidak lengkap: $f" } }

# Hentikan instance yang sedang berjalan agar exe bisa ditimpa.
$procs = Get-Process AIUsageWatcher, ClaudeUsageWidget -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    $procs | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force $InstallDir | Out-Null
foreach ($f in $files) { Copy-Item (Join-Path $src $f) (Join-Path $InstallDir $f) -Force }
Copy-Item (Join-Path $InstallDir 'AIUsageWatcher.exe') (Join-Path $InstallDir 'ClaudeUsageWidget.exe') -Force
$compSrc = Join-Path $src 'companion'
if (Test-Path $compSrc) { Copy-Item -Path $compSrc -Destination $InstallDir -Recurse -Force }
$exe = [IO.Path]::GetFullPath((Join-Path $InstallDir 'AIUsageWatcher.exe'))
Write-Host "Terpasang di: $InstallDir"

if (-not $NoShortcut) {
    $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Usage Watcher.lnk'
    $ws = New-Object -ComObject WScript.Shell
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath = $exe
    $s.WorkingDirectory = $InstallDir
    $s.Description = 'Pemantau Pemakaian AI (Claude Code, Antigravity, Codex CLI & Ollama)'
    $s.Save()
    Write-Host "Shortcut Start Menu: $lnk"
}

# Autostart: aktifkan bila diminta, dan arahkan ulang nilai lama ke lokasi baru.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$existing = (Get-ItemProperty $runKey -Name AIUsageWatcher -ErrorAction SilentlyContinue).AIUsageWatcher
$legacy = (Get-ItemProperty $runKey -Name ClaudeUsageWidget -ErrorAction SilentlyContinue).ClaudeUsageWidget
if ($Autostart -or $existing -or $legacy) {
    if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }
    Set-ItemProperty $runKey -Name AIUsageWatcher -Value ('"' + $exe + '"')
    if ($legacy) { Remove-ItemProperty $runKey -Name ClaudeUsageWidget -ErrorAction SilentlyContinue }
    Write-Host 'Autostart aktif.'
}

if ($Statusline) { & (Join-Path $InstallDir 'install-statusline.ps1') }

if (-not $NoLaunch) { Start-Process $exe }
Write-Host 'Selesai. Agar ikon selalu terlihat: Settings > Personalization > Taskbar > Other system tray icons > aktifkan AIUsageWatcher.'
