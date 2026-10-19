function Install-WidgetDotNet([string]$Destination = (Join-Path $env:LOCALAPPDATA 'AIUsageWidget/dotnet')) {
    Assert-WidgetPlatform
    $Destination = [IO.Path]::GetFullPath($Destination)
    $parent = Split-Path -Parent $Destination
    Assert-WidgetWritableDirectory $parent
    $archivePath = Join-Path $parent ('sdk-' + [Guid]::NewGuid().ToString('N') + '.zip')
    $stage = Join-Path $parent ('sdk-stage-' + [Guid]::NewGuid().ToString('N'))
    # Microsoft release metadata is the trust source for the SHA-512, not an unchecked remote script.
    $metadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/9.0/releases.json'
    $version = $metadata.'latest-sdk'
    if ($version -notmatch '^9\.0\.\d+$') { throw 'Microsoft metadata did not provide a stable .NET 9 SDK.' }
    $release = $metadata.releases | Where-Object { $_.sdk.version -eq $version } | Select-Object -First 1
    $file = $release.sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
    $uri = if ($file) { [Uri]$file.url } else { $null }
    if (-not $uri -or $uri.Scheme -ne 'https' -or $uri.Host -ne 'builds.dotnet.microsoft.com' -or $file.hash -notmatch '^[a-fA-F0-9]{128}$') {
        throw 'Unexpected SDK artifact or hash in Microsoft release metadata.'
    }
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $uri.AbsoluteUri -OutFile $archivePath
        if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash -ne $file.hash) { throw 'SDK checksum mismatch; existing SDK was preserved.' }
        Expand-Archive -LiteralPath $archivePath -DestinationPath $stage
        $exe = Join-Path $stage 'dotnet.exe'
        $actual = & $exe --version | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0 -or $actual -ne $version) { throw 'Downloaded SDK did not pass version verification.' }
        $runtimes = & $exe --list-runtimes
        if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft.WindowsDesktop.App 9\.0\.')) { throw 'SDK archive did not contain the required Desktop Runtime.' }
        if (Test-Path -LiteralPath $Destination) {
            # Keep the prior directory as a backup. Both paths are explicitly constrained to this parent.
            $backup = Join-Path $parent ('dotnet-backup-' + [Guid]::NewGuid().ToString('N'))
            if ((Split-Path -Parent $Destination) -ne $parent -or (Split-Path -Parent $backup) -ne $parent) { throw 'Unsafe SDK backup path.' }
            Move-Item -LiteralPath $Destination -Destination $backup
        }
        Move-Item -LiteralPath $stage -Destination $Destination
        Write-Output "Installed private .NET SDK $version. Global PATH and machine settings were not changed."
    } finally {
        if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
        # An incomplete staging directory is retained for inspection on failure.
    }
}
