using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AIUsageWidget;

public static class FeatureTests
{
    public static int Run()
    {
        int n = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); n++; }
        string dir = Path.Combine(Settings.DataDirectory, "feature-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string discoveryRoot = Path.Combine(dir, "discovery");
            Directory.CreateDirectory(discoveryRoot);
            string[] Detect() => ProviderDiscovery.Detect(new Settings(), discoveryRoot, discoveryRoot, discoveryRoot, "", discoveryRoot);
            Check(Detect().Length == 0, "Fresh machine detection does not invent provider installations");
            string native = Path.Combine(discoveryRoot, ".local", "bin"); Directory.CreateDirectory(native);
            File.WriteAllText(Path.Combine(native, "claude.exe"), "fixture");
            string codexBin = Path.Combine(discoveryRoot, "OpenAI", "Codex", "bin", "version"); Directory.CreateDirectory(codexBin);
            File.WriteAllText(Path.Combine(codexBin, "codex.exe"), "fixture");
            Check(Detect().SequenceEqual(new[] { "Codex", "Claude" }), "Installed clients are detected in stable order without requiring account login");
            var toggled = new Settings(); toggled.SetProviderEnabled("Claude", false);
            Check(!toggled.IsEnabled("Claude") && toggled.IsEnabled("Codex"), "A manual hide preserves the other legacy providers");
            foreach (string name in SetupDiagnostics.Providers) toggled.SetProviderEnabled(name, false);
            var restoredSelection = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(toggled))!;
            Check(restoredSelection.EnabledProviders!.Length == 0 && !restoredSelection.IsEnabled("Codex"), "An intentionally empty provider selection survives restart");
            restoredSelection.SetProviderEnabled("cLaUdE", true);
            Check(restoredSelection.EnabledProviders.SequenceEqual(new[] { "Claude" }), "Hidden providers can be re-enabled with canonical names");
            var absent = SetupDiagnostics.Classify("Codex", false, null);
            Check(absent.Status == "not_selected" && SetupDiagnostics.ExitCode(new[] { absent }) == 0,
                "Unselected providers do not fail setup diagnostics");
            var login = SetupDiagnostics.Classify("Claude", true, ProviderState.Empty("Claude") with { SetupStatus = "login_required" });
            Check(login.Status == "login_required" && SetupDiagnostics.ExitCode(new[] { login }) == 3,
                "Setup diagnostics distinguish user login from installation errors");
            Check(SetupDiagnostics.ExitCode(new[] { login, SetupDiagnostics.Classify("Codex", true, null) }) == 2,
                "Connection errors take precedence over pending login");
            Check(SetupDiagnostics.Classify("Codex", true, new("Codex", "", new() { new("x", "week", 20, null, 10080) }, DateTimeOffset.Now, "ok", true)).Status == "ready",
                "A selected provider with current quota passes setup diagnostics");
            var selected = new Settings { EnabledProviders = new[] { "Codex" } };
            Check(!TrayFormatter.Format(ProviderState.Empty("Codex"), ProviderState.Empty("Claude"), selected).Contains("Claude")
                && TrayFormatter.Format(ProviderState.Empty("Codex"), ProviderState.Empty("Claude"), new Settings { EnabledProviders = new[] { "Devin" } }) == "AI Usage Widget",
                "Tray summary omits unselected providers and supports other-provider-only installs");
            Check(selected.IsEnabled("codex") && !selected.IsEnabled("Claude") && new Settings().IsEnabled("Claude"),
                "Provider selection is case insensitive and legacy settings retain all providers");
            using (var disabled = new TranscriptAnalytics(new Settings { EnabledProviders = Array.Empty<string>() }, dir, dir, dir))
                Check(disabled.Scan(CancellationToken.None).Entries.Count == 0, "Disabled providers do not index local transcripts");
            const string threadId = "019ef3c1-1234-7123-8123-0123456789ab";
            Check(CodexNavigation.ThreadUri(threadId) == "codex://threads/" + threadId,
                "Codex navigation targets the exact existing thread without prompt or wake parameters");
            Check(new[] { "", "new", "../new", threadId + "?prompt=hello", "codex://threads/" + threadId, Guid.Empty.ToString() }
                .All(id => CodexNavigation.ThreadUri(id) is null), "Invalid session IDs cannot become a Codex navigation command");
            Check(ClaudeNavigation.ThreadUri(threadId) == "claude://resume?session=" + threadId,
                "Claude navigation resumes the exact local conversation without a prompt");
            Check(new[] { "", "last", "../new", threadId + "&prompt=hello", "claude://resume?session=" + threadId, Guid.Empty.ToString() }
                .All(id => ClaudeNavigation.ThreadUri(id) is null), "Invalid session IDs cannot become a Claude navigation command");
            var at = DateTimeOffset.UtcNow;
            var projectRows = new[] {
                new TokenEntry("Codex", "project-a", "session-a", "model-a", at, 10, 90, 0, 0, false),
                new TokenEntry("Codex", "project-a", "session-a", "model-b", at, 20, 0, 0, 10, false),
                new TokenEntry("Codex", "project-a", "session-b", "model-a", at, 50, 150, 0, 20, false),
                new TokenEntry("Claude", "project-a", "session-a", "foreign-provider", at, 9999, 0, 0, 0, false),
                new TokenEntry("Codex", "project-b", "session-a", "foreign-project", at, 9999, 0, 0, 0, false),
                new TokenEntry("Codex", "project-a", "old", "old-model", at.AddDays(-2), 9999, 0, 0, 0, false) };
            var project = ProjectBreakdown.Build(projectRows, "Codex", "project-a", at.AddDays(-1));
            Check(project.Tokens.Total == 350 && project.Tokens.Cached == 240 && project.Tokens.Responses == 3,
                "Project breakdown scopes provider, exact project and time without double counting cache");
            Check(project.Sessions.Count == 2 && project.Sessions[0].Id == "session-b" && project.Sessions[1].Models.Count == 2,
                "Project session ranking preserves mixed-model sessions and sorts by tokens");
            Check(project.Models[0].Name == "model-a" && project.Models[0].Tokens.Total == 320 && project.Models[0].Sessions == 2
                && project.Sessions.Sum(s => s.Tokens.Total) == project.Models.Sum(m => m.Tokens.Total),
                "Session and model rankings reconcile to the same project total");
            Check(ProjectBreakdown.Build(projectRows, "Codex", "missing", at.AddDays(-1)).Tokens.Total == 0,
                "Empty project breakdown has no invented usage");
            string bounded = WidgetTips.Body(string.Join("\n", Enumerable.Repeat(new string('x', 10000), 30)));
            Check(bounded.Length <= 420 && bounded.EndsWith("…"), "Provider tooltip text has a hard display bound");
            Check(WidgetTips.Line("Short title\nLarge prompt body") == "Short title", "Details heading uses only first line");
            var now = DateTimeOffset.UtcNow.AddMinutes(-6);
            var h = new UsageHistory(Path.Combine(dir, "history.json"));
            var s = new ProviderState("Codex", "", new() { new("main", "Tydzień", 15, now.AddDays(2).ToUnixTimeSeconds(), 10080) }, now, "", true);
            Check(h.Record(s, now), "History records a real quota observation");
            Check(!h.Record(s, now.AddMinutes(1)) && h.Samples.Count == 1, "Repaint and cached observations do not create history");
            Check(!h.Record(s with { Connected = false, Updated = now.AddMinutes(2) }, now.AddMinutes(2)), "Offline quota is never a fresh history sample");
            Check(!h.Record(s with { Updated = now.AddSeconds(10), Windows = s.Windows.Select(w => w with { Used = 16 }).ToList() }, now.AddSeconds(10)), "Burst updates are coalesced to limit history size");
            Check(h.Record(s with { Updated = now.AddMinutes(5) }, now.AddMinutes(5)), "Unchanged quota is sampled periodically");
            var a = h.Samples[0]; var b = h.Samples[1];
            Check(UsageHistory.CanConnect(a, b) && !UsageHistory.CanConnect(a, b with { ResetAt = b.ResetAt + 100 }), "Graph never joins independent quota cycles");
            Check(!UsageHistory.CanConnect(a, b with { At = now.AddHours(1) }) && !UsageHistory.CanConnect(a, b with { Used = 1 }), "Graph breaks at offline gaps and quota drops");
            var reloaded = new UsageHistory(Path.Combine(dir, "history.json"));
            Check(reloaded.Samples.Count == 2 && h.Error is null, "History survives restart");
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{broken");
            Check(new UsageHistory(Path.Combine(dir, "broken.json")).Samples.Count == 0, "Corrupt history does not prevent startup");
            File.WriteAllText(Path.Combine(dir, "broken.json"), "[null,{\"Provider\":\"Codex\"}]");
            Check(new UsageHistory(Path.Combine(dir, "broken.json")).Samples.Count == 0, "Malformed history rows are ignored");
            Check(!File.ReadAllText(Path.Combine(dir, "history.json")).Contains("Plan"), "History persists quotas only");
            JsonElement Parse(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
            var sessions = SessionMonitor.ParseClaude(Parse("""[{"sessionId":"1","cwd":"C:/one","status":"busy"},{"sessionId":"2","cwd":"C:/two","status":"waiting","waitingFor":"input needed"},{"sessionId":"3","cwd":"C:/three","status":"idle"},{"id":"4","state":"done"}]"""));
            Check(sessions.Count == 3 && sessions[0].State == "working" && sessions[1].State == "waiting" && sessions[2].State == "idle", "Claude active states exclude completed tasks");
            Check(sessions[1].Reason == "Odpowiedź na pytanie", "Claude wait reason is readable in Polish");
            Check(SessionMonitor.IsWaitingItem("""{"type":"requestUserInput"}""") && !SessionMonitor.IsWaitingItem("""{"name":"functions.request_user_input_async"}"""), "Only blocking Codex inputs count as waiting");
            var live = new SessionSnapshot(new() { sessions[1] }, DateTimeOffset.Now);
            Check(MascotView.Choose(s with { Name = "Claude" }, live) == MascotState.Waiting, "Mascot gives waiting sessions priority");
            Check(MascotView.Choose(s with { Updated = DateTimeOffset.Now }, SessionSnapshot.Empty) == MascotState.Sleeping, "No active session gives a resting mascot");
            var healthy = HealthMonitor.Parse("Claude", Parse("""{"status":{"indicator":"none"},"incidents":[]}"""), now);
            Check(healthy.Available && !healthy.Incident, "Healthy service response");
            Check(HealthMonitor.Parse("Codex", Parse("{}"), now).Available == false, "Missing status does not imply a healthy service");
            Check(HealthMonitor.Parse("Codex", Parse("""{"status":{"indicator":"minor"},"incidents":[{"name":"Test outage","status":"investigating"}]}"""), now).Detail == "Test outage", "Service incidents retain the published description");
            string jsonl = Path.Combine(dir, "partial.jsonl");
            File.WriteAllText(jsonl, "{\"value\":\"żółw\"}", new UTF8Encoding(false));
            using var cursor = new JsonlCursor(); int lines = 0;
            cursor.Read(jsonl, 1_000_000, e => lines++, CancellationToken.None);
            Check(lines == 0, "Partial JSONL records are held until complete");
            File.AppendAllText(jsonl, "\n"); cursor.Read(jsonl, 1_000_000, e => { if (UsageParser.Text(e, "value") == "żółw") lines++; }, CancellationToken.None);
            cursor.Read(jsonl, 1_000_000, e => lines++, CancellationToken.None);
            Check(lines == 1, "Incremental reader keeps UTF-8 and never replays complete lines");
            string cp = Path.Combine(dir, "claude"), xp = Path.Combine(dir, "codex"), ap = Path.Combine(dir, "archive");
            Directory.CreateDirectory(cp); Directory.CreateDirectory(xp); Directory.CreateDirectory(ap);
            string stamp = DateTimeOffset.UtcNow.ToString("O");
            string ClaudeLine(long output) => JsonSerializer.Serialize(new { type = "assistant", timestamp = stamp, cwd = "C:/project", sessionId = "session", message = new { id = "response", model = "claude-test", usage = new { input_tokens = 10, cache_read_input_tokens = 100, cache_creation_input_tokens = 20, output_tokens = output }, content = new[] { new { type = "tool_use", id = "tool", name = "mcp__example__read" } } } });
            File.WriteAllText(Path.Combine(cp, "one.jsonl"), ClaudeLine(2) + "\n" + ClaudeLine(8) + "\n");
            File.WriteAllText(Path.Combine(cp, "copy.jsonl"), ClaudeLine(8) + "\n");
            var switchable = new Settings { EnabledProviders = new[] { "Claude" } };
            using (var switchingAnalytics = new TranscriptAnalytics(switchable, cp, xp, ap)) {
                Check(switchingAnalytics.Scan(CancellationToken.None).Entries.Count == 1, "Selected provider transcripts are indexed");
                switchable.SetProviderEnabled("Claude", false);
                Check(switchingAnalytics.Scan(CancellationToken.None).Entries.Count == 0, "Hiding a provider removes its live analytics without a restart");
                switchable.SetProviderEnabled("Claude", true);
                Check(switchingAnalytics.Scan(CancellationToken.None).Entries.Count == 1, "Re-enabling a provider resumes transcript indexing");
            }
            var codexUsage = new { input_tokens = 80, cached_input_tokens = 30, output_tokens = 10 };
            string codexLine = JsonSerializer.Serialize(new { type = "token_usage_record", timestamp = stamp, payload = new { response_id = "r1", usage = codexUsage } });
            File.WriteAllText(Path.Combine(xp, "one.jsonl"), codexLine + "\n" + codexLine + "\n");
            using var analyzer = new TranscriptAnalytics(new Settings(), cp, xp, ap);
            var stats = analyzer.Scan(CancellationToken.None);
            Check(stats.Entries.Count == 2 && stats.Tools.Count == 1, "Responses and tools are deduplicated across streamed records and copies");
            Check(stats.Entries.Single(e => e.Provider == "Claude").Total == 138, "Claude streaming updates use final token counts");
            Check(stats.Entries.Single(e => e.Provider == "Codex").Total == 90, "Codex cached input is not counted twice");
            Check(analyzer.Scan(CancellationToken.None).Entries.Count == 2, "Repeated analytics refresh is idempotent");
            string partialResponse = ClaudeLine(15).Replace("response", "appended").Replace("\"tool\"", "\"new-tool\"");
            string appendFile = Path.Combine(cp, "one.jsonl");
            File.AppendAllText(appendFile, partialResponse);
            Check(analyzer.Scan(CancellationToken.None).Entries.Count == 2, "Unterminated appended response stays out of statistics");
            File.AppendAllText(appendFile, "\n");
            Check(analyzer.Scan(CancellationToken.None).Entries.Count == 3, "Completed append is indexed after unchanged scans");
            File.WriteAllText(appendFile, ClaudeLine(8) + "\n");
            Check(analyzer.Scan(CancellationToken.None).Entries.Count == 2, "Truncated transcript invalidates its old indexed responses");
            File.Delete(appendFile);
            Check(analyzer.Scan(CancellationToken.None).Entries.Count == 2, "Deleting a duplicate transcript preserves the surviving response");
            File.Delete(Path.Combine(cp, "copy.jsonl"));
            Check(analyzer.Scan(CancellationToken.None).Entries.Count == 1, "Deleted transcripts disappear from cached statistics");
            string expiryRoot = Path.Combine(dir, "expiry"); Directory.CreateDirectory(expiryRoot);
            var expiryNow = DateTimeOffset.UtcNow;
            string boundaryResponse = ClaudeLine(8).Replace(JsonSerializer.Serialize(stamp), JsonSerializer.Serialize(expiryNow.AddDays(-7).ToString("O")));
            File.WriteAllText(Path.Combine(expiryRoot, "one.jsonl"), boundaryResponse + "\n");
            using var expiryAnalyzer = new TranscriptAnalytics(new Settings(), expiryRoot, ap, ap);
            Check(expiryAnalyzer.Scan(CancellationToken.None, expiryNow).Entries.Count == 1
                && expiryAnalyzer.Scan(CancellationToken.None, expiryNow.AddTicks(1)).Entries.Count == 0,
                "Seven-day expiry removes unchanged records at the exact boundary");
            string oversizedFile = Path.Combine(dir, "oversized.jsonl");
            File.WriteAllText(oversizedFile, new string('x', 4_000_001) + "\n{\"value\":1}\n");
            using var oversizedCursor = new JsonlCursor(); int validLines = 0;
            oversizedCursor.Read(oversizedFile, 10_000_000, _ => validLines++, CancellationToken.None);
            Check(validLines == 1 && oversizedCursor.Skipped == 1, "Oversized records do not consume the following valid record");
            var trayClaude = s with { Name = "Claude", Updated = DateTimeOffset.Now, Windows = new() { new("five_hour", "5h", 6, DateTimeOffset.Now.AddHours(2).ToUnixTimeSeconds(), 300), new("seven_day", "7d", 91, DateTimeOffset.Now.AddDays(2).ToUnixTimeSeconds(), 10080) } };
            string tooltip = TrayFormatter.Format(s with { Updated = DateTimeOffset.Now }, trayClaude);
            Check(tooltip.Contains("5h 94%") && tooltip.Contains("7d 9%") && tooltip.Length <= 63, "Tray tooltip retains both Claude windows when weekly quota is constrained");
        }
        finally { Directory.Delete(dir, true); }
        return n;
    }
}
