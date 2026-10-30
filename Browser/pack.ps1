# Packt die YT-Bridge-Erweiterungen zum Hochladen: Firefox (addons.mozilla.org) und Chrome (Chrome Web Store)
# -> dist\knobflyout-yt-bridge-<browser>-<version>.zip. Zip-Einträge selbst mit "/" anlegen: Compress-Archive
# in PowerShell 5.1 schreibt "\" in die Pfade (icons\icon-48.png), das lehnt addons.mozilla.org ab.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path (Split-Path -Parent $root) 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

foreach ($browser in 'firefox', 'chrome') {
    $src = Join-Path $root $browser
    $version = (Get-Content (Join-Path $src 'manifest.json') -Raw | ConvertFrom-Json).version
    $zip = Join-Path $dist "knobflyout-yt-bridge-$browser-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    $archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        Get-ChildItem $src -Recurse -File | ForEach-Object {
            $name = $_.FullName.Substring($src.Length + 1).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, 'Optimal')
        }
    }
    finally { $archive.Dispose() }
    Write-Host "$browser $version -> $zip"
}
