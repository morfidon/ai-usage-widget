# Connect your accounts

You only need the providers you actually use. Select them with
`setup.ps1 -Providers "Codex,Claude"`, for example. Missing unselected providers
are reported as `not_selected`; they do not make setup fail.

You can change the selection live in **••• → Dostawcy**, or by right-clicking the tray
icon and opening **Dostawcy**. Uncheck any provider you do not want, regardless of
whether it is installed. Hidden providers stop quota/session reads and live transcript
indexing; their saved quota history is retained. Any read already running is cancelled
when the selection changes.

**Wybierz wykryte aplikacje** replaces your current selection with detected native
clients; it does not check login or make account requests. Automatic selection runs
only for a new configuration. Later launches keep your saved choices. Connection
errors, expired logins and a closed Antigravity app do not silently hide selected cards.

No clients yet? Setup can still install the widget. Use `-Providers None` to explicitly
start with no cards, then select providers from the menu when you are ready.

| Provider | What you need | User login action | How the widget reads limits | Expected result |
|---|---|---|---|---|
| Codex | Codex for Windows or a native `codex.exe` supporting `app-server` | Open Codex and sign in to your subscription account. | Locates `%LOCALAPPDATA%\OpenAI\Codex\bin\**\codex.exe`, then native executables on PATH; uses `account/rateLimits/read`. | `ready`; only the quota windows the account actually returns appear. |
| Claude | Native Claude Code CLI, signed in | Open Claude Code and complete its login flow. If authorization cannot be renewed, log in again in Claude Code. | Existing Claude Code credentials; optional official status line data, plus the internal usage endpoint. | `ready`; if the usage endpoint is unavailable or throttled, the bridge can supply quotas after a normal user-initiated response. |
| Devin | Devin Desktop with a signed-in account | Open Devin Desktop and sign in. | Reads the local account database, then requests account quotas from the supported provider server. | `ready`; daily/weekly windows depend on the subscription. |
| JetBrains Air | A JetBrains AI account with credits; setup downloads pinned Central 1.11.0 | Run the installed `Connect JetBrains Air.cmd`, approve browser login, and choose the same credit source you use in Air. | Only `central limit --json`. It does not wire agents or start a proxy. | `ready`; monthly and additional credits remain separate. |
| Antigravity | Antigravity installed and running | Open Antigravity, sign in to Google, and leave it running. | Discovers its local language server and makes quota-only loopback requests. | `ready`; `app_required` means the local server could not be found. |

Automatic detection is a convenience, not proof that an account is ready. If a client
uses a custom location, select it explicitly and use the supported settings below.
No API key is needed for the widget itself. Subscription access and client compatibility
still depend on the provider. These instructions do not promise every account exposes quotas.

## Paths and overrides

Edit the installed data directory's `settings.json` only while the widget is closed.
Preserve existing fields and add just what you need:

```json
{
  "EnabledProviders": ["Codex", "Claude"],
  "CodexPath": "C:\\path\\to\\native\\codex.exe",
  "ClaudeDirectory": "C:\\path\\to\\claude-config"
}
```

`CodexPath` must point to the native executable, not `codex.cmd`, a shell script,
or a WSL executable. If your npm installation exposes only a wrapper, locate its
native Windows executable or install the Windows desktop client.

`CODEX_HOME` controls the Codex history/session location. `CLAUDE_CONFIG_DIR` controls
the default Claude configuration location, and `ClaudeDirectory` overrides it for
runtime reads. Set `CLAUDE_CONFIG_DIR` before enabling the bridge so the installer
modifies the same account directory. Do not switch config directories while the old
bridge remains installed; undo that bridge first with `uninstall.ps1 -OnlyClaudeBridge`.
The native Claude CLI must be in `%USERPROFILE%\.local\bin` or on PATH.

## Finish after a login handoff

1. Complete the login in the provider's own application or browser. Never paste its
   tokens, cookies or credential files into an AI chat.
2. For the default installation, run:

   ```powershell
   & "$env:LOCALAPPDATA\Programs\AIUsageWidget\bin\AIUsageWidget.exe" --doctor
   Get-Content "$env:LOCALAPPDATA\AIUsageWidget\data\setup-diagnostic.json"
   ```

3. `ready` means a current quota read succeeded. `login_required` and `app_required`
   require the actions above. `not_installed` needs the missing client/CLI. `error`
   needs investigation; do not repeatedly reset credentials to force a green result.
4. Open **AI Usage Widget** from Start and confirm its window and tray icon. `--doctor`
   verifies provider access; it does not prove the window is visible or startup works.

An AI agent should keep a required login handoff pending until the user responds.
After the user finishes, rerun the account check and report any remaining blocker.
No model inference is needed to test quotas. Do not create a Claude response merely
to populate the bridge; use existing data or let the user's next normal response do it.

## Compatibility and downloads

Codex uses an official app-server method. Claude's status line is official, while its
idle usage endpoint and the Devin/Antigravity client interfaces are internal and can
change after updates. Codex session history also uses internal local database schemas.
The quota check can work even when session monitoring is unavailable.

Central downloads use a repository-pinned SHA-256 captured from JetBrains' official
HTTPS archive, not a vendor-published signature. Version/hash changes require review.
The optional .NET installer uses SHA-512 from Microsoft's HTTPS release metadata;
the stable .NET 9 patch may advance. Neither installer executes downloaded shell scripts.

References: [Codex app server](https://developers.openai.com/codex/app-server),
[Claude status line](https://code.claude.com/docs/en/statusline),
[JetBrains Central login](https://www.jetbrains.com/help/central-cli/cli-setup.html),
[.NET runtime discovery](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#apphostdotnetsearch).
