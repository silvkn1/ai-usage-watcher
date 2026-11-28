# AI Usage Watcher - pembungkus statusline untuk PC TANPA Git Bash
# (Claude Code di Windows menjalankan statusline lewat PowerShell bila Git Bash tidak ada).
# 1) Simpan HANYA objek rate_limits (+ waktu tangkap) ke %LOCALAPPDATA%\AIUsageWatcher\ratelimits.json.
# 2) Teruskan input yang sama byte demi byte ke perintah statusline lama (statusline-chain.txt),
#    dan salin output-nya apa adanya. Tidak lewat pipeline string PowerShell (mencegah rusaknya UTF-8).
$ErrorActionPreference = 'SilentlyContinue'

$ms = New-Object System.IO.MemoryStream
[Console]::OpenStandardInput().CopyTo($ms)
$bytes = $ms.ToArray()
$text = [System.Text.Encoding]::UTF8.GetString($bytes)

$dir = Join-Path $env:LOCALAPPDATA 'AIUsageWatcher'
if (-not (Test-Path $dir) -and (Test-Path (Join-Path $env:LOCALAPPDATA 'ClaudeUsageWidget'))) {
    $dir = Join-Path $env:LOCALAPPDATA 'ClaudeUsageWidget'
}

$rlFragment = $null
try {
    $parsed = $text | ConvertFrom-Json
    if ($null -ne $parsed -and $parsed.PSObject.Properties['rate_limits'] -and $null -ne $parsed.rate_limits) {
        $rlFragment = '"rate_limits":' + ($parsed.rate_limits | ConvertTo-Json -Compress -Depth 10)
    }
} catch { }
if (-not $rlFragment) {
    $flat = $text -replace "[`r`n]", ''
    $m = [regex]::Match($flat, '"rate_limits":\s*\{(\s*"[a-z_]+":\s*\{[^{}]*\}\s*,?)*\s*\}')
    if ($m.Success) { $rlFragment = $m.Value }
}

if ($rlFragment) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $tmp = Join-Path $dir ("ratelimits.json." + $PID + "." + [Guid]::NewGuid().ToString("N") + ".tmp")
    $dst = Join-Path $dir 'ratelimits.json'
    [System.IO.File]::WriteAllText($tmp, ('{"captured_at":' + $now + ',' + $rlFragment + '}'), (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -Force $tmp $dst
}

$chain = Join-Path $dir 'statusline-chain.txt'
if ((Test-Path $chain) -and (Get-Item $chain).Length -gt 0) {
    $cmd = [System.IO.File]::ReadAllText($chain).Trim()
    if ($cmd) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = Join-Path $env:SystemRoot 'System32\cmd.exe'
        $psi.Arguments = "/d /c $cmd"
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.CreateNoWindow = $true
        try { [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
        $p = [System.Diagnostics.Process]::Start($psi)
        if ($null -ne $p) {
            try {
                try {
                    $p.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
                    $p.StandardInput.Close()
                } catch { }
                $out = [Console]::OpenStandardOutput()
                $p.StandardOutput.BaseStream.CopyTo($out)
                $out.Flush()
                if (-not $p.WaitForExit(5000)) { try { $p.Kill() } catch { } }
            } finally {
                $p.Dispose()
            }
        }
    }
}
exit 0
