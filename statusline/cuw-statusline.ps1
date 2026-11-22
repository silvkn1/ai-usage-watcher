# Claude Usage Widget - pembungkus statusline untuk PC TANPA Git Bash
# (Claude Code di Windows menjalankan statusline lewat PowerShell bila Git Bash tidak ada).
# 1) Simpan HANYA objek rate_limits (+ waktu tangkap) ke %LOCALAPPDATA%\ClaudeUsageWidget\ratelimits.json.
# 2) Teruskan input yang sama byte demi byte ke perintah statusline lama (statusline-chain.txt),
#    dan salin output-nya apa adanya. Tidak lewat pipeline string PowerShell (mencegah rusaknya UTF-8).
$ErrorActionPreference = 'SilentlyContinue'

$ms = New-Object System.IO.MemoryStream
[Console]::OpenStandardInput().CopyTo($ms)
$bytes = $ms.ToArray()
$text = [System.Text.Encoding]::UTF8.GetString($bytes)

$dir = Join-Path $env:LOCALAPPDATA 'ClaudeUsageWidget'
$flat = $text -replace "[`r`n]", ''
$m = [regex]::Match($flat, '"rate_limits":\s*\{(\s*"[a-z_]+":\s*\{[^{}]*\}\s*,?)*\s*\}')
if ($m.Success) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $tmp = Join-Path $dir 'ratelimits.json.tmp'
    $dst = Join-Path $dir 'ratelimits.json'
    [System.IO.File]::WriteAllText($tmp, ('{"captured_at":' + $now + ',' + $m.Value + '}'), (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -Force $tmp $dst
}

$chain = Join-Path $dir 'statusline-chain.txt'
if ((Test-Path $chain) -and (Get-Item $chain).Length -gt 0) {
    $cmd = [System.IO.File]::ReadAllText($chain).Trim()
    $enc = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($cmd))
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'powershell.exe'
    $psi.Arguments = "-NoProfile -NonInteractive -EncodedCommand $enc"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.CreateNoWindow = $true
    # .NET Framework menulis BOM ke stdin anak bila Console.InputEncoding punya preamble: pakai UTF-8 tanpa BOM.
    try { [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $p.StandardInput.Close()
    $out = [Console]::OpenStandardOutput()
    $p.StandardOutput.BaseStream.CopyTo($out)
    $out.Flush()
    $p.WaitForExit()
}
exit 0
