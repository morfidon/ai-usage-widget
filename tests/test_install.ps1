param([string]$BuildDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $root ('.verify/install-' + [Guid]::NewGuid().ToString('N'))
$runKey = 'HKCU:\Software\AIUsageWidget-Tests-' + [Guid]::NewGuid().ToString('N')
$priorClaude = $env:CLAUDE_CONFIG_DIR
$env:CLAUDE_CONFIG_DIR = Join-Path $fixture 'claude'
$options = @{ NoLaunch = $true; SkipLegacyMigration = $true; BuildDirectory = $BuildDirectory
    InstallDirectory = (Join-Path $fixture 'app'); DataDirectory = (Join-Path $fixture 'data')
    ShortcutDirectory = (Join-Path $fixture 'shortcuts'); RunKeyPath = $runKey }
function Check($Condition, [string]$Message) { if (-not $Condition) { throw $Message }; Write-Output "PASS $Message" }
try {
    & (Join-Path $root 'install.ps1') @options
    Check (-not (Test-Path -LiteralPath $env:CLAUDE_CONFIG_DIR)) 'Fresh defaults do not create Claude settings'
    Check (-not (Test-Path -LiteralPath $runKey)) 'Fresh defaults do not enable autostart'
    [IO.Directory]::CreateDirectory($env:CLAUDE_CONFIG_DIR) | Out-Null
    $settingsPath = Join-Path $env:CLAUDE_CONFIG_DIR 'settings.json'
    [IO.File]::WriteAllText($settingsPath, '{"theme":"dark","statusLine":{"type":"command","command":"echo original-status","padding":2}}')
    & (Join-Path $root 'install.ps1') @options -Autostart Enable -ClaudeBridge Enable
    $backupPath = Join-Path $options.DataDirectory 'claude-statusline-backup.json'
    $originalBackup = [IO.File]::ReadAllText($backupPath)
    $configured = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Check ($configured.theme -eq 'dark' -and $configured.statusLine.padding -eq 2) 'Installation preserves unrelated Claude settings and status line options'
    $expectedExe = Join-Path $options.InstallDirectory 'bin/AIUsageWidget.exe'
    Check ((Get-ItemProperty -LiteralPath $runKey).AIUsageWidget -eq ('"' + $expectedExe + '" --startup')) 'Autostart targets the stable installed executable'
    $configured | Add-Member -NotePropertyName addedAfterInstall -NotePropertyValue 'keep-me'
    [IO.File]::WriteAllText($settingsPath, ($configured | ConvertTo-Json -Depth 30))
    [IO.File]::WriteAllText((Join-Path $options.DataDirectory 'usage-history.json'), '[]')
    [IO.File]::WriteAllText((Join-Path $options.DataDirectory 'settings.json'), '{"EnabledProviders":[]}')
    & (Join-Path $root 'install.ps1') @options
    Check ([IO.File]::ReadAllText($backupPath) -eq $originalBackup) 'Reinstallation retains the original status line backup'
    Check ([IO.File]::ReadAllText((Join-Path $options.DataDirectory 'usage-history.json')) -eq '[]') 'Reinstallation preserves history'
    $installed = Join-Path $options.InstallDirectory 'bin/AIUsageWidget.dll'
    dotnet $installed --doctor
    Check ($LASTEXITCODE -eq 0) 'Installed diagnostics use separate data and ignore unselected providers'
    & (Join-Path $options.InstallDirectory 'uninstall.ps1') -OnlyClaudeBridge
    $restored = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Check ($restored.statusLine.command -eq 'echo original-status' -and $restored.addedAfterInstall -eq 'keep-me') 'Bridge rollback restores the original while preserving subsequent changes'
    Check ((Get-ItemProperty -LiteralPath $runKey).AIUsageWidget) 'Bridge-only rollback preserves autostart'
    & (Join-Path $root 'install.ps1') @options -ClaudeBridge Enable
    & (Join-Path $options.InstallDirectory 'uninstall.ps1')
    & (Join-Path $options.InstallDirectory 'uninstall.ps1')
    Check (-not (Get-ItemProperty -LiteralPath $runKey -Name AIUsageWidget -ErrorAction SilentlyContinue)) 'Repeated uninstall removes owned autostart safely'
    Check (-not (Test-Path -LiteralPath (Join-Path $options.ShortcutDirectory 'AI Usage Widget.lnk'))) 'Uninstall removes its shortcut'
    Check (Test-Path -LiteralPath (Join-Path $options.DataDirectory 'usage-history.json')) 'Uninstall retains user history'

    & (Join-Path $root 'install.ps1') @options -ClaudeBridge Enable -Autostart Enable
    [IO.File]::WriteAllText($settingsPath, '{"statusLine":{"type":"command","command":"echo user-replacement"}}')
    Set-ItemProperty -LiteralPath $runKey -Name AIUsageWidget -Value 'user-replacement'
    & (Join-Path $options.InstallDirectory 'uninstall.ps1')
    Check ((Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).statusLine.command -eq 'echo user-replacement') 'Uninstall preserves a user-replaced status line'
    Check ((Get-ItemProperty -LiteralPath $runKey).AIUsageWidget -eq 'user-replacement') 'Uninstall preserves a user-replaced startup entry'
    [IO.File]::WriteAllText($settingsPath, '{broken')
    $failed = $false
    try { & (Join-Path $root 'install.ps1') @options -ClaudeBridge Enable } catch { $failed = $true }
    Check ($failed -and [IO.File]::ReadAllText($settingsPath) -eq '{broken') 'Malformed Claude settings fail without rewriting the file'
    Write-Output "Installation lifecycle passed. Fixture retained: $fixture"
} finally {
    $env:CLAUDE_CONFIG_DIR = $priorClaude
    # Only this test's explicitly named registry key is removed; fixture files remain inspectable.
    if (Test-Path -LiteralPath $runKey) { Remove-Item -LiteralPath $runKey }
}
