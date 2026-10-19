param([switch]$KeepClaudeBridge, [switch]$OnlyClaudeBridge, [string]$DataDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/Layout.ps1')
if (-not $DataDirectory) { $DataDirectory = Get-WidgetDataDirectory $PSScriptRoot }
$statePath = Join-Path $dataDirectory 'install-state.json'
if (-not (Test-Path -LiteralPath $statePath)) { Write-Output 'No installation state; nothing to restore.'; return }
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$runKey = if ($state.runKey) { $state.runKey } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' }
$current = (Get-ItemProperty -LiteralPath $runKey -Name AIUsageWidget -ErrorAction SilentlyContinue).AIUsageWidget
if (-not $OnlyClaudeBridge -and $current -eq ('"' + $state.executable + '" --startup')) { Remove-ItemProperty -LiteralPath $runKey -Name AIUsageWidget }
if (-not $KeepClaudeBridge -and $state.claudeSettings -and (Test-Path -LiteralPath $state.claudeSettings)) {
    $settings = Get-Content -LiteralPath $state.claudeSettings -Raw | ConvertFrom-Json
    if ($settings.statusLine.command -eq $state.bridgeCommand) {
        $backup = Get-Content -LiteralPath (Join-Path $dataDirectory 'claude-statusline-backup.json') -Raw | ConvertFrom-Json
        if ($backup.statusLine) { $settings.statusLine = $backup.statusLine }
        else { $settings.PSObject.Properties.Remove('statusLine') }
        [IO.File]::WriteAllText($state.claudeSettings, ($settings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    }
}
if (-not $OnlyClaudeBridge -and (Test-Path -LiteralPath $state.shortcut)) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($state.shortcut).TargetPath -eq $state.executable) { Remove-Item -LiteralPath $state.shortcut }
}
if ($OnlyClaudeBridge) {
    $state.claudeSettings = $null; $state.bridgeCommand = $null
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
Write-Output 'Removed owned integrations. Files and history were kept. Exit the widget through its tray menu.'
