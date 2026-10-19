using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIUsageWidget;

public sealed record SetupCheck(string Provider, string Status, string Action);

public static class SetupDiagnostics
{
    public static readonly string[] Providers = { "Codex", "Claude", "Devin", "JetBrains Air", "Antigravity" };
    public static SetupCheck Classify(string name, bool selected, ProviderState? state)
    {
        if (!selected) return new(name, "not_selected", "No action needed.");
        if (state is { Connected: true, Windows.Count: > 0 } && state.Windows.Any(w => !w.Expired))
            return new(name, "ready", "Quota read succeeded.");
        string status = state?.SetupStatus ?? "error";
        return new(name, status, status switch {
            "not_installed" => name == "Codex" ? "Install Codex for Windows or set CodexPath to its native codex.exe."
                : "Run setup.ps1 with JetBrains Air selected to install the pinned Central CLI.",
            "login_required" => name == "JetBrains Air" ? "Run the installed Connect JetBrains Air.cmd and complete browser login."
                : $"Open {name}{(name == "Claude" ? " Code" : "")} and sign in, then rerun --doctor.",
            "app_required" => "Start Antigravity, sign in to Google, and leave the application running.",
            _ => "Quota read failed or returned no current limits. Check the provider app, network and docs/TROUBLESHOOTING.md; do not repeatedly poll or reset credentials."
        });
    }
    public static int ExitCode(SetupCheck[] checks) => checks.Any(c => c.Status is "error" or "not_installed") ? 2
        : checks.Any(c => c.Status is "login_required" or "app_required") ? 3 : 0;

    public static async Task<int> Run(string[] args)
    {
        var settings = Settings.Load();
        int index = Array.IndexOf(args, "--providers");
        if (index >= 0)
        {
            if (index + 1 >= args.Length) { Console.Error.WriteLine("--providers requires comma-separated provider names."); return 2; }
            var selected = args[index + 1].Split(',').Select(s => s.Trim()).ToArray();
            if (selected.Any(s => !Providers.Contains(s, StringComparer.OrdinalIgnoreCase))) {
                Console.Error.WriteLine("Unknown provider. Valid names: " + string.Join(", ", Providers)); return 2;
            }
            settings.EnabledProviders = selected;
        }
        using var codex = new CodexProvider(settings);
        using var claude = new ClaudeProvider(settings);
        using var devin = new DevinProvider();
        using var air = new AirProvider();
        using var antigravity = new AntigravityProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        async Task<SetupCheck> Check(string name)
        {
            if (!settings.IsEnabled(name)) return Classify(name, false, null);
            if (name == "Claude" && LocalPaths.ClaudeExe() is null)
                return new(name, "not_installed", "Install the native Claude Code CLI, sign in, and rerun --doctor.");
            try {
                ProviderState? state = name switch {
                    "Codex" => await codex.Fetch(timeout.Token), "Claude" => await claude.Fetch(timeout.Token),
                    "Devin" => await devin.Fetch(timeout.Token), "JetBrains Air" => await air.Fetch(timeout.Token),
                    _ => await antigravity.Fetch(timeout.Token)
                };
                return Classify(name, true, state);
            } catch { return Classify(name, true, null); }
        }
        var checks = await Task.WhenAll(Providers.Select(Check));
        int exit = ExitCode(checks);
        var result = new { SchemaVersion = 1, ExitCode = exit, Checks = checks };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(Settings.DataDirectory, "setup-diagnostic.json"), json);
        Console.WriteLine(json);
        return exit;
    }
}
