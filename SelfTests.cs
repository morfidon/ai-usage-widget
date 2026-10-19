using System;
using System.Linq;
using System.Text.Json;

namespace AIUsageWidget;

public static class SelfTests
{
    public static int Run()
    {
        L.SetLanguage("pl"); // Preserve the existing Polish regression expectations.
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); count++; }
        JsonElement Parse(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
        var weekly = UsageParser.Codex(Parse("""{"rateLimits":{"primary":{"usedPercent":31,"windowDurationMins":10080,"resetsAt":1999999999},"secondary":null}}"""));
        Check(weekly.Count == 1 && weekly[0].Label == "Tydzień", "Weekly primary is labeled by duration");
        Check(weekly[0].Remaining == 69, "Remaining percentage");
        var buckets = UsageParser.Codex(Parse("""{"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"special":{"limitName":"Model X","primary":{"usedPercent":14,"windowDurationMins":300}},"codex":{"primary":{"usedPercent":40,"windowDurationMins":300},"secondary":{"usedPercent":72,"windowDurationMins":10080}}}}"""));
        Check(buckets.Count == 3, "All returned model buckets are retained without duplicates");
        Check(buckets[0].Id.StartsWith("codex:"), "Shared quota comes first");
        Check(buckets[2].Label == "Model X · 5 godzin", "Scoped bucket uses server name");
        Check(UsageParser.Codex(Parse("{} ")).Count == 0, "Missing quota is not fabricated");
        Check(UsageParser.Codex(Parse("""{"rateLimits":{"primary":{"usedPercent":140}}}"""))[0].Remaining == 0, "Clamp out-of-range percentages");
        Check(UsageParser.Codex(Parse("""{"rateLimits":{"primary":{"usedPercent":null}}}""" )).Count == 0, "Null quota is absent");
        var native = UsageParser.Claude(Parse("""{"rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1999999999},"seven_day":{"used_percentage":41.2,"resets_at":1999999998}}}"""), true);
        Check(native.Count == 2 && native[0].Remaining == 76.5, "Claude official status line format");
        Check(native[0].ResetAt == 1999999999, "Native Unix seconds");
        var api = UsageParser.Claude(Parse("""{"five_hour":{"utilization":37,"resets_at":"2030-02-08T04:59:59+00:00"},"seven_day":null,"seven_day_sonnet":{"utilization":0}}"""), false);
        Check(api.Count == 2 && api[0].Remaining == 63, "Claude API and optional scoped quotas");
        Check(api[0].ResetAt.HasValue, "ISO reset timestamp");
        Check(UsageParser.Claude(Parse("{}"), true).Count == 0, "Absent Claude rate_limits");
        Check(UsageParser.Claude(Parse("""{"meters":[{"kind":"session","percent":5},{"kind":"weekly_all","percent":14},{"kind":"weekly_scoped","percent":16,"scope":{"model":{"display_name":"Fable"}}}]}"""), false)[2].Label == "Tydzień · Fable", "Scoped meter array");
        var expired = new UsageWindow("x", "x", 30, 1, 300);
        Check(expired.Expired && expired.ResetText.Contains("minął"), "Expired quota waits for actual data");
        Check(new UsageWindow("x", "x", 0, null, 0).ResetText == "Brak czasu resetu", "Missing reset time");
        var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var now = DateTimeOffset.Parse("2026-10-04T16:50:00+02:00");
        ResetPresentation Reset(string instant) => ResetFormatter.Format(DateTimeOffset.Parse(instant).ToUnixTimeSeconds(), now, warsaw);
        var sameDay = Reset("2026-10-04T18:30:00+02:00");
        Check(sameDay.Countdown == "Reset za 1 godz. 40 min" && sameDay.Calendar == "Dzisiaj, 4 października · 18:30", "Readable same-day reset in Polish");
        Check(Reset("2026-10-05T00:10:00+02:00").ShortCalendar == "Jutro · 00:10", "Tomorrow follows local calendar, not 24-hour duration");
        var weeklyReset = Reset("2026-10-07T14:00:00+02:00");
        Check(weeklyReset.Calendar == "Środa, 7 października · 14:00" && weeklyReset.ShortCalendar == "Środa · 14:00", "Weekly reset includes weekday and exact time");
        Check(Reset("2026-10-04T14:50:30Z").Countdown == "Reset za <1 min", "Sub-minute countdown never reads zero minutes");
        Check(Reset("2026-10-04T14:49:59Z").Countdown.Contains("minął"), "Past reset waits for fresh data");
        Check(Reset("2027-01-02T10:00:00+01:00").Calendar.Contains("2027"), "Reset in another year includes year");
        var dstNow = DateTimeOffset.Parse("2026-10-25T00:30:00Z");
        var dst = ResetFormatter.Format(DateTimeOffset.Parse("2026-10-25T01:30:00Z").ToUnixTimeSeconds(), dstNow, warsaw);
        Check(dst.Countdown == "Reset za 1 godz." && dst.ShortCalendar == "Dzisiaj · 02:30", "DST repeated hour retains accurate elapsed time");
        var initialize = Parse(ClaudeAuth.Initialize);
        Check(UsageParser.Text(initialize, "type") == "control_request"
            && UsageParser.Text(initialize.GetProperty("request"), "subtype") == "initialize"
            && !initialize.TryGetProperty("message", out _), "Auth refresh sends initialization only, no user prompt");
        var auth = ClaudeAuth.StartInfo("claude.exe", "C:/test-config");
        Check(auth.CreateNoWindow && auth.ArgumentList.Contains("--safe-mode")
            && auth.ArgumentList.Contains("--strict-mcp-config") && auth.ArgumentList.Contains("--no-session-persistence"),
            "Auth bootstrap disables custom hooks, MCP and session persistence");
        string Devin(string fields, string plan = "Pro") => "{\"userStatus\":{\"planStatus\":{\"planInfo\":{\"planName\":\"" + plan + "\",\"billingStrategy\":\"BILLING_STRATEGY_QUOTA\"}," + fields + "}}}";
        var devin = AdditionalUsageParser.Devin(Parse(Devin("\"dailyQuotaRemainingPercent\":64,\"weeklyQuotaRemainingPercent\":29,\"dailyQuotaResetAtUnix\":\"1999999999\",\"weeklyQuotaResetAtUnix\":\"2000000000\"")));
        Check(devin.Windows.Count == 2 && devin.Windows[0].Remaining == 64 && devin.Windows[1].Remaining == 29, "Devin remaining quota does not invert used percentage");
        Check(devin.Windows[0].ResetAt == 1999999999, "Devin protobuf string timestamps");
        Check(AdditionalUsageParser.Devin(Parse(Devin("\"dailyQuotaResetAtUnix\":1999999999"))).Windows.Single().Remaining == 0, "Omitted protobuf zero is exhausted quota with a real reset");
        Check(AdditionalUsageParser.Devin(Parse(Devin("\"weeklyQuotaRemainingPercent\":80,\"weeklyQuotaResetAtUnix\":2000000000", "Max"))).Windows.Single().Label == "Tydzień", "Devin Max absent daily quota is hidden");
        Check(AdditionalUsageParser.Devin(Parse(Devin("\"dailyQuotaRemainingPercent\":-1,\"dailyQuotaResetAtUnix\":2000000000"))).Windows.Count == 0, "Unlimited sentinel is not a percentage");
        Check(AdditionalUsageParser.Devin(Parse(Devin("\"dailyQuotaRemainingPercent\":100,\"dailyQuotaResetAtUnix\":0"))).Windows.Count == 0, "Inactive Devin quota is not fabricated");
        Check(DevinProvider.AllowedServer("https://server.self-serve.windsurf.com") && !DevinProvider.AllowedServer("http://server.self-serve.windsurf.com")
            && !DevinProvider.AllowedServer("https://server.self-serve.windsurf.com.evil.example") && !DevinProvider.AllowedServer("https://server.self-serve.windsurf.com/path"), "Devin credentials only go to the supported HTTPS origin");
        var air = AdditionalUsageParser.Air(Parse("""{"licenseName":"AI Pro","usedDollars":"3.5","maxDollars":"10","refillNext":1999999999000}"""));
        Check(air.Windows.Single().Remaining == 65 && air.Windows[0].ResetAt == 1999999999, "JetBrains string amounts and millisecond reset timestamp");
        Check(AdditionalUsageParser.Air(Parse("""{"maxDollars":10,"tariffQuota":{"maximum":"10","available":"10"}}""")).Windows.Single().Remaining == 100, "JetBrains omitted zero usage uses explicit available quota");
        Check(AdditionalUsageParser.Air(Parse("""{"usedDollars":15,"maxDollars":10}""")).Windows.Single().Remaining == 0, "JetBrains exhausted quota is clamped");
        Check(AdditionalUsageParser.Air(Parse("""{"usedDollars":5,"maxDollars":0}""")).Windows.Count == 0, "Unlimited or absent JetBrains limit is not fabricated");
        Check(AdditionalUsageParser.Air(Parse("""{"usedDollars":5,"maxDollars":10}""")).Windows.Single().ResetAt is null, "JetBrains missing reset does not guess a date");
        Check(AdditionalUsageParser.Air(Parse("""{"maxDollars":10}""")).Windows.Count == 0, "Missing JetBrains usage is not zero");
        var separate = AdditionalUsageParser.Air(Parse("""{"licenseName":"Personal name","usedDollars":"0.00","maxDollars":"51.12","tariffQuota":{"current":"0.00","maximum":"35.00","available":"35.00"},"topUpQuota":{"current":"0.00","maximum":"16.12","available":"16.12"},"refillNext":1999999999000}"""));
        Check(separate.Windows.Count == 2 && separate.Windows.All(w => w.Remaining == 100), "Monthly JetBrains allowance is not divided by monthly plus top-up credits");
        Check(separate.Windows[0].BalanceText == "35 / 35 kredytów AI" && separate.Windows[1].BalanceText == "16,12 / 16,12 kredytów AI", "Real JetBrains balances use readable Polish numbers");
        Check(separate.Windows[0].ResetAt.HasValue && !separate.Windows[1].Renews && separate.Windows[1].ResetAt is null, "Top-up credits do not inherit the monthly reset");
        Check(separate.Plan == "JetBrains AI", "License holder's name is not used as a plan name");
        const string summaryJson = """{"response":{"groups":[{"displayName":"Gemini Models","description":"Models within this group: Gemini Flash, Gemini Pro","buckets":[{"bucketId":"gemini-weekly","window":"weekly","remainingFraction":1,"resetTime":"2030-02-15T04:59:59Z"},{"bucketId":"gemini-5h","window":"5h","remainingFraction":1,"resetTime":"2030-02-08T04:59:59Z"}]},{"displayName":"Claude and GPT models","description":"Models within this group: Claude Opus, Claude Sonnet, GPT-OSS","buckets":[{"bucketId":"3p-weekly","window":"weekly","remainingFraction":0.94,"resetTime":"2030-02-14T04:59:59Z"},{"bucketId":"3p-5h","window":"5h","remainingFraction":0.89,"resetTime":"2030-02-08T04:45:00Z"}]}]}}""";
        var ag = AntigravityUsageParser.Parse(Parse(summaryJson));
        Check(ag.Windows.Count == 4 && ag.Windows.Select(w => w.Group).Distinct().SequenceEqual(new[] { "Gemini", "Claude / GPT" }), "Antigravity has two explicit groups with four independent meters");
        Check(ag.Windows.Select(w => w.Label).SequenceEqual(new[] { "Tydzień", "5 godzin", "Tydzień", "5 godzin" })
            && ag.Windows.Select(w => w.Minutes).SequenceEqual(new[] { 10080, 300, 10080, 300 }), "Antigravity separates weekly and five-hour windows");
        Check(Math.Abs(ag.Windows[2].Remaining - 94) < 0.001 && Math.Abs(ag.Windows[3].Remaining - 89) < 0.001 && ag.Windows[0].Remaining == 100, "Antigravity each family and cadence keeps its actual remaining fraction");
        Check(ag.Windows[0].ResetAt == DateTimeOffset.Parse("2030-02-15T04:59:59Z").ToUnixTimeSeconds()
            && ag.Windows[1].ResetAt == DateTimeOffset.Parse("2030-02-08T04:59:59Z").ToUnixTimeSeconds()
            && ag.Windows[2].ResetAt != ag.Windows[0].ResetAt, "Antigravity independent weekly and session resets are preserved");
        Check(ag.Windows[2].DetailText == "Wspólna pula: Claude Opus, Claude Sonnet, GPT-OSS" && ag.Error is null, "Antigravity model membership is explained without duplicating bars");
        Check(AntigravityUsageParser.Parse(Parse(summaryJson).GetProperty("response")).Windows.Count == 4, "Antigravity accepts the unwrapped quota summary");
        ProviderState AgBuckets(string buckets) => AntigravityUsageParser.Parse(Parse("{\"groups\":[{\"displayName\":\"Gemini Models\",\"buckets\":" + buckets + "}]}"));
        Check(AgBuckets("""[{"bucketId":"zero","window":"weekly","resetTime":"2030-02-15T04:59:59Z"}]""").Windows.Single().Remaining == 0, "Antigravity summary omitted protobuf zero with an actual reset is exhausted");
        var agNoReset = AgBuckets("""[{"bucketId":"explicit-zero","window":"5h","remainingFraction":0},{"bucketId":"numeric-string","window":"weekly","remainingFraction":"0.25"}]""");
        Check(agNoReset.Windows.Count == 2 && agNoReset.Windows.Single(w => w.Minutes == 300).Remaining == 0
            && agNoReset.Windows.Single(w => w.Minutes == 10080).Remaining == 25 && agNoReset.Windows.All(w => w.ResetAt is null), "Antigravity summary accepts explicit zero and numeric strings without inventing resets");
        Check(AgBuckets("""[{"bucketId":"empty","window":"weekly"},{"bucketId":"bad-reset","window":"weekly","resetTime":0},{"bucketId":"null","window":"5h","remainingFraction":null,"resetTime":"2030-02-08T04:59:59Z"},{"bucketId":"negative","window":"5h","remainingFraction":-1},{"bucketId":"high","window":"5h","remainingFraction":1.1},{"bucketId":"disabled","window":"5h","disabled":true,"remainingFraction":1}]""").Windows.Count == 0, "Antigravity invalid, missing and disabled summary quotas are not fabricated");
        Check(AgBuckets("""[{"bucketId":"same","window":"weekly","remainingFraction":0.5},{"bucketId":"same","window":"weekly","remainingFraction":0.5}]""").Windows.Count == 1, "Duplicate Antigravity summary buckets are displayed once");
        Check(AgBuckets("""[{"bucketId":"session","window":"5h","remainingFraction":0.5}]""").Windows.Single().Minutes == 300, "Missing weekly summary does not inherit the session percentage");
        var reordered = AgBuckets("""[{"bucketId":"gemini-5h","window":"5h","remainingFraction":1},{"bucketId":"gemini-weekly","window":"WEEKLY","remainingFraction":1}]""");
        Check(reordered.Windows[0].Minutes == 10080, "Antigravity cadence ordering stays stable when the server reorders buckets");
        Check(AntigravityUsageParser.Parse(Parse("{\"groups\":[]}")).Windows.Count == 0, "Empty valid Antigravity summary does not trigger invented quotas");
        var agTier = Parse("""{"userStatus":{"email":"private@example.test","userTier":{"name":"Google AI Pro"},"planStatus":{"planInfo":{"planName":"Inherited plan"}}}}""");
        Check(AntigravityUsageParser.Plan(agTier) == "Google AI Pro" && !JsonSerializer.Serialize(ag).Contains("private@example.test"), "Antigravity uses its own plan without retaining account identity");
        ProviderState AgModels(string models) => AntigravityUsageParser.Parse(Parse("{\"clientModelConfigs\":" + models + "}"));
        const string argonGroup = """{"displayName":"Gemini 4 Argon","buckets":[{"bucketId":"argon-weekly","displayName":"Gemini 4 Argon Weekly Limit","window":"weekly","remainingFraction":0.48,"resetTime":"2030-02-22T04:59:59Z"},{"bucketId":"argon-5h","window":"5h","remainingFraction":0.10}]}""";
        var withArgon = AntigravityUsageParser.Parse(Parse(JsonSerializer.Serialize(new { response = new {
            groups = new[] { Parse(argonGroup) }.Concat(Parse(summaryJson).GetProperty("response").GetProperty("groups").EnumerateArray()).ToArray() } })));
        Check(withArgon.Windows.Count == 5 && withArgon.Windows.Select(w => w.Group).Distinct().SequenceEqual(new[] { "Gemini", "Claude / GPT", "Gemini 4 Argon" }), "Argon is a separate last group without duplicating the existing pools");
        var argon = withArgon.Windows.Last();
        Check(argon.Id == "quota:argon-weekly" && argon.Label == "Tydzień" && argon.Minutes == 10080
            && argon.Remaining == 48 && withArgon.Windows[0].Remaining == 100, "Argon has only its own weekly percentage, independent of Gemini");
        Check(argon.ResetAt == DateTimeOffset.Parse("2030-02-22T04:59:59Z").ToUnixTimeSeconds()
            && argon.ResetAt != withArgon.Windows[0].ResetAt, "Argon preserves its own reset instead of borrowing the Gemini reset");
        var legacyArgon = AgModels("""[{"label":"Gemini Flash","quotaInfo":{"remainingFraction":1}},{"label":"Gemini 4 Argon (Medium)","quotaInfo":{"remainingFraction":0.10}}]""");
        Check(legacyArgon.Windows.Single().Group == "Gemini" && legacyArgon.Windows[0].Remaining == 100, "Legacy Argon cannot consume the shared Gemini meter or fabricate a five-hour Argon limit");
        var legacy = AgModels("""[{"label":"Gemini X (High)","modelId":"gemini-high","quotaInfo":{"remainingFraction":0.75,"resetTime":"2030-02-08T04:59:59Z"}},{"label":"Gemini X (Low)","modelId":"gemini-low","quotaInfo":{"remainingFraction":0.75,"resetTime":"2030-02-08T04:59:59Z"}},{"label":"Claude X","quotaInfo":{"remainingFraction":0.50,"resetTime":"2030-02-08T04:45:00Z"}},{"label":"GPT-OSS","quotaInfo":{"remainingFraction":0.50,"resetTime":"2030-02-08T04:45:00Z"}}]""");
        Check(legacy.Windows.Count == 2 && legacy.Windows[0].Remaining == 75 && legacy.Windows[1].Remaining == 50
            && legacy.Windows.All(w => w.Minutes == 300) && legacy.Error is not null, "Antigravity legacy fallback merges shared session pools and reports missing weekly data");
        Check(AgModels("""[{"label":"Claude A","quotaInfo":{"remainingFraction":0.25}},{"label":"Claude B","quotaInfo":{"remainingFraction":0.75}}]""").Windows.Single().Remaining == 25, "Divergent legacy models use the constrained quota instead of adding or averaging");
        Check(AgModels("""[{"label":"Gemini Empty","quotaInfo":{}},{"label":"Gemini Disabled","disabled":true,"quotaInfo":{"remainingFraction":1}},{"label":"Gemini Absent"}]""").Windows.Count == 0, "Empty and disabled Antigravity model quotas are not fabricated");
        Check(AgModels("""[{"label":"Gemini Exhausted","quotaInfo":{"resetTime":"2030-02-08T04:59:59Z"}}]""").Windows.Single().Remaining == 0, "Antigravity legacy omitted protobuf zero with a reset is exhausted");
        Check(AgModels("""[{"label":"Gemini Null","quotaInfo":{"remainingFraction":null,"resetTime":"2030-02-08T04:59:59Z"}},{"label":"Gemini Negative","quotaInfo":{"remainingFraction":-1}},{"label":"Gemini High","quotaInfo":{"remainingFraction":1.5}}]""").Windows.Count == 0, "Invalid Antigravity legacy fractions are not shown as real limits");
        Check(AgModels("""[{"label":"Gemini Unknown","quotaInfo":{"resetTime":0}},{"label":"Gemini Before epoch","quotaInfo":{"resetTime":"1960-01-01T00:00:00Z"}}]""").Windows.Count == 0, "Invalid reset cannot turn a missing Antigravity fraction into exhausted quota");
        var cachedLegacy = JsonSerializer.Deserialize<UsageWindow>("{\"Id\":\"old\",\"Label\":\"Old model\",\"Used\":25,\"Minutes\":0}");
        Check(cachedLegacy?.Group is null && JsonSerializer.Deserialize<UsageWindow>(JsonSerializer.Serialize(ag.Windows[0]))?.Group == "Gemini", "Antigravity grouped cache roundtrip preserves old cache compatibility");
        Check(AntigravityEndpoint.Argument("server --csrf_token \"test value\" --https_server_port=1234", "csrf_token") == "test value"
            && AntigravityEndpoint.Argument("server --https_server_port=1234", "https_server_port") == "1234"
            && AntigravityEndpoint.Argument("server --other_csrf_token token", "csrf_token") is null, "Antigravity quoted and equals arguments match exact flag names");
        Check(AntigravityEndpoint.IsAntigravity("C:/Apps/Antigravity/resources/bin/language_server.exe", "server --https_server_port 0")
            && !AntigravityEndpoint.IsAntigravity("C:/Apps/Devin/language_server_windows_x64.exe", "server --ide_name devin")
            && !AntigravityEndpoint.IsAntigravity("C:/Apps/Antigravity/Antigravity.exe", "--csrf_token token"), "Antigravity discovery excludes other providers and non-server processes");
        string agRpc = "/exa.language_server_pb.LanguageServerService/GetUserStatus";
        Check(AntigravityProvider.IsLocalQuotaUri(new Uri("https://127.0.0.1:1234" + agRpc))
            && AntigravityProvider.IsLocalQuotaUri(new Uri("https://127.0.0.1:1234/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary"))
            && !AntigravityProvider.IsLocalQuotaUri(new Uri("https://example.com:1234" + agRpc))
            && !AntigravityProvider.IsLocalQuotaUri(new Uri("https://127.0.0.1:1234/GenerateContent"))
            && !AntigravityProvider.IsLocalQuotaUri(new Uri("https://user@127.0.0.1:1234" + agRpc))
            && !AntigravityProvider.IsLocalQuotaUri(new Uri("https://127.0.0.1:1234" + agRpc + "?redirect=1")), "Antigravity TLS exception is restricted to loopback quota RPCs");
        count += FeatureTests.Run();
        count += LocalizationTests.Run();
        Console.WriteLine($"{count} tests passed."); return 0;
    }
}
