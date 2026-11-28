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

$procs = Get-Process AIUsageWatcher, ClaudeUsageWidget -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    $procs | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
}

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

foreach ($extEntry in @(
    @{ Root = (Join-Path $env:USERPROFILE '.vscode\extensions'); Cli = @((Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\bin\code.cmd'), (Join-Path $env:ProgramFiles 'Microsoft VS Code\bin\code.cmd')) },
    @{ Root = (Join-Path $env:USERPROFILE '.cursor\extensions'); Cli = @((Join-Path $env:LOCALAPPDATA 'Programs\cursor\resources\app\bin\cursor.cmd')) },
    @{ Root = (Join-Path $env:USERPROFILE '.antigravity\extensions'); Cli = @((Join-Path $env:LOCALAPPDATA 'Programs\Antigravity\bin\antigravity.cmd')) },
    @{ Root = (Join-Path $env:USERPROFILE '.windsurf\extensions'); Cli = @((Join-Path $env:LOCALAPPDATA 'Programs\Windsurf\bin\windsurf.cmd')) })) {
    $extParent = $extEntry.Root
    $cli = $extEntry.Cli | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($cli) {
        try { & $cli --uninstall-extension silvkn1.ai-usage-watcher 2>$null | Out-Null } catch {}
    }
    if (Test-Path $extParent) {
        foreach ($cand in @(Get-ChildItem $extParent -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'ai-usage-watcher' -or $_.Name -like 'silvkn1.ai-usage-watcher-*' })) {
            Remove-Item $cand.FullName -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "Ekstensi editor dihapus: $($cand.FullName)"
        }
        $extJson = Join-Path $extParent 'extensions.json'
        if (Test-Path $extJson) {
            try {
                $arr = Get-Content $extJson -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($arr -is [System.Array]) {
                    $filtered = @($arr | Where-Object {
                        $idVal = if ($_.identifier) { $_.identifier.id } else { $null }
                        $idVal -ne 'silvkn1.ai-usage-watcher' -and $_.relativeLocation -ne 'ai-usage-watcher' -and $_.relativeLocation -notlike 'silvkn1.ai-usage-watcher-*'
                    })
                    if ($filtered.Count -ne $arr.Count) {
                        $outJson = ConvertTo-Json -InputObject $filtered -Depth 10 -Compress
                        if (-not $outJson.StartsWith('[')) { $outJson = '[' + $outJson + ']' }
                        [IO.File]::WriteAllText($extJson, $outJson, [Text.UTF8Encoding]::new($false))
                    }
                }
            } catch {}
        }
    }
}

if ($RemoveData) {
    if (Test-Path $appDir) {
        Remove-Item $appDir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $appDir) { Write-Warning "Sebagian data masih terkunci di: $appDir" }
        else { Write-Host "Data dihapus: $appDir" }
    }
    if (Test-Path $legacyAppDir) {
        Remove-Item $legacyAppDir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $legacyAppDir) { Write-Warning "Sebagian data legacy masih terkunci di: $legacyAppDir" }
        else { Write-Host "Data legacy dihapus: $legacyAppDir" }
    }
} elseif ((Test-Path $appDir) -or (Test-Path $legacyAppDir)) {
    Write-Host "Data dipertahankan (pakai -RemoveData untuk menghapus)"
}
Write-Host 'AI Usage Watcher dilepas.'
