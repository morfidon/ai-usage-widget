param([switch]$KeepClaudeBridge)
$ErrorActionPreference = 'Stop'
$dataDirectory = Join-Path $PSScriptRoot 'data'
$statePath = Join-Path $dataDirectory 'install-state.json'
if (-not (Test-Path -LiteralPath $statePath)) { throw 'Brak informacji o instalacji.' }
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$current = (Get-ItemProperty -LiteralPath $runKey -Name AIUsageWidget -ErrorAction SilentlyContinue).AIUsageWidget
if ($current -eq ('"' + $state.executable + '" --startup')) { Remove-ItemProperty -LiteralPath $runKey -Name AIUsageWidget }
if (-not $KeepClaudeBridge -and (Test-Path -LiteralPath $state.claudeSettings)) {
    $settings = Get-Content -LiteralPath $state.claudeSettings -Raw | ConvertFrom-Json
    if ($settings.statusLine.command -eq $state.bridgeCommand) {
        $backup = Get-Content -LiteralPath (Join-Path $dataDirectory 'claude-statusline-backup.json') -Raw | ConvertFrom-Json
        if ($backup.statusLine) { $settings.statusLine = $backup.statusLine }
        else { $settings.PSObject.Properties.Remove('statusLine') }
        [IO.File]::WriteAllText($state.claudeSettings, ($settings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    }
}
if (Test-Path -LiteralPath $state.shortcut) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($state.shortcut).TargetPath -eq $state.executable) { Remove-Item -LiteralPath $state.shortcut }
}
Write-Output 'Wylaczono autostart i przywrocono poprzedni pasek Claude. Widget mozna zamknac przez menu ikony przy zegarze.'
