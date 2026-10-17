using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AIUsageWidget;

public sealed record UsageWindow(string Id, string Label, double Used, long? ResetAt, int Minutes,
    string? BalanceText = null, bool Renews = true, string? Group = null, string? DetailText = null)
{
    public double Remaining => Math.Clamp(100 - Used, 0, 100);
    public bool Expired => ResetAt is long r && r <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string ResetText => Renews ? ResetFormatter.Format(ResetAt, DateTimeOffset.Now, TimeZoneInfo.Local).Detail
        : "Bez resetu miesięcznego · ważność dodatkowych kredytów sprawdzisz na koncie JetBrains";
}

public sealed record ResetPresentation(string Countdown, string Calendar, string ShortCalendar)
{
    public string Detail => Calendar.Length == 0 ? Countdown : Countdown + " · " + Calendar;
}

public static class ResetFormatter
{
    static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
    public static ResetPresentation Format(long? resetAt, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (resetAt is not long seconds) return new("Brak czasu resetu", "", "");
        var instant = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var span = instant - now;
        if (span <= TimeSpan.Zero) return new("Reset minął · czekam na dane", "", "");
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        int dayDifference = (local.Date - today).Days;
        string weekday = Polish.TextInfo.ToTitleCase(local.ToString("dddd", Polish));
        string day = dayDifference switch { 0 => "Dzisiaj", 1 => "Jutro", _ => weekday };
        string date = local.ToString(local.Year == today.Year ? "d MMMM" : "d MMMM yyyy", Polish);
        string time = local.ToString("HH:mm", Polish);
        string shortDay = dayDifference < 7 ? day : local.ToString(local.Year == today.Year ? "d MMM" : "dd.MM.yyyy", Polish);
        long minutes = (long)Math.Ceiling(span.TotalMinutes);
        long days = minutes / 1440, hours = minutes / 60 % 24, mins = minutes % 60;
        string remaining = span.TotalMinutes < 1 ? "<1 min"
            : days > 0 ? $"{days} {(days == 1 ? "dzień" : "dni")}" + (hours > 0 ? $" {hours} godz." : "")
            : hours > 0 ? $"{hours} godz." + (mins > 0 ? $" {mins} min" : "")
            : $"{mins} min";
        return new("Reset za " + remaining, $"{day}, {date} · {time}", $"{shortDay} · {time}");
    }
}

public sealed record ProviderState(string Name, string Plan, List<UsageWindow> Windows, DateTimeOffset? Updated,
    string Status, bool Connected, string? Error = null)
{
    public static ProviderState Empty(string name) => new(name, "", new(), null, "Łączenie…", false);
}

public static class UsageParser
{
    public static string? Text(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p)
        && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    public static JsonElement? Object(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p)
        && p.ValueKind == JsonValueKind.Object ? p : null;
    public static double? Number(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p)
        && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v) && double.IsFinite(v) ? v : null;
    public static long? Timestamp(JsonElement e, string key)
    {
        var n = Number(e, key);
        if (n.HasValue && n.Value >= 0 && n.Value < 253402300799) return (long)n.Value;
        return DateTimeOffset.TryParse(Text(e, key), out var d) ? d.ToUnixTimeSeconds() : null;
    }
    public static List<UsageWindow> Codex(JsonElement result)
    {
        var list = new List<UsageWindow>();
        var buckets = Object(result, "rateLimitsByLimitId");
        if (buckets.HasValue)
            foreach (var b in buckets.Value.EnumerateObject().OrderBy(x => x.Name == "codex" ? 0 : 1)) AddCodex(list, b.Value, b.Name);
        if (list.Count == 0 && Object(result, "rateLimits") is JsonElement single) AddCodex(list, single, "codex");
        return list;
    }
    static void AddCodex(List<UsageWindow> list, JsonElement bucket, string id)
    {
        foreach (var key in new[] { "primary", "secondary" })
        {
            if (Object(bucket, key) is not JsonElement w || Number(w, "usedPercent") is not double used) continue;
            int minutes = (int)(Number(w, "windowDurationMins") ?? 0);
            string duration = minutes switch { 300 => "5 godzin", 10080 => "Tydzień", > 0 and < 60 => $"{minutes} min", >= 60 and < 1440 => $"{minutes / 60.0:0.#} godz.", >= 1440 => $"{minutes / 1440.0:0.#} dni", _ => key == "primary" ? "Limit główny" : "Limit dodatkowy" };
            string prefix = id == "codex" ? "" : (Text(bucket, "limitName") ?? id) + " · ";
            list.Add(new(id + ":" + key, prefix + duration, Math.Clamp(used, 0, 100), Timestamp(w, "resetsAt"), minutes));
        }
    }
    public static List<UsageWindow> Claude(JsonElement root, bool bridge)
    {
        var list = new List<UsageWindow>();
        JsonElement source = bridge ? Object(root, "rate_limits") ?? root : root;
        foreach (var item in new[] { ("five_hour", "5 godzin", 300), ("seven_day", "Tydzień", 10080),
            ("seven_day_opus", "Tydzień · Opus", 10080), ("seven_day_sonnet", "Tydzień · Sonnet", 10080), ("spend_limit", "Limit wydatków", 0) })
        {
            if (Object(source, item.Item1) is not JsonElement w) continue;
            var used = Number(w, bridge ? "used_percentage" : "utilization");
            if (used.HasValue) list.Add(new(item.Item1, item.Item2, Math.Clamp(used.Value, 0, 100), Timestamp(w, "resets_at"), item.Item3));
        }
        // Newer usage endpoints can expose an array of scoped meters instead of the legacy fields.
        if (list.Count == 0 && source.ValueKind == JsonValueKind.Object)
            foreach (var field in source.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array))
                foreach (var meter in field.Value.EnumerateArray())
                {
                    string? kind = Text(meter, "kind");
                    var used = Number(meter, "percent");
                    if (kind is null || !used.HasValue) continue;
                    string label = kind switch { "session" => "5 godzin", "weekly_all" => "Tydzień", _ => "Tydzień · model" };
                    if (Object(meter, "scope") is JsonElement scope && Object(scope, "model") is JsonElement model)
                        label = "Tydzień · " + (Text(model, "display_name") ?? Text(model, "id") ?? "model");
                    list.Add(new(kind + ":" + list.Count, label, Math.Clamp(used.Value, 0, 100), Timestamp(meter, "resets_at"), kind == "session" ? 300 : 10080));
                }
        return list;
    }
}

public sealed class Settings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Compact { get; set; }
    public bool Topmost { get; set; } = true;
    public int RefreshSeconds { get; set; } = 60;
    public string? CodexPath { get; set; }
    public string? ClaudeDirectory { get; set; }
    public string TrayProvider { get; set; } = "Codex";
    public bool AnimateMascots { get; set; } = true;
    public static string ProjectDirectory => Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
    public static string DataDirectory => Path.Combine(ProjectDirectory, "data");
    public static string PathName => Path.Combine(DataDirectory, "settings.json");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(PathName + ".tmp", PathName, true);
    }
}

public static class TrayFormatter
{
    public static string Format(ProviderState codex, ProviderState claude)
    {
        string Summary(ProviderState state)
        {
            if (!state.Connected || state.Updated is null || DateTimeOffset.Now - state.Updated > TimeSpan.FromMinutes(10)) return state.Name + ": ?";
            var windows = state.Windows.Where(w => !w.Expired && (state.Name != "Codex" || w.Id.StartsWith("codex:"))).OrderBy(w => w.Minutes).Take(2);
            string detail = string.Join(" ", windows.Select(w => (w.Minutes == 300 ? "5h" : w.Minutes == 10080 ? "7d" : "limit") + $" {w.Remaining:0}%"));
            return state.Name + ": " + (detail.Length > 0 ? detail : "?");
        }
        string value = "Wolne · " + Summary(codex) + " | " + Summary(claude);
        return value[..Math.Min(63, value.Length)];
    }
}
