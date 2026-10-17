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
            var codexUsage = new { input_tokens = 80, cached_input_tokens = 30, output_tokens = 10 };
            string codexLine = JsonSerializer.Serialize(new { type = "token_usage_record", timestamp = stamp, payload = new { response_id = "r1", usage = codexUsage } });
            File.WriteAllText(Path.Combine(xp, "one.jsonl"), codexLine + "\n" + codexLine + "\n");
            using var analyzer = new TranscriptAnalytics(new Settings(), cp, xp, ap);
            var stats = analyzer.Scan(CancellationToken.None);
            Check(stats.Entries.Count == 2 && stats.Tools.Count == 1, "Responses and tools are deduplicated across streamed records and copies");
            Check(stats.Entries.Single(e => e.Provider == "Claude").Total == 138, "Claude streaming updates use final token counts");
            Check(stats.Entries.Single(e => e.Provider == "Codex").Total == 90, "Codex cached input is not counted twice");
            Check(analyzer.Scan(CancellationToken.None).Entries.Count == 2, "Repeated analytics refresh is idempotent");
            var trayClaude = s with { Name = "Claude", Updated = DateTimeOffset.Now, Windows = new() { new("five_hour", "5h", 6, DateTimeOffset.Now.AddHours(2).ToUnixTimeSeconds(), 300), new("seven_day", "7d", 91, DateTimeOffset.Now.AddDays(2).ToUnixTimeSeconds(), 10080) } };
            string tooltip = TrayFormatter.Format(s with { Updated = DateTimeOffset.Now }, trayClaude);
            Check(tooltip.Contains("5h 94%") && tooltip.Contains("7d 9%") && tooltip.Length <= 63, "Tray tooltip retains both Claude windows when weekly quota is constrained");
        }
        finally { Directory.Delete(dir, true); }
        return n;
    }
}
