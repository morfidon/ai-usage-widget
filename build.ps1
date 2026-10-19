param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/Prerequisites.ps1')
Assert-WidgetPlatform
Assert-WidgetWritableDirectory $OutputDirectory
$appHost = Join-Path $OutputDirectory 'AIUsageWidget.exe'
if (Test-Path -LiteralPath $appHost) {
    try { $probe = [IO.File]::Open($appHost, 'Open', 'ReadWrite', 'None'); $probe.Dispose() }
    catch { throw 'The output executable is in use. Exit the widget through its tray menu, or build with -OutputDirectory pointing to a separate folder.' }
}
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
    & $dotnet build (Join-Path $PSScriptRoot 'AIUsageWidget.csproj') -c Release -o $OutputDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet (Join-Path $OutputDirectory 'AIUsageWidget.dll') --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed.' }
} finally { Pop-Location }
