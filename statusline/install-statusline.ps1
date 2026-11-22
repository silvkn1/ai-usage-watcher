<#
  Memasang / melepas pembungkus statusline AI Usage Watcher.
  - Backup settings.json dulu (settings.json.bak-cuw-<waktu>).
  - Perintah statusline lama disimpan ke statusline-chain.txt dan tetap dijalankan dengan input yang sama.
  - Wrapper dipilih seperti aturan Claude Code di Windows: Git Bash bila terpasang, selain itu PowerShell.
  Pakai:  powershell -NoProfile -ExecutionPolicy Bypass -File install-statusline.ps1 [-Shell auto|bash|powershell] [-Uninstall]
#>
param(
    [ValidateSet('auto', 'bash', 'powershell')][string]$Shell = 'auto',
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'

$claudeDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$settings  = Join-Path $claudeDir 'settings.json'
$appDir    = Join-Path $env:LOCALAPPDATA 'AIUsageWatcher'
if (-not (Test-Path $appDir) -and (Test-Path (Join-Path $env:LOCALAPPDATA 'ClaudeUsageWidget'))) {
    $appDir = Join-Path $env:LOCALAPPDATA 'ClaudeUsageWidget'
}
$chainFile = Join-Path $appDir 'statusline-chain.txt'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Test-GitBash {
    if ($env:CLAUDE_CODE_GIT_BASH_PATH -and (Test-Path $env:CLAUDE_CODE_GIT_BASH_PATH)) { return $true }
    foreach ($p in @("$env:ProgramFiles\Git\bin\bash.exe", "${env:ProgramFiles(x86)}\Git\bin\bash.exe", "$env:LOCALAPPDATA\Programs\Git\bin\bash.exe")) {
        if ($p -and (Test-Path $p)) { return $true }
    }
    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($git) {
        $bash = Join-Path (Split-Path (Split-Path $git.Source)) 'bin\bash.exe'
        if (Test-Path $bash) { return $true }
    }
    return $false
}

New-Item -ItemType Directory -Force $appDir | Out-Null

if (Test-Path $settings) {
    $raw = [IO.File]::ReadAllText($settings, $utf8)
    Copy-Item $settings ("$settings.bak-cuw-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $json = $raw | ConvertFrom-Json
} else {
    New-Item -ItemType Directory -Force $claudeDir | Out-Null
    $json = New-Object PSObject
}

$bashWrapper = Join-Path $appDir 'cuw-statusline.sh'
$psWrapper   = Join-Path $appDir 'cuw-statusline.ps1'
$bashCmd = 'bash "' + ($bashWrapper -replace '\\', '/') + '"'
$psCmd   = 'powershell -NoProfile -ExecutionPolicy Bypass -File "' + ($psWrapper -replace '\\', '/') + '"'

$current = $null
if ($json.PSObject.Properties['statusLine']) { $current = $json.statusLine.command }
$isOurs = ($current -eq $bashCmd) -or ($current -eq $psCmd)

if ($Uninstall) {
    if (Test-Path $chainFile) {
        $orig = [IO.File]::ReadAllText($chainFile, $utf8).Trim()
        if ($orig) { $json.statusLine.command = $orig } elseif ($isOurs) { $json.PSObject.Properties.Remove('statusLine') }
        Remove-Item $chainFile
    } elseif ($isOurs) {
        $json.PSObject.Properties.Remove('statusLine')
    }
    Write-Host 'Pembungkus statusline dilepas.'
} else {
    if ($Shell -eq 'auto') { $Shell = if (Test-GitBash) { 'bash' } else { 'powershell' } }
    Copy-Item (Join-Path $PSScriptRoot 'cuw-statusline.sh') $bashWrapper -Force
    Copy-Item (Join-Path $PSScriptRoot 'cuw-statusline.ps1') $psWrapper -Force
    $wrapperCmd = if ($Shell -eq 'bash') { $bashCmd } else { $psCmd }

    if ($current -and -not $isOurs) {
        [IO.File]::WriteAllText($chainFile, $current, $utf8)
    } elseif (-not $current) {
        [IO.File]::WriteAllText($chainFile, '', $utf8)
    }
    if ($json.PSObject.Properties['statusLine']) {
        $json.statusLine.command = $wrapperCmd
        if (-not $json.statusLine.type) { $json.statusLine | Add-Member type 'command' }
    } else {
        $json | Add-Member statusLine ([pscustomobject]@{ type = 'command'; command = $wrapperCmd })
    }
    Write-Host "Pembungkus statusline ($Shell) dipasang. Perintah lama disimpan di: $chainFile"
}

$out = $json | ConvertTo-Json -Depth 100
$null = $out | ConvertFrom-Json   # validasi sebelum menulis
[IO.File]::WriteAllText($settings, $out, $utf8)
if (-not $Uninstall) { Write-Host 'Kirim satu pesan di Claude Code CLI agar snapshot limit terisi.' }
