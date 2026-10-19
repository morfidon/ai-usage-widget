param(
    [string[]]$Providers,
    [switch]$InstallPrerequisites,
    [ValidateSet('Keep','Enable','Disable')][string]$Autostart = 'Keep',
    [ValidateSet('Keep','Enable','Disable')][string]$ClaudeBridge = 'Keep',
    [switch]$NoLaunch,
    [switch]$CheckOnly,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs/AIUsageWidget'),
    [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA 'AIUsageWidget/data'),
    [string]$BuildDirectory = (Join-Path $PSScriptRoot 'dist'),
    [switch]$SkipLegacyMigration,
    [string]$RunKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    [string]$ShortcutDirectory = ([Environment]::GetFolderPath('Programs'))
)
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'scripts/Prerequisites.ps1')
    . (Join-Path $PSScriptRoot 'scripts/ProviderDetection.ps1')
    Assert-WidgetPlatform
    $valid = @('Codex','Claude','Devin','JetBrains Air','Antigravity')
    if ($PSBoundParameters.ContainsKey('Providers')) {
        # Both a PowerShell array and a comma-separated string work with powershell.exe -File.
        $Providers = @($Providers | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
        foreach ($name in $Providers) { if ($name -notin $valid) { throw "Unknown provider '$name'. Choose: $($valid -join ', ')." } }
    } else {
        $settingsPath = Join-Path $DataDirectory 'settings.json'
        if (-not (Test-Path -LiteralPath $settingsPath) -and -not $SkipLegacyMigration) {
            $settingsPath = Join-Path $PSScriptRoot 'data/settings.json'
        }
        if (Test-Path -LiteralPath $settingsPath) {
            $existing = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            $Providers = if ($null -ne $existing.EnabledProviders) { @($existing.EnabledProviders) } else { $valid }
        } else { $Providers = @(Get-WidgetDetectedProviders) }
    }
    if ($Providers.Count -eq 0) { throw 'No supported clients detected. Install and sign in to a provider app, then rerun; or select explicitly, e.g. -Providers Codex.' }
    if ($ClaudeBridge -eq 'Enable' -and 'Claude' -notin $Providers) { throw 'Select Claude before enabling its status line bridge.' }
    try { $dotnet = Assert-WidgetDotNet $PSScriptRoot }
    catch {
        if (-not $InstallPrerequisites -or $CheckOnly) { throw }
        . (Join-Path $PSScriptRoot 'scripts/Install-DotNet.ps1')
        Install-WidgetDotNet
        $dotnet = Assert-WidgetDotNet $PSScriptRoot
    }
    if ($CheckOnly) {
        Write-Output (@{ Platform = 'Windows x64'; DotNet = $dotnet; Providers = $Providers; InstallDirectory = $InstallDirectory; DataDirectory = $DataDirectory } | ConvertTo-Json)
        exit 0
    }
    Assert-WidgetWritableDirectory $DataDirectory
    & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $BuildDirectory
    & (Join-Path $PSScriptRoot 'tests/test_bridge.ps1')
    if ('JetBrains Air' -in $Providers) { & (Join-Path $PSScriptRoot 'install-central.ps1') }
    & (Join-Path $PSScriptRoot 'install.ps1') -NoLaunch -InstallDirectory $InstallDirectory -DataDirectory $DataDirectory -Autostart $Autostart -ClaudeBridge $ClaudeBridge -BuildDirectory $BuildDirectory -SkipLegacyMigration:$SkipLegacyMigration -RunKeyPath $RunKeyPath -ShortcutDirectory $ShortcutDirectory
    $settingsPath = Join-Path $DataDirectory 'settings.json'
    $settings = if (Test-Path -LiteralPath $settingsPath) { Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    $settings | Add-Member -NotePropertyName EnabledProviders -NotePropertyValue @($Providers) -Force
    if ($settings.TrayProvider -notin $Providers -and ($Providers -contains 'Codex' -or $Providers -contains 'Claude')) {
        $settings | Add-Member -NotePropertyName TrayProvider -NotePropertyValue (@($Providers | Where-Object { $_ -in @('Codex','Claude') })[0]) -Force
    }
    [IO.File]::WriteAllText($settingsPath + '.tmp', ($settings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath ($settingsPath + '.tmp') -Destination $settingsPath -Force
    $installedDll = Join-Path $InstallDirectory 'bin/AIUsageWidget.dll'
    & $dotnet $installedDll --doctor
    $diagnosticExit = $LASTEXITCODE
    $windowVerified = $false
    if (-not $NoLaunch) {
        $checkPath = Join-Path $DataDirectory 'window-check.json'
        if (Test-Path -LiteralPath $checkPath) { Remove-Item -LiteralPath $checkPath }
        Start-Process -FilePath (Join-Path $InstallDirectory 'bin/AIUsageWidget.exe') -ArgumentList '--capture' -WorkingDirectory $InstallDirectory -WindowStyle Hidden | Out-Null
        foreach ($attempt in 1..20) {
            Start-Sleep -Seconds 1
            if (Test-Path -LiteralPath $checkPath) {
                $windowVerified = [bool](Get-Content -LiteralPath $checkPath -Raw | ConvertFrom-Json).Visible
                break
            }
        }
    }
    $checks = Get-Content -LiteralPath (Join-Path $DataDirectory 'setup-diagnostic.json') -Raw | ConvertFrom-Json
    $result = [ordered]@{ SchemaVersion = 1; Installed = $true; Executable = (Join-Path $InstallDirectory 'bin/AIUsageWidget.exe')
        Providers = $Providers; Checks = $checks.Checks; WindowVerified = $windowVerified; LaunchSkipped = [bool]$NoLaunch
        ExitCode = $(if (-not $NoLaunch -and -not $windowVerified) { 2 } else { $diagnosticExit }) }
    [IO.File]::WriteAllText((Join-Path $DataDirectory 'setup-result.json'), ($result | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Write-Output ($result | ConvertTo-Json -Depth 10)
    Write-Output 'Exit 0: ready. Exit 3: installed; user login or running provider app required. Exit 2: an error needs attention.'
    exit $result.ExitCode
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 2
}
