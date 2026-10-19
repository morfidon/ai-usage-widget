param(
    [switch]$NoLaunch,
    [ValidateSet('Keep','Enable','Disable')][string]$Autostart = 'Keep',
    [ValidateSet('Keep','Enable','Disable')][string]$ClaudeBridge = 'Keep',
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs/AIUsageWidget'),
    [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA 'AIUsageWidget/data'),
    [string]$BuildDirectory = (Join-Path $PSScriptRoot 'dist'),
    [switch]$SkipLegacyMigration,
    # Overrides allow lifecycle tests to use isolated registry keys and shortcuts.
    [string]$RunKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    [string]$ShortcutDirectory = ([Environment]::GetFolderPath('Programs'))
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/Prerequisites.ps1')
. (Join-Path $PSScriptRoot 'scripts/Layout.ps1')
Assert-WidgetPlatform
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$DataDirectory = [IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
$source = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
if ($InstallDirectory -eq $source -or $source.StartsWith($InstallDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $DataDirectory -eq $InstallDirectory -or $DataDirectory.StartsWith($InstallDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installation must be separate from the source and data directories.'
}
if (-not (Test-Path -LiteralPath (Join-Path $BuildDirectory 'AIUsageWidget.exe'))) { throw 'Build first, or run setup.ps1.' }
$layoutPath = Join-Path $InstallDirectory 'install-layout.json'
if ((Test-Path -LiteralPath $InstallDirectory) -and (Get-ChildItem -LiteralPath $InstallDirectory -Force | Select-Object -First 1) -and
    -not (Test-Path -LiteralPath $layoutPath)) { throw 'Refusing to overwrite a non-empty directory without an AI Usage Widget installation marker.' }
if (Test-Path -LiteralPath $layoutPath) {
    $existingLayout = Get-Content -LiteralPath $layoutPath -Raw | ConvertFrom-Json
    if ($existingLayout.Application -ne 'AIUsageWidget' -or $existingLayout.DataDirectory -ne $DataDirectory) {
        throw 'Existing installation belongs to another layout or data directory. Reuse its DataDirectory; do not overwrite it.'
    }
}
Assert-WidgetWritableDirectory $InstallDirectory
Assert-WidgetWritableDirectory $DataDirectory
$executable = Join-Path $InstallDirectory 'bin/AIUsageWidget.exe'
if (Test-Path -LiteralPath $executable) {
    try { $probe = [IO.File]::Open($executable, 'Open', 'ReadWrite', 'None'); $probe.Dispose() }
    catch { throw 'Installed widget is running. Exit it through the tray menu and rerun setup.' }
}

# Migrate only known widget files. Leave originals for recovery; existing destination files win.
$legacyData = Join-Path $source 'data'
foreach ($name in @('settings.json','usage-cache.json','usage-history.json','claude-live.json',
    'claude-statusline-backup.json','claude-settings-before-install.json','install-state.json')) {
    $oldFile = Join-Path $legacyData $name
    $newFile = Join-Path $DataDirectory $name
    if (-not $SkipLegacyMigration -and (Test-Path -LiteralPath $oldFile) -and -not (Test-Path -LiteralPath $newFile)) {
        Copy-Item -LiteralPath $oldFile -Destination $newFile
    }
}
$statePath = Join-Path $DataDirectory 'install-state.json'
$oldState = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
$claudeSettingsPath = if ($oldState) { $oldState.claudeSettings } else { $null }
$bridgeCommand = if ($oldState) { $oldState.bridgeCommand } else { $null }
$previousExe = if ($oldState) { $oldState.executable } else { $null }
if ($ClaudeBridge -eq 'Enable') {
    $claudeDirectory = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
    $requestedSettings = Join-Path $claudeDirectory 'settings.json'
    if ($claudeSettingsPath -and $claudeSettingsPath -ne $requestedSettings) {
        throw 'Claude configuration directory changed. Disable the old bridge with its installed uninstall.ps1 -OnlyClaudeBridge before switching.'
    }
    $claudeSettingsPath = $requestedSettings
}
# Parse before changing external configuration; malformed user settings remain untouched.
$claudeSettings = if ($claudeSettingsPath -and (Test-Path -LiteralPath $claudeSettingsPath)) {
    Get-Content -LiteralPath $claudeSettingsPath -Raw | ConvertFrom-Json
} else { [pscustomobject]@{} }
if ($ClaudeBridge -eq 'Disable' -and $oldState -and $oldState.bridgeCommand -eq $claudeSettings.statusLine.command) {
    & (Join-Path $source 'uninstall.ps1') -OnlyClaudeBridge -DataDirectory $DataDirectory
    $claudeSettingsPath = $null; $bridgeCommand = $null
}

$bin = Join-Path $InstallDirectory 'bin'
[IO.Directory]::CreateDirectory($bin) | Out-Null
Get-ChildItem -LiteralPath $BuildDirectory -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $bin -Force }
foreach ($name in @('claude-bridge.ps1','uninstall.ps1','Connect JetBrains Air.cmd')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $InstallDirectory -Force
}
[IO.Directory]::CreateDirectory((Join-Path $InstallDirectory 'scripts')) | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'scripts/Layout.ps1') -Destination (Join-Path $InstallDirectory 'scripts/Layout.ps1') -Force
$central = Join-Path $source 'tools/central/central.exe'
if (Test-Path -LiteralPath $central) {
    [IO.Directory]::CreateDirectory((Join-Path $InstallDirectory 'tools/central')) | Out-Null
    Copy-Item -LiteralPath $central -Destination (Join-Path $InstallDirectory 'tools/central/central.exe') -Force
}
$layout = [ordered]@{ Application = 'AIUsageWidget'; SchemaVersion = 1; DataDirectory = $DataDirectory }
# A private SDK is never registered globally. Copy its runtime so the apphost also works at next logon.
$privateDotNet = Join-Path $env:LOCALAPPDATA 'AIUsageWidget/dotnet'
if (Test-Path -LiteralPath (Join-Path $privateDotNet 'dotnet.exe')) {
    $runtimeDirectory = Join-Path $InstallDirectory 'dotnet'
    [IO.Directory]::CreateDirectory($runtimeDirectory) | Out-Null
    foreach ($component in @('host','shared','dotnet.exe','LICENSE.txt','ThirdPartyNotices.txt')) {
        $componentPath = Join-Path $privateDotNet $component
        if (Test-Path -LiteralPath $componentPath) { Copy-Item -LiteralPath $componentPath -Destination $runtimeDirectory -Recurse -Force }
    }
}
[IO.File]::WriteAllText($layoutPath, ($layout | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

$newBridge = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + (Join-Path $InstallDirectory 'claude-bridge.ps1').Replace('\','/') + '"'
$ownsBridge = $oldState -and $oldState.bridgeCommand -and $oldState.bridgeCommand -eq $claudeSettings.statusLine.command
if ($ClaudeBridge -eq 'Enable' -or ($ClaudeBridge -eq 'Keep' -and $ownsBridge)) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $claudeSettingsPath)) | Out-Null
    $backupPath = Join-Path $DataDirectory 'claude-statusline-backup.json'
    if (-not $ownsBridge -and $claudeSettings.statusLine.command -ne $newBridge) {
        [IO.File]::WriteAllText($backupPath, (@{ statusLine = $claudeSettings.statusLine } | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $DataDirectory 'claude-settings-before-install.json'), ($claudeSettings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    }
    $status = [ordered]@{ type = 'command'; command = $newBridge }
    if ($claudeSettings.statusLine) {
        foreach ($property in $claudeSettings.statusLine.PSObject.Properties) {
            if ($property.Name -notin @('type','command')) { $status[$property.Name] = $property.Value }
        }
    }
    $claudeSettings | Add-Member -NotePropertyName statusLine -NotePropertyValue $status -Force
    $temporary = $claudeSettingsPath + '.ai-usage.tmp'
    [IO.File]::WriteAllText($temporary, ($claudeSettings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $claudeSettingsPath -Force
    $bridgeCommand = $newBridge
}
$currentRun = (Get-ItemProperty -LiteralPath $RunKeyPath -Name AIUsageWidget -ErrorAction SilentlyContinue).AIUsageWidget
$ownsRun = $currentRun -and ($currentRun -eq ('"' + $previousExe + '" --startup') -or $currentRun -eq ('"' + $executable + '" --startup'))
if ($Autostart -eq 'Enable' -or ($Autostart -eq 'Keep' -and $ownsRun)) {
    if (-not (Test-Path -LiteralPath $RunKeyPath)) { New-Item -Path $RunKeyPath -Force | Out-Null }
    New-ItemProperty -LiteralPath $RunKeyPath -Name AIUsageWidget -Value ('"' + $executable + '" --startup') -PropertyType String -Force | Out-Null
} elseif ($Autostart -eq 'Disable' -and $ownsRun) { Remove-ItemProperty -LiteralPath $RunKeyPath -Name AIUsageWidget }
[IO.Directory]::CreateDirectory($ShortcutDirectory) | Out-Null
$shortcutPath = Join-Path $ShortcutDirectory 'AI Usage Widget.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable; $shortcut.WorkingDirectory = $InstallDirectory
$shortcut.Description = 'AI subscription quotas'; $shortcut.Save()
$state = [ordered]@{ claudeSettings = $claudeSettingsPath; bridgeCommand = $bridgeCommand; shortcut = $shortcutPath; executable = $executable; runKey = $RunKeyPath }
[IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Installed: $executable"
Write-Output "Data: $DataDirectory"
Write-Output "Autostart: $Autostart. Claude bridge: $ClaudeBridge."
if (-not $NoLaunch) { Start-Process -FilePath $executable -WorkingDirectory $InstallDirectory -WindowStyle Hidden }
