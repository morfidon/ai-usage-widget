using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIUsageWidget;

public sealed record ServiceHealth(string Provider, string Indicator, string Detail, DateTimeOffset At, bool Available)
{
    public static ServiceHealth Empty(string name) => new(name, "unknown", L.T("Oczekiwanie na status usługi"), DateTimeOffset.MinValue, false);
    public string Url => Provider == "Claude" ? "https://status.claude.com" : "https://status.openai.com";
    public bool Incident => Available && Indicator != "none";
}
public sealed class HealthMonitor : IDisposable
{
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    public async Task<ServiceHealth> Read(string name, CancellationToken token)
    {
        try
        {
            var url = name == "Claude" ? "https://status.claude.com/api/v2/summary.json" : "https://status.openai.com/api/v2/summary.json";
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1_000_000) throw new HttpRequestException();
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            if (bytes.Length > 1_000_000) throw new HttpRequestException();
            using var doc = JsonDocument.Parse(bytes);
            return Parse(name, doc.RootElement, DateTimeOffset.Now);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        { token.ThrowIfCancellationRequested(); return new(name, "unknown", L.T("Nie można odczytać statusu usługi"), DateTimeOffset.Now, false); }
    }
    public static ServiceHealth Parse(string name, JsonElement root, DateTimeOffset at)
    {
        var status = UsageParser.Object(root, "status");
        if (status is null) return new(name, "unknown", L.T("Brak danych statusu usługi"), at, false);
        string indicator = UsageParser.Text(status.Value, "indicator") ?? "unknown";
        var incidents = new List<string>();
        if (root.TryGetProperty("incidents", out var list) && list.ValueKind == JsonValueKind.Array)
            incidents.AddRange(list.EnumerateArray().Where(i => UsageParser.Text(i, "status") is not ("resolved" or "postmortem"))
                .Select(i => UsageParser.Text(i, "name") ?? L.T("Incydent")));
        return new(name, indicator, incidents.Count > 0 ? string.Join("\n", incidents)
            : indicator == "none" ? L.T("Brak zgłoszonych awarii") : UsageParser.Text(status.Value, "description") ?? L.T("Zakłócenia usług"), at, indicator != "unknown");
    }
    public void Dispose() => http.Dispose();
}
