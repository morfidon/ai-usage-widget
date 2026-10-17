using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AIUsageWidget;

public sealed record TokenEntry(string Provider, string Project, string Session, string Model, DateTimeOffset At,
    long Input, long Cached, long CacheWrite, long Output, bool Subagent)
{
    public long Total => Input + Cached + CacheWrite + Output;
}
public sealed record ToolEntry(string Provider, string Kind, string Name, DateTimeOffset At);
public sealed record AnalyticsSnapshot(List<TokenEntry> Entries, List<ToolEntry> Tools, DateTimeOffset At, bool Loading, int Skipped)
{
    public static AnalyticsSnapshot Empty => new(new(), new(), DateTimeOffset.MinValue, true, 0);
}

// Incremental JSONL reader: only complete lines, bounded memory, no copies of prompts or tool output.
public sealed class JsonlCursor : IDisposable
{
    public long Offset { get; private set; }
    readonly MemoryStream pending = new();
    bool oversized;
    public int Skipped { get; private set; }
    public bool Read(string path, long budget, Action<JsonElement> consume, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < Offset) { Offset = 0; pending.SetLength(0); oversized = false; }
        stream.Position = Offset;
        byte[] buffer = new byte[65536]; long read = 0; int n;
        while (read < budget && (n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, budget - read))) > 0)
        {
            token.ThrowIfCancellationRequested(); read += n; Offset += n;
            int pos = 0;
            while (pos < n)
            {
                int next = Array.IndexOf(buffer, (byte)'\n', pos, n - pos);
                int end = next < 0 ? n : next;
                if (!oversized && pending.Length + end - pos <= 4_000_000) pending.Write(buffer, pos, end - pos);
                else { oversized = true; pending.SetLength(0); }
                if (next < 0) break;
                if (!oversized && pending.Length > 0)
                {
                    try { using var doc = JsonDocument.Parse(pending.GetBuffer().AsMemory(0, (int)pending.Length)); consume(doc.RootElement); }
                    catch (JsonException) { Skipped++; }
                }
                else if (oversized) Skipped++;
                pending.SetLength(0); oversized = false; pos = next + 1;
            }
        }
        return Offset < stream.Length;
    }
    public void Dispose() => pending.Dispose();
}

public sealed class TranscriptAnalytics : IDisposable
{
    sealed class FileData : IDisposable
    {
        public readonly JsonlCursor Cursor = new();
        public readonly Dictionary<string, TokenEntry> Entries = new();
        public readonly Dictionary<string, ToolEntry> Tools = new();
        public string Project = "Nieznany projekt", Session = "", Model = "nieznany";
        public bool Subagent;
        public long LastInput, LastCached, LastOutput;
        public void Dispose() => Cursor.Dispose();
    }
    readonly Dictionary<string, FileData> files = new(StringComparer.OrdinalIgnoreCase);
    readonly Settings settings;
    readonly (string Path, string Provider)[] sources;
    public TranscriptAnalytics(Settings settings, string? claudeProjects = null, string? codexSessions = null, string? codexArchives = null)
    {
        this.settings = settings;
        sources = new[] { (claudeProjects ?? Path.Combine(LocalPaths.Claude(settings), "projects"), "Claude"),
            (codexSessions ?? Path.Combine(LocalPaths.Codex, "sessions"), "Codex"),
            (codexArchives ?? Path.Combine(LocalPaths.Codex, "archived_sessions"), "Codex") };
    }
    public AnalyticsSnapshot Scan(CancellationToken token)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-7); long budget = 64_000_000; bool more = false; int skipped = 0;
        var candidates = new List<(string Path, string Provider, DateTime Modified)>();
        foreach (var source in sources)
        {
            try
            {
                if (Directory.Exists(source.Path)) candidates.AddRange(Directory.EnumerateFiles(source.Path, "*.jsonl", SearchOption.AllDirectories)
                    .Select(p => (p, source.Provider, File.GetLastWriteTimeUtc(p))).Where(p => p.Item3 >= since.UtcDateTime));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
        }
        // Files already partly read first, then newest. Every scan has a bounded IO budget.
        foreach (var file in candidates.OrderByDescending(f => f.Modified))
        {
            token.ThrowIfCancellationRequested();
            if (budget <= 0) { more = true; break; }
            if (!files.TryGetValue(file.Path, out var data))
            {
                data = new FileData { Session = Path.GetFileNameWithoutExtension(file.Path), Subagent = file.Path.Contains("subagent", StringComparison.OrdinalIgnoreCase) };
                files[file.Path] = data;
            }
            try
            {
                long length = new FileInfo(file.Path).Length;
                if (length < data.Cursor.Offset) { data.Dispose(); files[file.Path] = data = new FileData(); }
                long before = data.Cursor.Offset;
                more |= data.Cursor.Read(file.Path, Math.Min(budget, 16_000_000), e => Parse(data, file.Provider, e, since), token);
                budget -= data.Cursor.Offset - before;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
        }
        var livePaths = candidates.Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files.Keys.Where(p => !livePaths.Contains(p)).ToList()) { files[path].Dispose(); files.Remove(path); }
        foreach (var f in files.Values)
        {
            foreach (var k in f.Entries.Where(e => e.Value.At < since).Select(e => e.Key).ToList()) f.Entries.Remove(k);
            foreach (var k in f.Tools.Where(e => e.Value.At < since).Select(e => e.Key).ToList()) f.Tools.Remove(k);
        }
        return new(files.Values.SelectMany(f => f.Entries).GroupBy(e => (e.Value.Provider, e.Key)).Select(g => g.MaxBy(e => e.Value.Total).Value).ToList(),
            files.Values.SelectMany(f => f.Tools).GroupBy(e => (e.Value.Provider, e.Key)).Select(g => g.First().Value).ToList(),
            DateTimeOffset.Now, more, skipped + files.Values.Sum(f => f.Cursor.Skipped));
    }
    static long N(JsonElement e, string key) => (long)Math.Clamp(UsageParser.Number(e, key) ?? 0, 0, 1e12);
    static void Parse(FileData f, string provider, JsonElement e, DateTimeOffset since)
    {
        string? type = UsageParser.Text(e, "type");
        var p = UsageParser.Object(e, "payload") ?? e;
        if (type == "session_meta")
        {
            f.Project = UsageParser.Text(p, "cwd") ?? f.Project; f.Session = UsageParser.Text(p, "id") ?? f.Session;
            f.Subagent |= p.TryGetProperty("source", out var source) && source.GetRawText().Contains("subagent", StringComparison.OrdinalIgnoreCase);
        }
        if (type == "turn_context") f.Model = UsageParser.Text(p, "model") ?? f.Model;
        f.Project = UsageParser.Text(e, "cwd") ?? f.Project;
        f.Session = UsageParser.Text(e, "sessionId") ?? f.Session;
        if (!DateTimeOffset.TryParse(UsageParser.Text(e, "timestamp"), out var at)) return;
        if (provider == "Claude" && type == "assistant" && UsageParser.Object(e, "message") is JsonElement m)
        {
            string id = UsageParser.Text(m, "id") ?? UsageParser.Text(e, "uuid") ?? "";
            if (id.Length == 0 || at < since) return;
            string model = UsageParser.Text(m, "model") ?? "nieznany";
            if (UsageParser.Object(m, "usage") is JsonElement u)
            {
                var entry = new TokenEntry(provider, f.Project, f.Session, model, at, N(u, "input_tokens"), N(u, "cache_read_input_tokens"),
                    N(u, "cache_creation_input_tokens"), N(u, "output_tokens"), f.Subagent);
                if (f.Entries.TryGetValue(id, out var old)) entry = entry with
                { At = old.At, Input = Math.Max(old.Input, entry.Input), Cached = Math.Max(old.Cached, entry.Cached), CacheWrite = Math.Max(old.CacheWrite, entry.CacheWrite), Output = Math.Max(old.Output, entry.Output) };
                if (entry.Total > 0) f.Entries[id] = entry;
            }
            if (m.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var c in content.EnumerateArray())
                {
                    if (UsageParser.Text(c, "type") != "tool_use") continue;
                    string name = UsageParser.Text(c, "name") ?? "";
                    string toolId = UsageParser.Text(c, "id") ?? id + name;
                    if (name.StartsWith("mcp__", StringComparison.Ordinal))
                        f.Tools[toolId] = new(provider, "MCP", name.Split("__").ElementAtOrDefault(1) ?? name, at);
                    else if (name == "Skill" && UsageParser.Object(c, "input") is JsonElement input)
                        f.Tools[toolId] = new(provider, "Umiejętności", SafeName(UsageParser.Text(input, "skill") ?? "skill"), at);
                    else if (name is "Agent" or "Task")
                    {
                        var agentInput = UsageParser.Object(c, "input");
                        f.Tools[toolId] = new(provider, "Subagenci", SafeName(agentInput.HasValue ? UsageParser.Text(agentInput.Value, "subagent_type") ?? "agent" : "agent"), at);
                    }
                }
        }
        else if (provider == "Codex" && type == "token_usage_record" && UsageParser.Object(p, "usage") is JsonElement u)
        {
            if (f.LastInput >= 0)
                foreach (var key in f.Entries.Keys.Where(k => k.StartsWith("legacy:")).ToList()) f.Entries.Remove(key);
            string id = UsageParser.Text(p, "response_id") ?? (UsageParser.Text(p, "turn_id") + ":" + (e.TryGetProperty("ordinal", out var ordinal) ? ordinal.ToString() : at.ToString("O")));
            if (at >= since)
            {
                long cached = N(u, "cached_input_tokens");
                f.Entries[id] = new(provider, f.Project, f.Session, f.Model, at, Math.Max(0, N(u, "input_tokens") - cached), cached,
                    N(u, "cache_write_input_tokens"), N(u, "output_tokens"), f.Subagent);
            }
            // Modern records are authoritative; legacy token_count is ignored for this file.
            f.LastInput = -1;
        }
        else if (provider == "Codex" && type == "event_msg" && UsageParser.Text(p, "type") == "token_count" && f.LastInput >= 0
            && UsageParser.Object(p, "info") is JsonElement info && UsageParser.Object(info, "total_token_usage") is JsonElement totals)
        {
            long input = N(totals, "input_tokens"), cached = N(totals, "cached_input_tokens"), output = N(totals, "output_tokens");
            if (at >= since && (input > f.LastInput || output > f.LastOutput))
                f.Entries["legacy:" + at.ToString("O")] = new(provider, f.Project, f.Session, f.Model, at,
                    Math.Max(0, input - f.LastInput - Math.Max(0, cached - f.LastCached)), Math.Max(0, cached - f.LastCached), 0,
                    Math.Max(0, output - f.LastOutput), f.Subagent);
            f.LastInput = input; f.LastCached = cached; f.LastOutput = output;
        }
    }
    static string SafeName(string value) => new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ':' or '.').Take(100).ToArray());
    public void Dispose() { foreach (var f in files.Values) f.Dispose(); files.Clear(); }
}
