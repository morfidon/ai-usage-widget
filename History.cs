using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AIUsageWidget;

public sealed record UsageSample(string Provider, string Id, string Label, DateTimeOffset At, double Used, long? ResetAt);

// Contains only server-reported quota samples, never credentials or conversation content.
public sealed class UsageHistory
{
    readonly string path;
    public string? Error { get; private set; }
    public List<UsageSample> Samples { get; } = new();
    public UsageHistory(string? path = null)
    {
        this.path = path ?? Path.Combine(Settings.DataDirectory, "usage-history.json");
        try
        {
            var rows = JsonSerializer.Deserialize<List<UsageSample>>(File.ReadAllText(this.path));
            Samples.AddRange((rows ?? new()).Where(s => s.Provider is "Claude" or "Codex"
                && double.IsFinite(s.Used) && s.Used >= 0 && s.Used <= 100
                && s.At >= DateTimeOffset.UtcNow.AddDays(-31) && s.At <= DateTimeOffset.UtcNow.AddMinutes(1))
                .OrderBy(s => s.At).DistinctBy(s => (s.Provider, s.Id, s.At)));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { if (File.Exists(this.path)) Error = "Nie można odczytać historii (" + e.GetType().Name + ")"; }
    }
    public bool Record(ProviderState state, DateTimeOffset now)
    {
        if (state.Name is not ("Claude" or "Codex") || !state.Connected || state.Updated is not DateTimeOffset at
            || now - at > TimeSpan.FromMinutes(10) || at > now.AddMinutes(1)) return false;
        bool changed = false;
        foreach (var w in state.Windows)
        {
            if (!double.IsFinite(w.Used) || w.ResetAt is long reset && reset <= at.ToUnixTimeSeconds()) continue;
            var last = Samples.LastOrDefault(s => s.Provider == state.Name && s.Id == w.Id);
            if (last is not null && (at <= last.At || (at - last.At < TimeSpan.FromMinutes(5)
                && last.Used == w.Used && last.ResetAt == w.ResetAt))) continue;
            Samples.Add(new(state.Name, w.Id, w.Label, at, Math.Clamp(w.Used, 0, 100), w.ResetAt));
            changed = true;
        }
        Samples.RemoveAll(s => s.At < now.AddDays(-31));
        if (changed) Save();
        return changed;
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Samples));
            File.Move(path + ".tmp", path, true);
            Error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Error = "Nie można zapisać historii (" + e.GetType().Name + ")"; }
    }
    public static bool CanConnect(UsageSample a, UsageSample b) => a.Provider == b.Provider && a.Id == b.Id
        && a.ResetAt == b.ResetAt && b.At > a.At && b.At - a.At <= TimeSpan.FromMinutes(12)
        && b.Used >= a.Used;
}
