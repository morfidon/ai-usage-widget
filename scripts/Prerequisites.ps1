# Shared by build, setup and tests. Compatible with Windows PowerShell 5.1.
function Assert-WidgetPlatform {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or [Environment]::OSVersion.Version.Major -lt 10) {
        throw 'AI Usage Widget requires native Windows 10/11. WSL, Linux and macOS cannot run the WPF widget. Run setup in Windows PowerShell.'
    }
    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess -or
        $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
        throw 'This release supports Windows x64 in a 64-bit PowerShell process. ARM64 and x86 have not been validated.'
    }
}

function Assert-WidgetWritableDirectory([string]$Path) {
    [IO.Directory]::CreateDirectory($Path) | Out-Null
    $probe = Join-Path $Path ('.write-check-' + [Guid]::NewGuid().ToString('N'))
    try { [IO.File]::WriteAllText($probe, '') }
    catch { throw "Directory is not writable: $Path. Choose a directory owned by your Windows user." }
    finally { if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe } }
}

function Find-WidgetDotNet {
    $private = Join-Path $env:LOCALAPPDATA 'AIUsageWidget/dotnet/dotnet.exe'
    if (Test-Path -LiteralPath $private) { return $private }
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

function Assert-WidgetDotNet([string]$Root) {
    $exe = Find-WidgetDotNet
    if (-not $exe) { throw 'Missing .NET 9 SDK. Run setup.ps1 -InstallPrerequisites or install the Windows x64 .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0 and reopen PowerShell.' }
    Push-Location -LiteralPath $Root
    try {
        $selected = & $exe --version 2>$null | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0 -or $selected -notmatch '^9\.0\.\d+$') {
            throw 'A stable .NET 9 SDK is required by global.json. Run setup.ps1 -InstallPrerequisites, or install the Windows x64 .NET 9 SDK.'
        }
        $runtimes = & $exe --list-runtimes
        if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft.WindowsDesktop.App 9\.0\.')) {
            throw 'Missing .NET Desktop Runtime 9 x64. Run setup.ps1 -InstallPrerequisites or install it from https://dotnet.microsoft.com/download/dotnet/9.0.'
        }
    } finally { Pop-Location }
    return $exe
}
