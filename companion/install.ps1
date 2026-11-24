# Installs the AI Usage Watcher companion status bar extension into VS Code and Cursor
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $scriptDir "vscode-status-bar"
$extName = "ai-usage-watcher"

$targets = @(
    (Join-Path $env:USERPROFILE ".vscode\extensions\$extName"),
    (Join-Path $env:USERPROFILE ".cursor\extensions\$extName")
)

$installedCount = 0

foreach ($dest in $targets) {
    $parentDir = Split-Path -Parent $dest
    if (Test-Path $parentDir) {
        if (-not (Test-Path $dest)) {
            New-Item -ItemType Directory -Path $dest -Force | Out-Null
        }
        Copy-Item -Path (Join-Path $sourceDir "package.json") -Destination $dest -Force
        Copy-Item -Path (Join-Path $sourceDir "extension.js") -Destination $dest -Force
        Copy-Item -Path (Join-Path $sourceDir "README.md") -Destination $dest -Force
        Write-Host "Installed companion into: $dest" -ForegroundColor Green
        $installedCount++
    }
}

if ($installedCount -eq 0) {
    # Default to .vscode\extensions if neither directory exists yet
    $defaultDest = Join-Path $env:USERPROFILE ".vscode\extensions\$extName"
    New-Item -ItemType Directory -Path $defaultDest -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceDir "package.json") -Destination $defaultDest -Force
    Copy-Item -Path (Join-Path $sourceDir "extension.js") -Destination $defaultDest -Force
    Copy-Item -Path (Join-Path $sourceDir "README.md") -Destination $defaultDest -Force
    Write-Host "Created extension folder and installed companion into: $defaultDest" -ForegroundColor Green
}

Write-Host "Done! Please reload VS Code / Cursor (Ctrl+Shift+P -> Developer: Reload Window) to activate." -ForegroundColor Cyan
