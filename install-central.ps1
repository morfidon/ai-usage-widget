$ErrorActionPreference = 'Stop'
# Portable official CLI: no agent configuration, proxy, or global PATH changes.
$version = '1.11.0'
$baseUri = 'https://jetbrains-central-cli.s3.eu-west-1.amazonaws.com'
$destination = Join-Path $PSScriptRoot 'tools/central'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$archivePath = Join-Path $destination 'central-download.zip'
try {
    Invoke-WebRequest -Uri "$baseUri/central/$version/central_${version}_windows_x86_64.zip" -OutFile $archivePath
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.Entries | Where-Object { $_.Name -eq 'central.exe' } | Select-Object -First 1
        if ($null -eq $entry) { throw 'Archiwum nie zawiera central.exe.' }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination 'central.exe'), $true)
    } finally { $archive.Dispose() }
} finally {
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
}
Write-Host 'JetBrains Central gotowy. Polacz konto przez menu widgetu: JetBrains Air - polacz konto.'
