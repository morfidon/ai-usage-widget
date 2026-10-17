using System;
using System.IO;
using System.Linq;

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
            Check(h.Record(s with { Updated = now.AddMinutes(5) }, now.AddMinutes(5)), "Unchanged quota is sampled periodically");
            var a = h.Samples[0]; var b = h.Samples[1];
            Check(UsageHistory.CanConnect(a, b) && !UsageHistory.CanConnect(a, b with { ResetAt = b.ResetAt + 100 }), "Graph never joins independent quota cycles");
            Check(!UsageHistory.CanConnect(a, b with { At = now.AddHours(1) }) && !UsageHistory.CanConnect(a, b with { Used = 1 }), "Graph breaks at offline gaps and quota drops");
            var reloaded = new UsageHistory(Path.Combine(dir, "history.json"));
            Check(reloaded.Samples.Count == 2 && h.Error is null, "History survives restart");
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{broken");
            Check(new UsageHistory(Path.Combine(dir, "broken.json")).Samples.Count == 0, "Corrupt history does not prevent startup");
            Check(!File.ReadAllText(Path.Combine(dir, "history.json")).Contains("Plan"), "History persists quotas only");
        }
        finally { Directory.Delete(dir, true); }
        return n;
    }
}
