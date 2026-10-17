$ErrorActionPreference = 'Stop'
$fixture = Join-Path $PSScriptRoot 'bridge-fixture'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'data')) | Out-Null
Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'claude-bridge.ps1') -Destination (Join-Path $fixture 'claude-bridge.ps1') -Force
[IO.File]::WriteAllText((Join-Path $fixture 'data/claude-statusline-backup.json'), '{"statusLine":null}')
$bridge = Join-Path $fixture 'claude-bridge.ps1'
$payload = '{"model":{"display_name":"Sonnet"},"rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1999999999},"seven_day":{"used_percentage":41.2,"resets_at":1999999998}},"transcript_path":"SECRET_TRANSCRIPT","accessToken":"SECRET_TOKEN","prompt":"SECRET_PROMPT"}'
$out = $payload | powershell -NoProfile -ExecutionPolicy Bypass -File $bridge
if ($out -notmatch '5h: 77%') { throw 'Status line output failed' }
$cachePath = Join-Path $fixture 'data/claude-live.json'
$raw = Get-Content -LiteralPath $cachePath -Raw
$cache = $raw | ConvertFrom-Json
if ($cache.rate_limits.five_hour.used_percentage -ne 23.5) { throw 'Rate limit persistence failed' }
if ($raw -match 'SECRET_|transcript_path|accessToken|prompt') { throw 'Sensitive data leaked into cache' }
if ($cache.updated_at -lt ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - 20)) { throw 'Cache timestamp failed' }
'{"model":{"display_name":"Sonnet"}}' | powershell -NoProfile -ExecutionPolicy Bypass -File $bridge | Out-Null
if ((Get-Content -LiteralPath $cachePath -Raw) -ne $raw) { throw 'Missing limits clobbered existing values' }
[IO.File]::WriteAllText((Join-Path $fixture 'data/claude-statusline-backup.json'), '{"statusLine":{"type":"command","command":"echo original-status"}}')
$preserved = $payload | powershell -NoProfile -ExecutionPolicy Bypass -File $bridge
if (($preserved | Out-String) -notmatch 'original-status') { throw 'Previous status line was not preserved' }
Write-Output 'PASS Claude bridge: quota persistence, output, timestamp, missing fields, sensitive data filtering, original status line'
