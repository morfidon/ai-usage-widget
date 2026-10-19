using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AIUsageWidget;

// Installation evidence only. Never probes accounts or treats a network error as an uninstall.
public static class ProviderDiscovery
{
    public static string[] Detect(Settings settings, string? user = null, string? local = null, string? roaming = null,
        string? searchPath = null, string? project = null)
    {
        user ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        local ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        roaming ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        searchPath ??= Environment.GetEnvironmentVariable("PATH") ?? "";
        project ??= Settings.ProjectDirectory;
        bool OnPath(string name) => searchPath.Split(Path.PathSeparator).Any(d => File.Exists(Path.Combine(d.Trim('"'), name)));
        bool CodexInstalled()
        {
            if (File.Exists(settings.CodexPath) || OnPath("codex.exe")) return true;
            try {
                string root = Path.Combine(local, "OpenAI", "Codex", "bin");
                return Directory.Exists(root) && Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories).Any();
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }
        var found = new HashSet<string>();
        if (CodexInstalled()) found.Add("Codex");
        if (File.Exists(Path.Combine(user, ".local", "bin", "claude.exe")) || OnPath("claude.exe")) found.Add("Claude");
        if (File.Exists(Path.Combine(roaming, "devin", "User", "globalStorage", "state.vscdb")) ||
            File.Exists(Path.Combine(local, "Programs", "Devin", "Devin.exe"))) found.Add("Devin");
        if (File.Exists(Path.Combine(project, "tools", "central", "central.exe")) ||
            File.Exists(Path.Combine(user, ".local", "bin", "central.exe")) || OnPath("central.exe")) found.Add("JetBrains Air");
        if (Directory.Exists(Path.Combine(local, "Programs", "Antigravity"))) found.Add("Antigravity");
        return SetupDiagnostics.Providers.Where(found.Contains).ToArray();
    }
}
