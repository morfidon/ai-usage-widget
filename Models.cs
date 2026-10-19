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
        : L.T("Bez resetu miesięcznego · ważność dodatkowych kredytów sprawdzisz na koncie JetBrains");
}

public sealed record ResetPresentation(string Countdown, string Calendar, string ShortCalendar)
{
    public string Detail => Calendar.Length == 0 ? Countdown : Countdown + " · " + Calendar;
}

public static class ResetFormatter
{
    public static ResetPresentation Format(long? resetAt, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (resetAt is not long seconds) return new(L.T("Brak czasu resetu"), "", "");
        var instant = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var span = instant - now;
        if (span <= TimeSpan.Zero) return new(L.T("Reset minął · czekam na dane"), "", "");
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        int dayDifference = (local.Date - today).Days;
        var culture = L.Culture;
        string weekday = culture.TextInfo.ToTitleCase(local.ToString("dddd", culture));
        string day = dayDifference switch { 0 => L.T("Dzisiaj"), 1 => L.T("Jutro"), _ => weekday };
        string date = local.ToString(local.Year == today.Year ? "d MMMM" : "d MMMM yyyy", culture);
        string time = local.ToString("HH:mm", culture);
        string shortDay = dayDifference < 7 ? day : local.ToString(local.Year == today.Year ? "d MMM" : "dd.MM.yyyy", culture);
        long minutes = (long)Math.Ceiling(span.TotalMinutes);
        long days = minutes / 1440, hours = minutes / 60 % 24, mins = minutes % 60;
        string remaining = span.TotalMinutes < 1 ? "<1 min"
            : days > 0 ? $"{days} {(days == 1 ? L.T("dzień") : L.T("dni"))}" + (hours > 0 ? L.F(" {0} godz.", hours) : "")
            : hours > 0 ? L.F("{0} godz.", hours) + (mins > 0 ? $" {mins} min" : "")
            : $"{mins} min";
        return new(L.T("Reset za ") + remaining, $"{day}, {date} · {time}", $"{shortDay} · {time}");
    }
}

public sealed record ProviderState(string Name, string Plan, List<UsageWindow> Windows, DateTimeOffset? Updated,
    string Status, bool Connected, string? Error = null)
{
    public static ProviderState Empty(string name) => new(name, "", new(), null, L.T("Łączenie…"), false);
    public string? SetupStatus { get; init; }
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
            string duration = minutes switch { 300 => L.T("5 godzin"), 10080 => L.T("Tydzień"), > 0 and < 60 => $"{minutes} min", >= 60 and < 1440 => L.F("{0:0.#} godz.", minutes / 60.0), >= 1440 => L.F("{0:0.#} dni", minutes / 1440.0), _ => key == "primary" ? L.T("Limit główny") : L.T("Limit dodatkowy") };
            string prefix = id == "codex" ? "" : (Text(bucket, "limitName") ?? id) + " · ";
            list.Add(new(id + ":" + key, prefix + duration, Math.Clamp(used, 0, 100), Timestamp(w, "resetsAt"), minutes));
        }
    }
    public static List<UsageWindow> Claude(JsonElement root, bool bridge)
    {
        var list = new List<UsageWindow>();
        JsonElement source = bridge ? Object(root, "rate_limits") ?? root : root;
        foreach (var item in new[] { ("five_hour", L.T("5 godzin"), 300), ("seven_day", L.T("Tydzień"), 10080),
            ("seven_day_opus", L.T("Tydzień · Opus"), 10080), ("seven_day_sonnet", L.T("Tydzień · Sonnet"), 10080), ("spend_limit", L.T("Limit wydatków"), 0) })
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
                    string label = kind switch { "session" => L.T("5 godzin"), "weekly_all" => L.T("Tydzień"), _ => L.T("Tydzień · model") };
                    if (Object(meter, "scope") is JsonElement scope && Object(scope, "model") is JsonElement model)
                        label = L.T("Tydzień · ") + (Text(model, "display_name") ?? Text(model, "id") ?? "model");
                    list.Add(new(kind + ":" + list.Count, label, Math.Clamp(used.Value, 0, 100), Timestamp(meter, "resets_at"), kind == "session" ? 300 : 10080));
                }
        return list;
    }
}

public sealed class Settings
{
    public string Language { get; set; } = "en";
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Compact { get; set; }
    public bool Topmost { get; set; } = true;
    public int RefreshSeconds { get; set; } = 60;
    public string? CodexPath { get; set; }
    public string? ClaudeDirectory { get; set; }
    // Null preserves the existing five-provider behavior on upgrades.
    public string[]? EnabledProviders { get; set; }
    public bool IsEnabled(string name) => EnabledProviders is null || EnabledProviders.Contains(name, StringComparer.OrdinalIgnoreCase);
    public void SetProviderEnabled(string name, bool enabled)
    {
        if (!SetupDiagnostics.Providers.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unknown provider", nameof(name));
        EnabledProviders = SetupDiagnostics.Providers.Where(p => p.Equals(name, StringComparison.OrdinalIgnoreCase) ? enabled : IsEnabled(p)).ToArray();
    }
    public string TrayProvider { get; set; } = "Codex";
    public bool AnimateMascots { get; set; } = true;
    public static string ProjectDirectory => Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
    public static string DataDirectory {
        get {
            string layout = Path.Combine(ProjectDirectory, "install-layout.json");
            if (!File.Exists(layout)) return Path.Combine(ProjectDirectory, "data");
            using var doc = JsonDocument.Parse(File.ReadAllText(layout));
            string? path = doc.RootElement.GetProperty("DataDirectory").GetString();
            if (path is null || !Path.IsPathFullyQualified(path)) throw new IOException("Invalid installation data directory.");
            return path;
        }
    }
    public static string PathName => Path.Combine(DataDirectory, "settings.json");
    public static Settings Load()
    {
        if (!File.Exists(PathName)) {
            var firstRun = new Settings();
            firstRun.EnabledProviders = ProviderDiscovery.Detect(firstRun);
            return firstRun;
        }
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
    public static string Format(ProviderState codex, ProviderState claude, Settings? settings = null)
    {
        string Summary(ProviderState state)
        {
            if (!state.Connected || state.Updated is null || DateTimeOffset.Now - state.Updated > TimeSpan.FromMinutes(10)) return state.Name + ": ?";
            var windows = state.Windows.Where(w => !w.Expired && (state.Name != "Codex" || w.Id.StartsWith("codex:"))).OrderBy(w => w.Minutes).Take(2);
            string detail = string.Join(" ", windows.Select(w => (w.Minutes == 300 ? "5h" : w.Minutes == 10080 ? "7d" : "limit") + $" {w.Remaining:0}%"));
            return state.Name + ": " + (detail.Length > 0 ? detail : "?");
        }
        var enabled = new[] { codex, claude }.Where(s => settings?.IsEnabled(s.Name) != false).ToArray();
        string value = enabled.Length == 0 ? "AI Usage Widget" : L.T("Wolne · ") + string.Join(" | ", enabled.Select(Summary));
        return value[..Math.Min(63, value.Length)];
    }
}
