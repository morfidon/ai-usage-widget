param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Prerequisites.ps1')
Assert-WidgetPlatform
$dotnet = Assert-WidgetDotNet $root
$appBuild = Join-Path $root '.verify/readme-app/dist'
if (-not $SkipBuild) { & (Join-Path $root 'build.ps1') -OutputDirectory $appBuild }
if (-not (Test-Path -LiteralPath (Join-Path $appBuild 'AIUsageWidget.dll'))) { throw 'Missing documentation build. Run without -SkipBuild.' }
$work = Join-Path $root '.verify/readme-export'
$rendered = Join-Path $work 'rendered'
[IO.Directory]::CreateDirectory($work) | Out-Null
$reference = [Security.SecurityElement]::Escape((Join-Path $appBuild 'AIUsageWidget.dll'))
$assets = [Security.SecurityElement]::Escape((Join-Path $root 'assets/*.svg'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF><UseWindowsForms>true</UseWindowsForms><Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="AIUsageWidget"><HintPath>$reference</HintPath></Reference>
    <Resource Include="$assets" Link="assets/%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $work 'ReadmeMedia.csproj'), $project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'readme-media/Program.cs.template') -Destination (Join-Path $work 'Program.cs') -Force
$env:DOTNET_CLI_HOME = Join-Path $root '.build-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location -LiteralPath $root
try {
    & $dotnet build (Join-Path $work 'ReadmeMedia.csproj') -c Release -o (Join-Path $work 'dist') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Documentation exporter build failed.' }
    & $dotnet (Join-Path $work 'dist/ReadmeMedia.dll') $rendered
    if ($LASTEXITCODE -ne 0) { throw 'Documentation capture failed.' }
} finally { Pop-Location }
$media = Join-Path $root 'docs/media'
[IO.Directory]::CreateDirectory($media) | Out-Null
foreach ($name in @('overview.png','widget.png','one-provider.png','history.png','projects.png','statistics.png')) {
    Copy-Item -LiteralPath (Join-Path $rendered $name) -Destination (Join-Path $media $name) -Force
}
$ffmpeg = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
if (-not $ffmpeg) { throw 'PNGs were exported. Install FFmpeg to encode the documentation GIF; it is not needed by the widget.' }
& $ffmpeg.Source -hide_banner -loglevel error -y -framerate 12 -i (Join-Path $rendered 'frames/frame-%03d.png') `
    -filter_complex '[0:v]split[a][b];[a]palettegen=max_colors=192:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle' `
    -frames:v 132 -loop 0 (Join-Path $media 'demo.gif')
if ($LASTEXITCODE -ne 0) { throw 'GIF encoding failed.' }
Write-Output "README media exported to $media. No provider accounts were queried."
