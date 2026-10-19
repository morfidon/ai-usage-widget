using System;
using System.Linq;
using System.Text.Json;

namespace AIUsageWidget;

public static class LocalizationTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); count++; }
        Check(new Settings().Language == "en" && JsonSerializer.Deserialize<Settings>("{}")!.Language == "en", "English is the default for fresh and legacy settings");
        Check(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { Language = "pl" }))!.Language == "pl", "Explicit Polish choice survives settings serialization");
        L.SetLanguage("unsupported");
        Check(L.Language == "en" && L.Culture.Name == "en-US", "Unknown language falls back to English independent of Windows locale");
        Check(L.T("Dostawcy") == "Providers" && L.T("Odśwież limity") == "Refresh limits", "English menus are translated");
        var now = DateTimeOffset.Parse("2026-10-04T16:50:00+02:00");
        var reset = ResetFormatter.Format(now.AddMinutes(100).ToUnixTimeSeconds(), now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));
        Check(reset.Countdown == "Resets in 1 h 40 min" && reset.Calendar == "Today, 4 October · 18:30", "English countdown and calendar retain the exact reset time");
        using var codex = JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":31,"windowDurationMins":10080}}}""");
        Check(UsageParser.Codex(codex.RootElement).Single().Label == "Weekly", "Quota labels use English");
        using var air = JsonDocument.Parse("""{"tariffQuota":{"maximum":35,"available":16.12}}""");
        Check(AdditionalUsageParser.Air(air.RootElement).Windows.Single().BalanceText == "16.12 / 35 AI credits", "English credit balances use a decimal point");
        Check(InsightsWindow.Tokens(1250000) == "1.25M" && InsightsWindow.Count(22, "session", "sessions", "sessions") == "22 sessions", "English counts and compact token units");
        Check(L.Display("Tydzień · Custom model") == "Weekly · Custom model" && L.Display("16,12 / 35 kredytów AI") == "16.12 / 35 AI credits", "Old cached labels and balances display in English without rewriting history");
        Check(L.QuotaLabel("Custom model · 5 godzin") == "Custom model · 5 hours" && L.QuotaLabel("1,5 godz.") == "1.5 h", "Cached scoped quotas and variable durations translate without changing model names");
        var original = new ProviderState("Codex", "Pro", new() { new("codex:primary", "Tydzień", 31, null, 10080) }, now, "Połączono", true);
        var english = L.Localize(original);
        Check(english.Windows[0].Label == "Weekly" && english.Windows[0].Id == original.Windows[0].Id && english.Windows[0].Used == 31 && ReferenceEquals(english, L.Localize(english)), "Translation preserves quota identity and stable render-cache references");
        L.SetLanguage("pl");
        Check(L.Display("Weekly · Custom model") == "Tydzień · Custom model" && L.Display("16.12 / 35 AI credits") == "16,12 / 35 kredytów AI", "Cached English labels and balances display in Polish");
        Check(InsightsWindow.Count(22, "sesja", "sesje", "sesji") == "22 sesje" && L.T("Dostawcy") == "Dostawcy", "Polish grammar and menus remain available");
        L.SetLanguage("en");
        return count;
    }
}
