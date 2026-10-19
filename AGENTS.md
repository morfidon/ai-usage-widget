# AI Usage Widget: setup and development

This is a native **Windows x64** WPF tray application targeting .NET 9. Linux,
WSL, macOS, ARM64 and x86 are not supported by this setup workflow. A remote
Linux coding agent cannot install or verify the Windows UI; use a native Windows
agent. Runtime use does not require Python, Node, or NuGet packages.

## Setting up for a user

1. Read the README quick start and `docs/PROVIDERS.md`. Run from a writable checkout
   in 64-bit Windows PowerShell 5.1 or PowerShell 7. Quote paths containing spaces.
2. Run the read-only preflight:
   `powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -CheckOnly`
   If automatic detection misses a client, repeat with `-Providers Codex` or
   `-Providers "Codex,Claude"`. Never install every provider merely to make checks green.
3. Use `setup.ps1` as the entry point. It builds, self-tests, checks the Claude
   bridge fixture, installs, diagnoses selected providers, launches and verifies
   the window. Exit the running widget through its tray menu before updating it.
4. Missing SDK/runtime: explain the download and use `-InstallPrerequisites`
   when authorized to install dependencies. It downloads the Microsoft Windows
   x64 SDK with SHA-512 verification into the current user's app data; it does
   not request elevation or change global PATH. No paid tools or subscriptions
   may be purchased as part of setup.
5. Fresh installs do not enable autostart or change Claude settings by default.
   `-Autostart Enable` writes the current user's Run entry. `-ClaudeBridge Enable`
   changes the selected user's Claude status line, retaining a backup and invoking
   the previous command. Explain these effects and honor the user's choices.
   `Keep` preserves an existing choice; `Disable` removes an owned integration.
6. Authentication belongs to the user. Never ask for passwords, tokens, cookies,
   credential-file contents or account database dumps. Do not copy an account
   from the development machine. Give the exact provider login action and wait
   when user interaction is needed; continue independent checks in the meantime.
7. Check `setup-result.json` in the data directory. Exit **0** means selected quotas
   passed and the requested window launch was verified. Exit **3** means installation
   completed but login or a running provider app is needed. Exit **2** means an error.
   `-NoLaunch` explicitly skips visibility verification; say so in the final report.
   `--doctor` alone checks provider readiness and does not verify installation or UI.

## Paths and recovery

- App: `%LOCALAPPDATA%\Programs\AIUsageWidget\bin\AIUsageWidget.exe`.
- Data: `%LOCALAPPDATA%\AIUsageWidget\data`; the install marker records custom paths.
- Start Menu: **AI Usage Widget**. The app uses a tray icon and is absent from the taskbar.
- Uninstall integrations: run the installed root's `uninstall.ps1`. It restores
  only integrations still owned by the widget; later user changes and data survive.
- The old checkout's known widget data is copied on first migration, never removed.
  Do not delete old files until migration has been verified. Never overwrite the
  original Claude status line backup on an ordinary reinstall.
- See `docs/TROUBLESHOOTING.md` for errors. Do not bypass checksum mismatches,
  repeatedly hammer rate-limited endpoints, or kill provider applications to force setup.

## Verification and change constraints

- `build.ps1` publishes a framework-dependent executable and runs offline self-tests.
  `-OutputDirectory .verify/check/dist` avoids overwriting an in-use development build.
- Run `tests/test_bridge.ps1` for bridge changes and `tests/test_install.ps1` for
  installer/layout changes. The latter uses fixture-only Claude settings, shortcuts
  and a unique registry key. Never test lifecycle scripts against a user's live settings.
- `dotnet .\dist\AIUsageWidget.dll --visual-test` uses synthetic data. Check its PNGs
  for UI changes. `--doctor` performs real quota reads; do not confuse it with an offline test.
- Preserve order: Codex, Claude, Devin, JetBrains Air, Antigravity. Legacy settings
  without `EnabledProviders` retain all five; new setup stores the selected subset.
- Keep notifications, keyboard behavior, quota meaning (remaining percentage), real
  reset times, stale-data handling and existing user settings intact unless requested.
- No model prompts, inference, new conversations, agent wiring or proxy startup for
  quota checks. Session navigation may resume only the selected existing conversation.
- Never persist credentials or conversation text. Keep quota history distinct from
  local token estimates. Disabled providers must not be polled or have transcripts indexed.
- Private provider interfaces can drift; preserve explicit failure states instead of
  inventing quotas. Review `docs/PRIVACY.md` before changing data access or diagnostics.
- Make focused commits. Do not push, publish a release or change external content
  without authorization. Before publication run `scripts/audit-publication.ps1`.
