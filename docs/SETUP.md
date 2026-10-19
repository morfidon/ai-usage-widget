# Setup, updates and verification

[Back to the README](../README.md#install-with-codex). Run the commands below from the source checkout.

You need **native Windows 10/11 x64** and a writable checkout. A signed-in provider
app is needed to show its limits; you can prepare the widget before installing any clients.
Use 64-bit Windows PowerShell 5.1 or PowerShell 7. WSL/Linux/macOS,
Windows ARM64 and x86 are outside the supported setup path. The widget starts in
English. Select **••• → Language / Język → Polski** for Polish; the choice applies
immediately and is saved in `settings.json` as `Language` (`en` or `pl`). Existing
settings without a language also default to English. Other settings are preserved.

Clone this repository (or download and extract its ZIP), then open PowerShell in
that directory. Git is needed only if you choose to clone it. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -InstallPrerequisites
```

Setup detects installed clients, obtains .NET 9 if needed, builds, runs offline tests,
installs for the current user, checks the selected accounts, and opens the widget.
The first download may take several minutes. No administrator access is needed.
Python, Node and NuGet packages are not required to build or run this app.

Prefer to select the accounts yourself? This works both when pasted into PowerShell
and when an AI agent invokes `powershell.exe -File`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -InstallPrerequisites -Providers "Codex,Claude"
```

Autostart and the optional Claude status line bridge stay unchanged on an existing
installation and are **off on a fresh installation**. To enable them explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -Providers "Codex,Claude" -Autostart Enable -ClaudeBridge Enable
```

The Claude bridge changes your Claude Code status line, keeps a backup, and invokes
your previous status line command. Autostart adds a current-user Windows Run entry.
Both options also accept `Disable`; the default is `Keep`. Provider selection controls
quota polling, session monitoring and local transcript indexing. Later setup runs
reuse saved provider selection unless you pass `-Providers` again. You can also change
providers at any time in **••• → Providers** or the tray icon menu, without restarting.
Uncheck accounts you do not want to monitor, even when their apps are installed.
The first standalone launch selects detected clients; existing saved choices are respected.
A temporary connection/login problem keeps a selected card visible with its status.
If no clients are detected or all are unchecked, the widget shows a short chooser
instruction. You can explicitly prepare an empty install with `-Providers None`.

| Result | Meaning | Next step |
|---|---|---|
| Exit 0 | Selected quotas passed; the window was verified if launch was requested. | Start using the widget. |
| Exit 3 | Installation finished; a selected account needs login or its app must run. | Follow `Checks[].Action`, then rerun the account check. |
| Exit 2 | A prerequisite, installation, connection, or window check failed. | Read the error and [troubleshooting](TROUBLESHOOTING.md). |

Setup saves `setup-result.json` in `%LOCALAPPDATA%\AIUsageWidget\data`. With `-NoLaunch`,
window verification is skipped and explicitly recorded. `-CheckOnly` checks platform,
SDK/runtime and provider selection without building, downloading, installing or logging in.
It does not verify account access, write permissions or the visible UI.

## Where it installs and how to update

| Item | Default location |
|---|---|
| Executable | `%LOCALAPPDATA%\Programs\AIUsageWidget\bin\AIUsageWidget.exe` |
| Start Menu | **AI Usage Widget** |
| Settings, quota history and diagnostic summaries | `%LOCALAPPDATA%\AIUsageWidget\data` |
| Optional private .NET SDK | `%LOCALAPPDATA%\AIUsageWidget\dotnet` |
| Installed optional private runtime | `%LOCALAPPDATA%\Programs\AIUsageWidget\dotnet` |

You can move or remove the source checkout after a successful install. To update,
exit the widget from its tray menu, obtain the new source, and rerun setup. Existing
settings, provider selection and history are kept. Old checkout-based installations
migrate known widget data without deleting the originals. Custom install/data locations
are supported with `-InstallDirectory` and `-DataDirectory`; reuse the same paths on updates.

To undo owned autostart, Start Menu and Claude integrations (files and data are retained):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\AIUsageWidget\uninstall.ps1"
```

To check accounts after login:

```powershell
& "$env:LOCALAPPDATA\Programs\AIUsageWidget\bin\AIUsageWidget.exe" --doctor
```

`--doctor` writes `setup-diagnostic.json` in the data directory. For terminal JSON,
run `dotnet <installed-path>\AIUsageWidget.dll --doctor` with a .NET 9 host. If setup
installed a private SDK, its host is `%LOCALAPPDATA%\AIUsageWidget\dotnet\dotnet.exe`.
Diagnostics accept `--providers "Codex,Claude"` for a one-off selection without saving it.

Read [provider login instructions](PROVIDERS.md), [troubleshooting](TROUBLESHOOTING.md),
[privacy and data access](PRIVACY.md), and the [MIT license](../LICENSE).

## Building and verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\test_bridge.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\test_install.ps1
dotnet .\dist\AIUsageWidget.dll --visual-test
```

`build.ps1` publishes a framework-dependent build and runs self-tests. `global.json`
selects a stable .NET 9 SDK, allowing later .NET 9 feature bands and patches, but not
a different major version or previews ([Microsoft's SDK policy](https://learn.microsoft.com/dotnet/core/tools/global-json)).
The project clears NuGet sources; required Windows desktop targeting packs must be
present in the SDK. To verify without replacing an in-use development executable, use
`build.ps1 -OutputDirectory .verify/check/dist`.

Windows CI runs these checks with synthetic data. Account login and actual post-logon
tray visibility require a Windows user session and are not proven by a green CI build.
See [performance evidence](../PERFORMANCE_2026-10-18.md) for the separate A/B measurements.
