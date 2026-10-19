# Privacy, local files and network access

The widget checks subscription quotas and reads local session/token metadata. It does
not send prompts, create AI tasks, or call a model to measure usage. Selecting fewer
providers also limits quota polling, session monitoring and transcript indexing.

| Source | What is accessed | Where requests go | What the widget stores |
|---|---|---|---|
| Codex | Native app-server account quota method; selected local history SQLite databases, writer locks and JSONL usage records under `CODEX_HOME` or `~/.codex` | Local app-server process; the official client handles account/server access. Public service status from `status.openai.com`. | Quota snapshots, reset times, UI preferences. Session/project/model token analysis stays in memory. |
| Claude | Quota-only status line payloads; `.credentials.json` in the selected Claude config directory; local project JSONL usage records and `claude agents --json` | `api.anthropic.com/api/oauth/usage`; credential renewal uses the installed Claude Code client without user/model messages. Public service status from `status.claude.com`. | Filtered quota cache and history. With bridge opt-in, a backup of the original status line and full Claude settings. |
| Devin | Read-only `AppData/Roaming/devin/User/globalStorage/state.vscdb`, plus local client version metadata | HTTPS account-status requests to supported `server.self-serve.windsurf.com` or `server.codeium.com` origins. No credential-bearing redirects are followed. | Quota snapshots and reset times; no API key is saved by the widget. |
| JetBrains Air | Runs `central limit --json`; Central manages its own encrypted account storage | The official Central CLI communicates with JetBrains. Widget does not configure agents or start a proxy. | Monthly/top-up balances and resets returned by Central; license holder identity is omitted. |
| Antigravity | Running local language-server discovery, including its local connection/CSRF token | Quota/status RPCs over `127.0.0.1`. The local token is held in memory, not written to widget data. | Grouped quotas, plan and reset times; no Google OAuth token or account identity is saved. |

Local JSONL analysis reads relevant records from conversation files in order to extract
usage, model and project metadata. Raw conversation text and tool arguments are not
copied into the statistics files. This is still local file access to potentially private
history, and project/session names may be displayed in the UI. Historical Codex session
titles can be read on demand for visible result pages. Token totals are local estimates,
not official billing or subscription percentages.

## Stored data and backups

The default data directory is `%LOCALAPPDATA%\AIUsageWidget\data`. Old development
checkouts use `data/` next to the source. A marked installed layout records custom paths.

- `settings.json`: selected providers, display preferences and optional local paths.
- Quota cache and `usage-history.json`: real observations and reset times. History
  retains approximately 31 days; no history is fabricated from token totals.
- `claude-statusline-backup.json` and `claude-settings-before-install.json`: created
  when the bridge is enabled. **The full settings backup can contain private paths,
  commands, environment configuration or secrets supplied by the user. Do not share it.**
- `install-state.json`: owned integration paths, executable and status line command.
- `setup-diagnostic.json`/`setup-result.json`: provider readiness and generic actions.
  Raw quota/activity diagnostic files can contain account plan/quota data and local
  application paths; review them before sharing.
- `preview.png`, visual test PNGs and `window-check.json`: screenshots and window
  verification. A real capture can reveal usage/account/project information visible
  on screen. Do not publish a data folder or screenshots without reviewing them.
- `errors.log`: exception type and time, without arbitrary exception messages.

Uninstalling integrations retains data and binaries. Deleting widget history does not
delete provider conversation history or sign out accounts. Provider applications and
Central retain ownership of their credentials and credential refresh.

## Setup downloads

The optional SDK bootstrap reads Microsoft's HTTPS .NET 9 release metadata, downloads
the referenced Windows x64 ZIP from `builds.dotnet.microsoft.com`, and verifies SHA-512.
Central 1.11.0 is downloaded from JetBrains' official S3 distribution endpoint and
checked against a repository-pinned SHA-256. This Central hash is a maintainer pin
captured from the official archive, not an independent vendor signature. Review a
version and checksum together before changing them.

SDK updates within .NET 9 are allowed by `global.json`; previews and other major
versions are excluded. A private SDK install does not register .NET system-wide or
change global PATH. Its runtime is copied into the installed app so startup can work
without an interactive shell's environment.

## Before making the repository public

Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\audit-publication.ps1`.
It checks tracked files and reachable commit history for common secret formats and
private/runtime filenames; output is limited to filenames and commit IDs. It is a
heuristic check, not proof that no confidential data exists. Review unusual credential
formats, screenshots, names, comments, Git author emails, and third-party asset rights
yourself. A `.gitignore` rule does not remove anything from existing history.

If a real credential is found, rotate it before publication. History cleanup requires
a separate deliberate decision; the audit never rewrites commits or deletes files.
Downloaded third-party software remains under its own license. The project's MIT
license does not grant ownership of provider trademarks or change provider terms.
