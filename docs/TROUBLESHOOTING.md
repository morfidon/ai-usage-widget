# Troubleshooting setup

Start with the message from `setup.ps1`, then check the matching row. Account login,
application installation and window visibility are different checks; fix the one that failed.

| Symptom | What to do |
|---|---|
| Windows/architecture check fails | Run on native Windows 10/11 x64 in 64-bit PowerShell. WPF cannot run inside WSL/Linux/macOS. ARM64/x86 are not validated. |
| No .NET SDK, incompatible SDK, or missing Desktop Runtime | Rerun with `-InstallPrerequisites`. Alternatively install the **Windows x64 .NET 9 SDK** from Microsoft's download page and reopen PowerShell. Having only .NET 10 or the non-desktop runtime is insufficient. |
| NuGet restore reports missing targeting packs | This project disables package sources. Install the full Windows x64 .NET 9 SDK, or use the verified private SDK option. Do not add arbitrary package feeds. |
| No supported clients detected | Select only the providers you use with `-Providers "Codex,Claude"`. Install/login to those clients; automatic detection is intentionally limited to known native locations. |
| Codex executable not found | Install the Windows client or set `CodexPath` to its native Windows `codex.exe`. A `.cmd` shim or WSL installation is insufficient. See [provider overrides](PROVIDERS.md). |
| Claude login required | Sign in through native Claude Code. Check that setup and the widget use the same `CLAUDE_CONFIG_DIR`/`ClaudeDirectory`. Never send credentials to the setup agent. |
| Claude quotas are unavailable or HTTP 429 occurs | Wait for the existing backoff. If you enabled the bridge, a normal user-initiated Claude response can update it. Do not repeatedly refresh or generate paid responses to force quota data. |
| Previous Claude status line is missing | Check the backup in the widget data directory. Use the installed `uninstall.ps1 -OnlyClaudeBridge` to restore it if the widget still owns that setting. Git Bash restrictions can prevent the original command from running; test in a normal Windows shell and inspect the existing command's shell requirements. |
| JetBrains Central is missing | Select `JetBrains Air` in setup to obtain the verified pinned CLI. Then run the installed `Connect JetBrains Air.cmd`. Pick the same credit source as Air. |
| Antigravity shows `app_required` | Launch Antigravity, sign in, and keep it running. The widget needs its local language server. A provider update may change this internal interface. |
| Devin login required | Open Devin Desktop and sign in. A missing local account database is not a reason to copy one from another machine. |
| Download checksum mismatch | Stop and keep the existing installation. Retry the official download once; if it persists, report the version and error. Do not remove or bypass verification. |
| Access denied to a directory | Use a folder owned by the current user. Avoid Program Files, protected folders and read-only shares. Default setup uses local per-user app data. |
| Executable is in use during build/install | Exit **AI Usage Widget** through its tray menu, then rerun. For development verification, use `build.ps1 -OutputDirectory .verify/check/dist`. Do not kill provider processes. |
| Installation directory is non-empty without a marker | Choose an empty directory or the existing widget's marked directory. The installer refuses to overwrite arbitrary folders. |
| Existing layout uses another data directory | Reuse its original `-DataDirectory`. `install-layout.json` in the install root records that location. Back up data before intentionally migrating elsewhere. |
| Widget process runs but no panel appears | Open **AI Usage Widget** from Start or run the installed EXE again. An existing instance receives a show request. Check the fish icon in the tray/overflow area; the app intentionally has no taskbar button. |
| Setup says window verification failed | Review `window-check.json`, `preview.png` and `errors.log` in the data directory. Run from an interactive Windows desktop. A process alone does not prove a visible window. |
| Autostart is off | It is opt-in: rerun setup with `-Autostart Enable`, or use the widget menu. Verify it after an actual Windows sign-out/sign-in; a Run entry alone is insufficient evidence. |
| Session monitoring fails but quotas work | Provider history schemas or CLI commands may have changed. Keep quota access working and report the client version plus the sanitized error. Do not dump conversation databases. |
| Git Bash fails only inside a restricted agent sandbox | Run `tests/test_bridge.ps1` in an ordinary Windows shell with authorization. This can be an environment restriction rather than a widget defect; do not change security settings to bypass it. |

## Useful commands

```powershell
# Check prerequisite selection only; no download, install or account reads:
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -CheckOnly -Providers "Codex,Claude"

# Read accounts for the installed widget, then inspect the sanitized report:
& "$env:LOCALAPPDATA\Programs\AIUsageWidget\bin\AIUsageWidget.exe" --doctor
Get-Content "$env:LOCALAPPDATA\AIUsageWidget\data\setup-diagnostic.json"

# Undo only an owned Claude bridge, preserving autostart and the shortcut:
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\AIUsageWidget\uninstall.ps1" -OnlyClaudeBridge
```

GUI EXE diagnostics write their report to disk; use a .NET 9 host with the DLL if
you need synchronous terminal output/exit codes. Private SDK installs include a
host at `%LOCALAPPDATA%\AIUsageWidget\dotnet\dotnet.exe`.

`--doctor` exits 0 for ready/unselected providers, 3 for pending login/running app,
and 2 for a missing dependency or failed/empty quota read. A connected account with
no current exposed quotas is not reported as ready. `--diagnose` is the older raw
quota report and uses exit 2 for unsuccessful selected reads; setup uses `--doctor`.

If reporting a problem, include Windows architecture, .NET SDK version, provider
client versions, exact command and the sanitized setup error. Review screenshots
and filenames before sharing them. Do not attach the whole data directory or a
Claude settings backup: it can contain private configuration.

## Rollback and removal

Run the installed `uninstall.ps1` before removing application files. It removes the
owned startup entry and shortcut, and restores the backed-up Claude status line
only when the widget still owns that setting. Later user changes survive. Repeating
uninstall is safe. `-KeepClaudeBridge` deliberately leaves that integration in place;
do not then delete its script.

The script keeps settings, history, binaries and the optional private SDK/runtime.
After exiting the widget, you may remove those exact app-owned directories yourself.
Other provider applications, logins and JetBrains Central account data are not removed.
Never delete `%LOCALAPPDATA%` or a shared parent directory.
