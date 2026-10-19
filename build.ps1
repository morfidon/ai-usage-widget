$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/Prerequisites.ps1')
Assert-WidgetPlatform
Assert-WidgetWritableDirectory (Join-Path $PSScriptRoot 'dist')
$dotnet = Assert-WidgetDotNet $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location -LiteralPath $PSScriptRoot
try {
    # Resolve global.json from this checkout even when invoked from another directory.
    & $dotnet build (Join-Path $PSScriptRoot 'AIUsageWidget.csproj') -c Release -o (Join-Path $PSScriptRoot 'dist') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet (Join-Path $PSScriptRoot 'dist/AIUsageWidget.dll') --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed.' }
} finally { Pop-Location }
