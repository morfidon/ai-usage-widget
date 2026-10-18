using System;
using System.Collections.Generic;
using System.Linq;

namespace AIUsageWidget;

public sealed record TokenTotals(long Input, long Cached, long CacheWrite, long Output, int Responses)
{
    public long Total => Input + Cached + CacheWrite + Output;
    public static TokenTotals From(IEnumerable<TokenEntry> entries)
    {
        long input = 0, cached = 0, write = 0, output = 0; int responses = 0;
        foreach (var e in entries) { input += e.Input; cached += e.Cached; write += e.CacheWrite; output += e.Output; responses++; }
        return new(input, cached, write, output, responses);
    }
}
public sealed record ModelUsage(string Name, TokenTotals Tokens, int Sessions);
public sealed record SessionUsage(string Id, TokenTotals Tokens, DateTimeOffset First, DateTimeOffset Last, List<ModelUsage> Models);
public sealed record ProjectBreakdown(TokenTotals Tokens, List<SessionUsage> Sessions, List<ModelUsage> Models)
{
    public static List<ModelUsage> RankModels(IEnumerable<TokenEntry> entries) => entries.GroupBy(e => e.Model)
        .Select(g => new ModelUsage(g.Key, TokenTotals.From(g), g.Select(e => e.Session).Distinct().Count()))
        .OrderByDescending(m => m.Tokens.Total).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
    public static ProjectBreakdown Build(IEnumerable<TokenEntry> entries, string provider, string project, DateTimeOffset since)
    {
        var rows = entries.Where(e => e.Provider == provider && e.Project == project && e.At >= since).ToList();
        var sessions = rows.GroupBy(e => e.Session)
            .Select(g => new SessionUsage(g.Key, TokenTotals.From(g), g.Min(e => e.At), g.Max(e => e.At), RankModels(g)))
            .OrderByDescending(s => s.Tokens.Total).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        return new(TokenTotals.From(rows), sessions, RankModels(rows));
    }
}
