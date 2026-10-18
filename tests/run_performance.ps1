param([string]$Baseline = 'a348497')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$commit = (git rev-parse --verify "$Baseline^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid baseline commit.' }
$evidence = Join-Path $root 'data/performance'
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$archive = Join-Path $evidence 'baseline.zip'
$source = Join-Path $evidence ('baseline-' + $commit.Substring(0, 12))
git archive --format=zip --output=$archive $commit
if ($LASTEXITCODE -ne 0) { throw 'Cannot export baseline.' }
Expand-Archive -LiteralPath $archive -DestinationPath $source -Force
Copy-Item -LiteralPath (Join-Path $root 'PerformanceBenchmarks.cs') -Destination (Join-Path $source 'PerformanceBenchmarks.cs')
$appPath = Join-Path $source 'App.cs'
$appText = [IO.File]::ReadAllText($appPath)
if (-not $appText.Contains('PerformanceBenchmarks.Run(args)')) {
    $appText = $appText.Replace('Directory.CreateDirectory(Settings.DataDirectory);', "Directory.CreateDirectory(Settings.DataDirectory);`n        if (args.Contains(`"--benchmark`")) return PerformanceBenchmarks.Run(args);")
    [IO.File]::WriteAllText($appPath, $appText, [Text.UTF8Encoding]::new($false))
}
$env:DOTNET_CLI_HOME = Join-Path $root '.build-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$before = Join-Path $root '.verify/perf-before'
$after = Join-Path $root '.verify/perf-after'
dotnet build (Join-Path $source 'AIUsageWidget.csproj') -c Release -o $before --nologo
if ($LASTEXITCODE -ne 0) { throw 'Baseline build failed.' }
dotnet build (Join-Path $root 'AIUsageWidget.csproj') -c Release -o $after --nologo
if ($LASTEXITCODE -ne 0) { throw 'Candidate build failed.' }
$fixture = Join-Path $root 'data/perf-fixture'
foreach ($run in 1..3) {
    $order = if ($run % 2 -eq 1) { @('before', 'after') } else { @('after', 'before') }
    foreach ($label in $order) {
        $binary = if ($label -eq 'before') { $before } else { $after }
        $output = dotnet (Join-Path $binary 'AIUsageWidget.dll') --benchmark $fixture
        if ($LASTEXITCODE -ne 0) { throw "Benchmark $label failed." }
        $output | Set-Content -LiteralPath (Join-Path $evidence "$label-$run.json") -Encoding utf8
    }
    $a = Get-Content -Raw -LiteralPath (Join-Path $evidence "before-$run.json") | ConvertFrom-Json
    $b = Get-Content -Raw -LiteralPath (Join-Path $evidence "after-$run.json") | ConvertFrom-Json
    if ($a.Requests -ne 32000 -or $b.Requests -ne 32000 -or $a.FixtureBytes -ne $b.FixtureBytes) { throw 'Unexpected fixture or response count.' }
    if ($a.AnalyticsHash -ne $b.AnalyticsHash -or ($a.MascotPixelHashes -join ',') -ne ($b.MascotPixelHashes -join ',')) {
        throw 'Behavior or pixel mismatch.'
    }
    Write-Output "Pair $run passed: identical statistics and 64 identical mascot frames."
}
