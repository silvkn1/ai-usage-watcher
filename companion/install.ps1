# Installs the AI Usage Watcher companion status bar extension into VS Code, Cursor, Antigravity & Windsurf
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $scriptDir "vscode-status-bar"
$pkgPath = Join-Path $sourceDir "package.json"
$extPath = Join-Path $sourceDir "extension.js"
$readmePath = Join-Path $sourceDir "README.md"
$iconPath = Join-Path $sourceDir "icon.png"
$pkg = Get-Content -Raw -Encoding UTF8 $pkgPath | ConvertFrom-Json
$extId = "$($pkg.publisher).$($pkg.name)"
$folderName = "$extId-$($pkg.version)"

function Register-ExtensionJson([string]$extRoot, [string]$extIdStr, [string]$folderStr, [string]$verStr) {
    $ej = Join-Path $extRoot "extensions.json"
    $list = @()
    if (Test-Path $ej) {
        try {
            $parsed = Get-Content $ej -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($parsed -is [System.Array]) { $list = @($parsed) }
            elseif ($null -ne $parsed) { $list = @($parsed) }
        } catch { $list = @() }
    }
    $list = @($list | Where-Object {
        $idVal = if ($_.identifier) { $_.identifier.id } else { $null }
        $idVal -ne $extIdStr -and $_.relativeLocation -ne $folderStr -and $_.relativeLocation -notlike ($extIdStr + "-*")
    })
    $extDirFull = Join-Path $extRoot $folderStr
    $entry = [ordered]@{
        identifier = [ordered]@{ id = $extIdStr }
        version = $verStr
        location = [ordered]@{ '$mid' = 1; path = ($extDirFull -replace '\\','/'); scheme = "file" }
        relativeLocation = $folderStr
        metadata = [ordered]@{ installedTimestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); source = "vsix" }
    }
    $list = @($list) + @($entry)
    $outJson = ConvertTo-Json -InputObject $list -Depth 10 -Compress
    if (-not $outJson.StartsWith("[")) { $outJson = "[" + $outJson + "]" }
    [IO.File]::WriteAllText($ej, $outJson, [Text.UTF8Encoding]::new($false))
}

# Build temporary .vsix so IDE CLI registers the extension natively in extensions.json and active profiles
$vsixPath = Join-Path ([IO.Path]::GetTempPath()) ("ai-usage-watcher-" + [Guid]::NewGuid().ToString("N") + ".vsix")
try {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $ct = '<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="vsixmanifest" ContentType="text/xml" /><Default Extension="json" ContentType="application/json" /><Default Extension="js" ContentType="text/javascript" /><Default Extension="md" ContentType="text/markdown" /><Default Extension="png" ContentType="image/png" /></Types>'
    $iconXml = if (Test-Path $iconPath) { '<Icon>extension/icon.png</Icon>' } else { '' }
    $readmeAsset = if (Test-Path $readmePath) { '<Asset Type="Microsoft.VisualStudio.Services.Content.Details" Path="extension/README.md" Addressable="true" />' } else { '' }
    $iconAsset = if (Test-Path $iconPath) { '<Asset Type="Microsoft.VisualStudio.Services.Icons.Default" Path="extension/icon.png" Addressable="true" />' } else { '' }
    $vm = '<?xml version="1.0" encoding="utf-8"?><PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Language="en-US" Id="' + [Security.SecurityElement]::Escape($pkg.name) + '" Version="' + [Security.SecurityElement]::Escape($pkg.version) + '" Publisher="' + [Security.SecurityElement]::Escape($pkg.publisher) + '" /><DisplayName>' + [Security.SecurityElement]::Escape($pkg.displayName) + '</DisplayName><Description xml:space="preserve">' + [Security.SecurityElement]::Escape($pkg.description) + '</Description><Categories>Other</Categories>' + $iconXml + '</Metadata><Installation><InstallationTarget Id="Microsoft.VisualStudio.Code" /></Installation><Dependencies /><Assets><Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" />' + $readmeAsset + $iconAsset + '</Assets></PackageManifest>'

    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    $zip = [IO.Compression.ZipFile]::Open($vsixPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entries = @(
            @('[Content_Types].xml', $utf8NoBom.GetBytes($ct)),
            @('extension.vsixmanifest', $utf8NoBom.GetBytes($vm)),
            @('extension/package.json', [IO.File]::ReadAllBytes($pkgPath)),
            @('extension/extension.js', [IO.File]::ReadAllBytes($extPath))
        )
        if (Test-Path $readmePath) { $entries += ,@('extension/README.md', [IO.File]::ReadAllBytes($readmePath)) }
        if (Test-Path $iconPath)   { $entries += ,@('extension/icon.png', [IO.File]::ReadAllBytes($iconPath)) }
        foreach ($pair in $entries) {
            $e = $zip.CreateEntry($pair[0])
            $s = $e.Open()
            try { $s.Write($pair[1], 0, $pair[1].Length) } finally { $s.Dispose() }
        }
    } finally { $zip.Dispose() }

    $codeOnPath = Get-Command code.cmd, code -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    $pf86 = ${env:ProgramFiles(x86)}
    $roots = @(
        @{ Root = (Join-Path $env:USERPROFILE ".vscode\extensions"); Cli = @((Join-Path $env:LOCALAPPDATA "Programs\Microsoft VS Code\bin\code.cmd"), (Join-Path $env:ProgramFiles "Microsoft VS Code\bin\code.cmd"), $(if ($pf86) { Join-Path $pf86 "Microsoft VS Code\bin\code.cmd" }), $codeOnPath) },
        @{ Root = (Join-Path $env:USERPROFILE ".cursor\extensions"); Cli = @((Join-Path $env:LOCALAPPDATA "Programs\cursor\resources\app\bin\cursor.cmd")) },
        @{ Root = (Join-Path $env:USERPROFILE ".antigravity\extensions"); Cli = @((Join-Path $env:LOCALAPPDATA "Programs\Antigravity\bin\antigravity.cmd")) },
        @{ Root = (Join-Path $env:USERPROFILE ".windsurf\extensions"); Cli = @((Join-Path $env:LOCALAPPDATA "Programs\Windsurf\bin\windsurf.cmd")) }
    )

    $installed = 0
    foreach ($item in $roots) {
        if (-not (Test-Path $item.Root)) { continue }
        $cli = $item.Cli | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
        $dest = Join-Path $item.Root $folderName
        $cliOk = $false
        if ($cli) {
            & $cli --install-extension $vsixPath --force 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) { $cliOk = $true }
            else { Write-Warning "IDE CLI gagal ($cli, exit $LASTEXITCODE); mendaftarkan lewat extensions.json." }
        }
        if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
        Copy-Item $pkgPath $dest -Force
        Copy-Item $extPath $dest -Force
        if (Test-Path $readmePath) { Copy-Item $readmePath $dest -Force }
        if (Test-Path $iconPath)   { Copy-Item $iconPath $dest -Force }
        $legacy = Join-Path $item.Root $pkg.name
        if (Test-Path $legacy) { Remove-Item $legacy -Recurse -Force -ErrorAction SilentlyContinue }
        foreach ($oldDir in @(Get-ChildItem $item.Root -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like ($extId + "-*") -and $_.Name -ne $folderName })) {
            Remove-Item $oldDir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (-not $cliOk) { Register-ExtensionJson $item.Root $extId $folderName $pkg.version }
        Write-Host "Installed companion into: $dest" -ForegroundColor Green
        $installed++
    }
    if ($installed -eq 0) {
        $vscRoot = Join-Path $env:USERPROFILE ".vscode\extensions"
        $defaultDest = Join-Path $vscRoot $folderName
        New-Item -ItemType Directory -Path $defaultDest -Force | Out-Null
        Copy-Item $pkgPath $defaultDest -Force
        Copy-Item $extPath $defaultDest -Force
        if (Test-Path $readmePath) { Copy-Item $readmePath $defaultDest -Force }
        if (Test-Path $iconPath)   { Copy-Item $iconPath $defaultDest -Force }
        Register-ExtensionJson $vscRoot $extId $folderName $pkg.version
        Write-Host "Created extension folder: $defaultDest" -ForegroundColor Green
    }
} finally {
    if (Test-Path $vsixPath) { Remove-Item $vsixPath -Force -ErrorAction SilentlyContinue }
}

Write-Host "Done! Please reload VS Code / Cursor (Ctrl+Shift+P -> Developer: Reload Window) to activate." -ForegroundColor Cyan
