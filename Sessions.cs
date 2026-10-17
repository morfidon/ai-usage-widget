using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIUsageWidget;

public sealed record LiveSession(string Provider, string Id, string Name, string Directory, string State, string Reason = "")
{
    public string StateLabel => State switch { "working" => "Pracuje", "waiting" => "Czeka na Ciebie", "idle" => "Bezczynna", _ => "Stan nieznany" };
}
public sealed record SessionSnapshot(List<LiveSession> Sessions, DateTimeOffset At, string? ClaudeError = null, string? CodexError = null)
{
    public static SessionSnapshot Empty => new(new(), DateTimeOffset.MinValue);
    public string? ErrorFor(string provider) => provider == "Claude" ? ClaudeError : CodexError;
}

public static class LocalPaths
{
    public static string Codex => Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public static string Claude(Settings s) => s.ClaudeDirectory ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    public static string? ClaudeExe() => new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe") }
        .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(d => Path.Combine(d, "claude.exe"))).FirstOrDefault(File.Exists);
}

// Fixed, SELECT-only queries; Windows supplies SQLite. No install or database changes.
public static class LocalSqlite
{
    public static List<string[]> Read(string path, string sql, int columns)
    {
        IntPtr db = IntPtr.Zero, stmt = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(path, out db, 1, IntPtr.Zero) != 0) throw new IOException("Local database unavailable");
            sqlite3_busy_timeout(db, 500);
            if (sqlite3_prepare_v2(db, sql, -1, out stmt, IntPtr.Zero) != 0) throw new IOException("Local schema unavailable");
            var rows = new List<string[]>(); int rc;
            while ((rc = sqlite3_step(stmt)) == 100)
            {
                var values = new string[columns];
                for (int i = 0; i < columns; i++) values[i] = sqlite3_column_bytes(stmt, i) <= 2_000_000
                    ? Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, i)) ?? "" : "";
                rows.Add(values);
            }
            if (rc != 101) throw new IOException("Local database busy");
            return rows;
        }
        finally { if (stmt != IntPtr.Zero) sqlite3_finalize(stmt); if (db != IntPtr.Zero) sqlite3_close(db); }
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string p, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr s);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr s, int c);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr s, int c);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr s);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db, int ms);
}

public sealed class SessionMonitor
{
    readonly Settings settings;
    public SessionMonitor(Settings settings) => this.settings = settings;
    public async Task<SessionSnapshot> Read(CancellationToken token)
    {
        var claude = ReadClaude(token); var codex = Task.Run(ReadCodex, token);
        await Task.WhenAll(claude, codex);
        return new(claude.Result.Item1.Concat(codex.Result.Item1).ToList(), DateTimeOffset.Now, claude.Result.Item2, codex.Result.Item2);
    }
    async Task<(List<LiveSession>, string?)> ReadClaude(CancellationToken outer)
    {
        string? exe = LocalPaths.ClaudeExe();
        if (exe is null) return (new(), "Nie znaleziono Claude Code");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outer); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Settings.ProjectDirectory };
        info.ArgumentList.Add("agents"); info.ArgumentList.Add("--json");
        info.Environment["CLAUDE_CONFIG_DIR"] = LocalPaths.Claude(settings);
        Process? p;
        try { p = Process.Start(info); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { return (new(), "Nie można odczytać sesji Claude"); }
        if (p is null) return (new(), "Nie można odczytać sesji Claude");
        using var ownedProcess = p;
        try
        {
            var output = p.StandardOutput.ReadToEndAsync(timeout.Token); var error = p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token); await error;
            if (p.ExitCode != 0) return (new(), "Odczyt sesji Claude niedostępny");
            using var doc = JsonDocument.Parse(await output);
            return (ParseClaude(doc.RootElement), null);
        }
        catch (Exception e) when (e is JsonException or OperationCanceledException or IOException)
        { outer.ThrowIfCancellationRequested(); return (new(), "Sesje Claude · brak aktualnego odczytu"); }
        finally { try { if (!p.HasExited) p.Kill(true); } catch { } }
    }
    public static List<LiveSession> ParseClaude(JsonElement root)
    {
        var result = new List<LiveSession>();
        if (root.ValueKind != JsonValueKind.Array) return result;
        foreach (var s in root.EnumerateArray())
        {
            string? state = UsageParser.Text(s, "state"), status = UsageParser.Text(s, "status");
            if (state is "done" or "stopped" or "failed") continue;
            string cwd = UsageParser.Text(s, "cwd") ?? "";
            result.Add(new("Claude", UsageParser.Text(s, "sessionId") ?? UsageParser.Text(s, "id") ?? cwd,
                UsageParser.Text(s, "name") ?? Path.GetFileName(cwd), cwd,
                state == "blocked" || status == "waiting" ? "waiting" : state == "working" || status == "busy" ? "working" : "idle",
                Reason(UsageParser.Text(s, "waitingFor") ?? "")));
        }
        return result.DistinctBy(s => s.Id).ToList();
    }
    static string Reason(string reason) => reason switch
    { "permission prompt" => "Zgoda na działanie", "input needed" => "Odpowiedź na pytanie", "sandbox request" => "Zgoda na dostęp", "dialog open" => "Otwarty dialog", "worker request" => "Decyzja dotycząca zadania", _ => reason };
    public static bool WriterIsLive(string root, string id)
    {
        if (!Guid.TryParse(id, out _)) return false;
        string path = Path.Combine(root, "thread-writer-locks", id + ".lock");
        if (!File.Exists(path)) return false;
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            try { f.Lock(0, 1); f.Unlock(0, 1); return false; }
            catch (IOException) { return true; }
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static (List<LiveSession>, string?) ReadCodex()
    {
        var result = new List<LiveSession>(); string root = LocalPaths.Codex;
        try
        {
            // A separate app-server's runtime list omits desktop sessions. Read the shared history
            // projection and require the OS writer lock to be held, so crashed turns do not stay busy.
            var threads = LocalSqlite.Read(Path.Combine(root, "state_5.sqlite"),
                "SELECT id,cwd,title FROM threads WHERE archived=0 ORDER BY updated_at DESC LIMIT 2000", 3).ToDictionary(r => r[0]);
            var turns = LocalSqlite.Read(Path.Combine(root, "thread_history_1.sqlite"),
                "SELECT thread_id,turn_id FROM thread_turns WHERE status='inProgress' ORDER BY started_at DESC LIMIT 500", 2);
            var active = turns.DistinctBy(r => r[0]).Where(r => threads.ContainsKey(r[0]) && WriterIsLive(root, r[0])).ToList();
            foreach (var turn in active)
            {
                var t = threads[turn[0]];
                // Look only at unfinished tool calls; never copy arguments, prompts or output.
                string id = turn[0].Replace("'", "''"), turnId = turn[1].Replace("'", "''");
                var items = LocalSqlite.Read(Path.Combine(root, "thread_history_1.sqlite"),
                    $"SELECT item_json FROM thread_items WHERE thread_id='{id}' AND turn_id='{turnId}' AND completed_at_ms IS NULL ORDER BY rollout_ordinal DESC LIMIT 12", 1);
                bool waiting = items.Any(r => IsWaitingItem(r[0]));
                result.Add(new("Codex", t[0], string.IsNullOrWhiteSpace(t[2]) ? Path.GetFileName(t[1]) : t[2], t[1], waiting ? "waiting" : "working",
                    waiting ? "Odpowiedź lub zgoda" : ""));
            }
            return (result, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { return (new(), "Monitor sesji Codex niedostępny · sprawdź wersję aplikacji"); }
    }
    public static bool IsWaitingItem(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json); var e = d.RootElement;
            string name = UsageParser.Text(e, "name") ?? UsageParser.Text(e, "tool") ?? "";
            string type = UsageParser.Text(e, "type") ?? "";
            string status = UsageParser.Text(e, "status") ?? "";
            return name.EndsWith("request_user_input", StringComparison.Ordinal) || type is "approvalRequest" or "requestUserInput"
                || status is "waitingOnApproval" or "waitingOnUserInput" or "waitingForApproval" or "awaitingInput"
                || e.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array && questions.GetArrayLength() > 0;
        }
        catch (JsonException) { return false; }
    }
}
