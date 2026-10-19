param([string]$Destination = (Join-Path $PSScriptRoot 'tools/central'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/Prerequisites.ps1')
Assert-WidgetPlatform
# Portable official CLI: no agent configuration, proxy, or global PATH changes.
$version = '1.11.0'
$baseUri = 'https://jetbrains-central-cli.s3.eu-west-1.amazonaws.com'
# Repository-pinned SHA-256, captured from the official HTTPS archive on 2026-10-19.
# This is a maintainer pin, not a vendor-published signature. Review URL and hash together on updates.
$sha256 = '4320A89AA0D54E6040D8F5796463B12013D31DEEE306837EDA48C1ED21EB976F'
Assert-WidgetWritableDirectory $Destination
$archivePath = Join-Path $Destination ('central-download-' + [Guid]::NewGuid().ToString('N') + '.zip')
$stagedExe = Join-Path $Destination ('central-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    Invoke-WebRequest -UseBasicParsing -Uri "$baseUri/central/$version/central_${version}_windows_x86_64.zip" -OutFile $archivePath
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $sha256) {
        throw 'Central checksum mismatch. Existing executable was preserved; do not bypass verification.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.Entries | Where-Object { $_.Name -eq 'central.exe' } | Select-Object -First 1
        if ($null -eq $entry) { throw 'Archiwum nie zawiera central.exe.' }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $stagedExe, $false)
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $stagedExe -Destination (Join-Path $Destination 'central.exe') -Force
} finally {
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
    if (Test-Path -LiteralPath $stagedExe) { Remove-Item -LiteralPath $stagedExe }
}
Write-Host 'JetBrains Central gotowy. Polacz konto przez menu widgetu: JetBrains Air - polacz konto.'
