function Get-WidgetDetectedProviders {
    $found = @()
    $codexRoot = Join-Path $env:LOCALAPPDATA 'OpenAI/Codex/bin'
    if ((Get-Command codex.exe -ErrorAction SilentlyContinue) -or
        ((Test-Path -LiteralPath $codexRoot) -and (Get-ChildItem -LiteralPath $codexRoot -Filter codex.exe -Recurse -File | Select-Object -First 1))) { $found += 'Codex' }
    if ((Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.local/bin/claude.exe')) -or
        (Get-Command claude.exe -ErrorAction SilentlyContinue)) { $found += 'Claude' }
    if (Test-Path -LiteralPath (Join-Path $env:APPDATA 'devin/User/globalStorage/state.vscdb')) { $found += 'Devin' }
    if ((Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.local/bin/central.exe')) -or
        (Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.jetbrains-central'))) { $found += 'JetBrains Air' }
    if ((Get-Process -Name Antigravity -ErrorAction SilentlyContinue) -or
        (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs/Antigravity'))) { $found += 'Antigravity' }
    return $found
}
