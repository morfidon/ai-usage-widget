param(
    [switch]$NoLaunch,
    [ValidateSet('Keep','Enable','Disable')][string]$Autostart = 'Keep',
    [ValidateSet('Keep','Enable','Disable')][string]$ClaudeBridge = 'Keep'
)
$ErrorActionPreference = 'Stop'
$projectDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$executable = Join-Path $projectDirectory 'dist/AIUsageWidget.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Najpierw uruchom build.ps1.' }
$dataDirectory = Join-Path $projectDirectory 'data'
[IO.Directory]::CreateDirectory($dataDirectory) | Out-Null
if ($ClaudeBridge -eq 'Disable') {
    & (Join-Path $PSScriptRoot 'uninstall.ps1') -OnlyClaudeBridge
}
$claudeSettingsPath = $null
$bridgeCommand = $null
if ($ClaudeBridge -eq 'Enable') {
$claudeDirectory = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$claudeSettingsPath = Join-Path $claudeDirectory 'settings.json'
[IO.Directory]::CreateDirectory($claudeDirectory) | Out-Null
$claudeSettings = if (Test-Path -LiteralPath $claudeSettingsPath) { Get-Content -LiteralPath $claudeSettingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
$bridgePath = (Join-Path $projectDirectory 'claude-bridge.ps1').Replace('\', '/')
$bridgeCommand = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $bridgePath + '"'
$backupPath = Join-Path $dataDirectory 'claude-statusline-backup.json'
if ($claudeSettings.statusLine.command -ne $bridgeCommand) {
    $previous = [ordered]@{ statusLine = $claudeSettings.statusLine }
    [IO.File]::WriteAllText($backupPath, ($previous | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $dataDirectory 'claude-settings-before-install.json'), ($claudeSettings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    $status = [ordered]@{ type = 'command'; command = $bridgeCommand }
    if ($claudeSettings.statusLine) {
        foreach ($property in $claudeSettings.statusLine.PSObject.Properties) {
            if ($property.Name -notin @('type','command')) { $status[$property.Name] = $property.Value }
        }
    }
    $claudeSettings | Add-Member -NotePropertyName statusLine -NotePropertyValue $status -Force
    $temporary = $claudeSettingsPath + '.ai-usage.tmp'
    [IO.File]::WriteAllText($temporary, ($claudeSettings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $claudeSettingsPath -Force
}
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey -Force | Out-Null }
if ($Autostart -eq 'Enable') {
    New-ItemProperty -LiteralPath $runKey -Name AIUsageWidget -Value ('"' + $executable + '" --startup') -PropertyType String -Force | Out-Null
} elseif ($Autostart -eq 'Disable') {
    $currentRun = (Get-ItemProperty -LiteralPath $runKey -Name AIUsageWidget -ErrorAction SilentlyContinue).AIUsageWidget
    if ($currentRun -eq ('"' + $executable + '" --startup')) { Remove-ItemProperty -LiteralPath $runKey -Name AIUsageWidget }
}
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Usage Widget.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $projectDirectory
$shortcut.Description = 'Limity Claude i Codex'
$shortcut.Save()
$installState = [ordered]@{ claudeSettings = $claudeSettingsPath; bridgeCommand = $bridgeCommand; shortcut = $shortcutPath; executable = $executable }
if ($ClaudeBridge -eq 'Keep' -and (Test-Path -LiteralPath (Join-Path $dataDirectory 'install-state.json'))) {
    $oldState = Get-Content -LiteralPath (Join-Path $dataDirectory 'install-state.json') -Raw | ConvertFrom-Json
    $installState.claudeSettings = $oldState.claudeSettings
    $installState.bridgeCommand = $oldState.bridgeCommand
}
[IO.File]::WriteAllText((Join-Path $dataDirectory 'install-state.json'), ($installState | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Installed AI Usage Widget. Autostart: $Autostart. Claude bridge: $ClaudeBridge."
if (-not $NoLaunch) { Start-Process -FilePath $executable -WorkingDirectory $projectDirectory -WindowStyle Hidden }
