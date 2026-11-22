<#
  Build Claude Usage Widget dengan csc.exe bawaan .NET Framework 4.x (tanpa Visual Studio / NuGet).
  Hasil: dist\ClaudeUsageWidget.exe + skrip.  -Package : juga buat dist\ClaudeUsageWidget-<versi>.zip untuk dibagikan.
#>
param([switch]$Package)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = $PSScriptRoot
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe .NET Framework 4.x tidak ditemukan.' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /langversion:5 `
    "/out:$dist\ClaudeUsageWidget.exe" `
    "/win32manifest:$root\app.manifest" `
    "/resource:$root\pricing.json,pricing.json" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.Net.Http.dll /r:System.Security.dll /r:System.Web.Extensions.dll `
    $src
if ($LASTEXITCODE -ne 0) { throw "Build gagal (exit $LASTEXITCODE)." }

$extra = @(
    (Join-Path $root 'statusline\cuw-statusline.sh'),
    (Join-Path $root 'statusline\cuw-statusline.ps1'),
    (Join-Path $root 'statusline\install-statusline.ps1'),
    (Join-Path $root 'install.ps1'),
    (Join-Path $root 'uninstall.ps1'),
    (Join-Path $root 'README.md')
)
foreach ($f in $extra) { Copy-Item $f $dist -Force }
$exe = Get-Item "$dist\ClaudeUsageWidget.exe"
Write-Host ("OK: {0} ({1:N0} KB)" -f $exe.FullName, ($exe.Length / 1KB))

if ($Package) {
    $ver = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe.FullName).FileVersion
    $zip = Join-Path $dist "ClaudeUsageWidget-$ver.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    # Hanya exe, skrip, README. Tidak pernah config/snapshot/log pengguna.
    $items = @($exe.FullName) + ($extra | ForEach-Object { Join-Path $dist (Split-Path $_ -Leaf) })
    Compress-Archive -Path $items -DestinationPath $zip
    Write-Host "Paket: $zip"
    # Checksum untuk catatan rilis (bukan tanda tangan penerbit; hanya memudahkan verifikasi unduhan).
    $sums = @($zip, $exe.FullName) | ForEach-Object { (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower() + '  ' + (Split-Path $_ -Leaf) }
    Set-Content -Path (Join-Path $dist 'SHA256SUMS.txt') -Value $sums -Encoding ASCII
    $sums | ForEach-Object { Write-Host "SHA-256  $_" }
}
