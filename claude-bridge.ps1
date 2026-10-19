$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    $payload = $raw | ConvertFrom-Json
    $dataDirectory = Join-Path $PSScriptRoot 'data'
    $layoutPath = Join-Path $PSScriptRoot 'install-layout.json'
    if (Test-Path -LiteralPath $layoutPath) {
        $dataDirectory = (Get-Content -LiteralPath $layoutPath -Raw | ConvertFrom-Json).DataDirectory
        if (-not $dataDirectory -or -not [IO.Path]::IsPathRooted($dataDirectory)) { throw 'Invalid data directory.' }
    }
    if ($payload.rate_limits) {
        [IO.Directory]::CreateDirectory($dataDirectory) | Out-Null
        # Save only quotas. Never persist prompts, transcripts, account identifiers or tokens.
        $filtered = [ordered]@{
            updated_at = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            rate_limits = $payload.rate_limits
        }
        $temporary = Join-Path $dataDirectory ('claude-live.' + [Guid]::NewGuid().ToString('N') + '.tmp')
        [IO.File]::WriteAllText($temporary, ($filtered | ConvertTo-Json -Depth 15), [Text.UTF8Encoding]::new($false))
        $target = Join-Path $dataDirectory 'claude-live.json'
        # Several Claude sessions can update the same cache. Atomic replacement avoids partial reads.
        if ([IO.File]::Exists($target)) { [IO.File]::Replace($temporary, $target, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $target) }
    }
    $backup = Join-Path $dataDirectory 'claude-statusline-backup.json'
    $previous = if (Test-Path -LiteralPath $backup) { Get-Content -Raw -LiteralPath $backup | ConvertFrom-Json } else { $null }
    if ($previous.statusLine.command) {
        # The original status line is trusted user configuration, preserved verbatim.
        $bash = @((Join-Path $env:ProgramFiles 'Git/bin/bash.exe'), (Join-Path $env:LOCALAPPDATA 'Programs/Git/bin/bash.exe')) |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($bash) { $raw | & $bash -c $previous.statusLine.command }
        else { $raw | & powershell.exe -NoProfile -Command $previous.statusLine.command }
    } else {
        $parts = @()
        if ($null -ne $payload.rate_limits.five_hour.used_percentage) { $parts += ('5h: {0:0}% wolne' -f (100 - $payload.rate_limits.five_hour.used_percentage)) }
        if ($null -ne $payload.rate_limits.seven_day.used_percentage) { $parts += ('7d: {0:0}% wolne' -f (100 - $payload.rate_limits.seven_day.used_percentage)) }
        $model = $payload.model.display_name
        if ($parts.Count) { [Console]::WriteLine(('[' + $model + '] | ' + ($parts -join ' | '))) }
        elseif ($model) { [Console]::WriteLine(('[' + $model + ']')) }
    }
} catch {
    # A status line failure must not interrupt an active Claude session.
    exit 0
}
