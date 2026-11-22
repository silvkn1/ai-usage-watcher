# Test runner script for Claude Usage Widget
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $Csc)) {
    $Csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $Csc)) {
    throw "csc.exe tidak ditemukan di .NET Framework 4.x"
}

$RootDir = $PSScriptRoot
$TestExe = Join-Path $RootDir "dist\Tests.exe"
$SrcFiles = Get-ChildItem (Join-Path $RootDir "src\*.cs") | ForEach-Object { $_.FullName }
$TestFiles = Get-ChildItem (Join-Path $RootDir "tests\*.cs") | ForEach-Object { $_.FullName }
$Pricing = Join-Path $RootDir "pricing.json"

if (-not (Test-Path (Join-Path $RootDir "dist"))) {
    New-Item -ItemType Directory -Path (Join-Path $RootDir "dist") | Out-Null
}

$Refs = "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", `
        "System.Net.Http.dll", "System.Security.dll", "System.Web.Extensions.dll"
$RefArgs = $Refs -join ","

Write-Host "Compiling unit tests..." -ForegroundColor Cyan
& $Csc /nologo /target:exe /main:Tests.TestRunner /out:$TestExe /resource:$Pricing /r:$RefArgs $TestFiles $SrcFiles

if ($LASTEXITCODE -ne 0) {
    throw "Kompilasi unit test gagal (exit $LASTEXITCODE)."
}

Write-Host "Running unit tests..." -ForegroundColor Cyan
Write-Host ""
& $TestExe

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
