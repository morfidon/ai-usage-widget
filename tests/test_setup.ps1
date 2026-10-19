param([string]$BuildDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $root ('.verify/setup-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $fixture 'data')) | Out-Null
$serverPath = Join-Path $fixture 'fixture-codex.exe'
# A synthetic stdio app-server; no logins, real provider processes or network requests.
Add-Type -OutputAssembly $serverPath -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.Text.RegularExpressions;
public class FixtureCodex {
    public static void Main(string[] args) {
        string line;
        while ((line = Console.ReadLine()) != null) {
            var id = Regex.Match(line, "\"id\":([0-9]+)");
            if (!id.Success) continue;
            if (line.Contains("account/rateLimits/read") && System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "login-required"))) {
                Console.WriteLine("{\"id\":" + id.Groups[1].Value + ",\"error\":{\"message\":\"auth required\"}}");
                continue;
            }
            string result = "{}";
            if (line.Contains("account/rateLimits/read")) {
                result = "{\"rateLimits\":{\"planType\":\"fixture\",\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":1999999999}}}";
            }
            Console.WriteLine("{\"id\":" + id.Groups[1].Value + ",\"result\":" + result + "}");
        }
    }
}
'@
$settingsPath = Join-Path $fixture 'data/settings.json'
[IO.File]::WriteAllText($settingsPath, (@{ CodexPath = $serverPath; Compact = $true; EnabledProviders = @('Codex') } | ConvertTo-Json))
$options = @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root 'setup.ps1'),
    '-Providers','Codex','-NoLaunch','-SkipLegacyMigration','-BuildDirectory',$BuildDirectory,
    '-InstallDirectory',(Join-Path $fixture 'app'),'-DataDirectory',(Join-Path $fixture 'data'),
    '-ShortcutDirectory',(Join-Path $fixture 'shortcuts'),'-RunKeyPath',('HKCU:\Software\AIUsageWidget-SetupTests-' + [Guid]::NewGuid().ToString('N')))
& powershell.exe @options
if ($LASTEXITCODE -ne 0) { throw "Synthetic full setup failed. Fixture: $fixture" }
$result = Get-Content -LiteralPath (Join-Path $fixture 'data/setup-result.json') -Raw | ConvertFrom-Json
if (-not $result.Installed -or -not $result.LaunchSkipped -or $result.WindowVerified -or $result.ExitCode -ne 0 -or
    ($result.Checks | Where-Object { $_.Provider -eq 'Codex' }).Status -ne 'ready') { throw 'Incorrect setup result.' }
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if (-not $settings.Compact -or $settings.CodexPath -ne $serverPath -or $settings.EnabledProviders.Count -ne 1) { throw 'Setup lost existing preferences.' }
# Omit explicit providers on repeat to verify that saved selection is reused.
$repeatOptions = @(); $providerOption = [Array]::IndexOf($options, '-Providers')
for ($i = 0; $i -lt $options.Count; $i++) { if ($i -ne $providerOption -and $i -ne ($providerOption + 1)) { $repeatOptions += $options[$i] } }
& powershell.exe @repeatOptions
if ($LASTEXITCODE -ne 0) { throw 'Repeated setup failed.' }
[IO.File]::WriteAllText((Join-Path $fixture 'login-required'), '')
& powershell.exe @repeatOptions
if ($LASTEXITCODE -ne 3) { throw 'Pending login must exit 3, not pretend success or installation failure.' }
$pending = Get-Content -LiteralPath (Join-Path $fixture 'data/setup-result.json') -Raw | ConvertFrom-Json
if (-not $pending.Installed -or ($pending.Checks | Where-Object { $_.Provider -eq 'Codex' }).Status -ne 'login_required') { throw 'Login handoff result was not recorded.' }
& (Join-Path $fixture 'app/uninstall.ps1')
Write-Output "PASS End-to-end setup, saved selection, pending login handoff and preserved preferences; UI explicitly skipped. Fixture: $fixture"
