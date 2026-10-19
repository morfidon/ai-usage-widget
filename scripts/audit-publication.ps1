param([switch]$CurrentTreeOnly)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath (Split-Path $PSScriptRoot -Parent)
try {
    # Deliberately emit filenames/commit IDs only, never matching secret values.
    # Heuristic release hygiene, not a guarantee or substitute for manual review.
    $pattern = 'sk-(proj|svcacct)-[A-Za-z0-9_-]{24,}|sk-ant-[A-Za-z0-9_-]{24,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[A-Z0-9]{16}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
    $issues = @()
    $files = @(git ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    foreach ($file in $files) {
        if ($file -match '(^|/)(data|dist|\.build-cli|\.nuget)/|(^|/)(\.env($|\.)|\.credentials\.json$|auth\.json$|tokens\.enc$)|\.(pem|pfx|key)$') {
            $issues += "Private/runtime filename tracked: $file"
        }
    }
    $matches = @(git grep -I -l -E -- $pattern)
    if ($LASTEXITCODE -gt 1) { throw 'Working-tree content scan failed.' }
    $issues += @($matches | ForEach-Object { "Possible secret in working tree: $_" })
    $commits = @()
    if (-not $CurrentTreeOnly) {
        $commits = @(git rev-list --all)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository history.' }
        foreach ($commit in $commits) {
            $matches = @(git grep -I -l -E $pattern $commit --)
            if ($LASTEXITCODE -gt 1) { throw "Content scan failed for commit $commit." }
            $issues += @($matches | ForEach-Object { "Possible secret in history: $_" })
        }
        $historicalNames = @(git log --all --format= --name-only)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate historical filenames.' }
        $issues += @($historicalNames | Where-Object { $_ -match '(^|/)(data|dist|\.build-cli|\.nuget)/|(^|/)(\.env($|\.)|\.credentials\.json$|auth\.json$|tokens\.enc$)|\.(pem|pfx|key)$' } |
            Select-Object -Unique | ForEach-Object { "Private/runtime filename in history: $_" })
    }
    if ($issues.Count) {
        $issues | Select-Object -Unique | Write-Output
        Write-Output 'Review locally before publishing. Values were suppressed. Do not rewrite history or delete files automatically.'
        exit 2
    }
    Write-Output "No matches for the configured secret patterns or private/runtime filenames. Checked $($files.Count) tracked files and $($commits.Count) commits."
    Write-Output 'Manually review identity, screenshots, licensing and uncommon credential formats before publication.'
    exit 0
} finally { Pop-Location }
