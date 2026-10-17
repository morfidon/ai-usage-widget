$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build (Join-Path $PSScriptRoot 'AIUsageWidget.csproj') -c Release -o (Join-Path $PSScriptRoot 'dist') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Kompilacja nie powiodla sie.' }
dotnet (Join-Path $PSScriptRoot 'dist/AIUsageWidget.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Testy nie powiodly sie.' }
