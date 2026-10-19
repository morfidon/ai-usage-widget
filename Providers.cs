using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIUsageWidget;

public sealed class CodexProvider : IDisposable
{
    readonly Settings settings;
    Process? process;
    int sequence;
    readonly SemaphoreSlim gate = new(1, 1);
    public CodexProvider(Settings settings) => this.settings = settings;
    public string? Locate()
    {
        if (!string.IsNullOrEmpty(settings.CodexPath) && File.Exists(settings.CodexPath)) return settings.CodexPath;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "OpenAI", "Codex", "bin");
        if (Directory.Exists(root))
        {
            var found = Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (found != null) return found;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (File.Exists(Path.Combine(dir, "codex.exe"))) return Path.Combine(dir, "codex.exe");
        return null;
    }
    async Task<JsonElement> Request(string method, object? parameters, CancellationToken token)
    {
        int id = ++sequence;
        await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }));
        await process.StandardInput.FlushAsync(token);
        while (true)
        {
            string? line = await process.StandardOutput.ReadLineAsync(token);
            if (line is null) throw new IOException(L.T("Serwer Codex zakończył połączenie."));
            using var doc = JsonDocument.Parse(line);
            var e = doc.RootElement;
            if (UsageParser.Number(e, "id") != id) continue;
            if (UsageParser.Object(e, "error") is JsonElement error)
            {
                string message = UsageParser.Text(error, "message") ?? "";
                // Do not surface arbitrary backend text, which can contain credential-bearing URLs.
                throw new InvalidOperationException(message.Contains("auth", StringComparison.OrdinalIgnoreCase)
                    ? L.T("Zaloguj się w aplikacji Codex.") : L.T("Nie udało się odczytać limitów Codex."));
            }
            return e.GetProperty("result").Clone();
        }
    }
    async Task Start(CancellationToken token)
    {
        if (process is { HasExited: false }) return;
        DisposeProcess();
        string exe = Locate() ?? throw new FileNotFoundException(L.T("Nie znaleziono aplikacji Codex."));
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Settings.ProjectDirectory };
        start.ArgumentList.Add("app-server");
        start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("analytics.enabled=false");
        process = Process.Start(start) ?? throw new IOException(L.T("Nie można uruchomić serwera Codex."));
        process.ErrorDataReceived += (_, _) => { }; // Drain stderr without storing credential-bearing diagnostics.
        process.BeginErrorReadLine();
        await Request("initialize", new { clientInfo = new { name = "ai_usage_widget", title = "AI Usage Widget", version = "1.0.0" } }, token);
        await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
        await process.StandardInput.FlushAsync(token);
    }
    public async Task<ProviderState> Fetch(CancellationToken outer)
    {
        await gate.WaitAsync(outer);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outer);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            await Start(timeout.Token);
            var result = await Request("account/rateLimits/read", new { }, timeout.Token);
            var windows = UsageParser.Codex(result);
            var main = UsageParser.Object(result, "rateLimits");
            return new("Codex", main.HasValue ? UsageParser.Text(main.Value, "planType") ?? "" : "", windows,
                DateTimeOffset.Now, windows.Count == 0 ? L.T("Konto nie udostępnia limitów") : L.T("Połączono"), true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !outer.IsCancellationRequested)
        {
            DisposeProcess();
            string error = ex is OperationCanceledException ? L.T("Brak odpowiedzi · ponowię połączenie") :
                ex is FileNotFoundException or InvalidOperationException ? ex.Message : L.T("Brak połączenia · spróbuję ponownie");
            return new ProviderState("Codex", "", new(), null, error, false, error) {
                SetupStatus = ex is FileNotFoundException ? "not_installed" : L.Display(error) == L.T("Zaloguj się w aplikacji Codex.") ? "login_required" : "error" };
        }
        finally { gate.Release(); }
    }
    void DisposeProcess()
    {
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(true); } catch { }
        process.Dispose(); process = null;
    }
    public void Dispose() => DisposeProcess();
}

public sealed class ClaudeProvider : IDisposable
{
    readonly Settings settings;
    readonly HttpClient http;
    DateTimeOffset nextApi = DateTimeOffset.MinValue;
    int errors;
    public ClaudeProvider(Settings settings)
    {
        this.settings = settings;
        // Credentials must never follow an HTTP redirect to another host.
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    }
    public ProviderState? Bridge()
    {
        string path = Path.Combine(Settings.DataDirectory, "claude-live.json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var windows = UsageParser.Claude(root, true);
            if (windows.Count == 0) return null;
            var timestamp = UsageParser.Timestamp(root, "updated_at");
            if (timestamp is null) return null;
            return new("Claude", "", windows, DateTimeOffset.FromUnixTimeSeconds(timestamp.Value), L.T("Połączono · Claude Code"), true);
        }
        catch { return null; }
    }
    public async Task<ProviderState?> Fetch(CancellationToken token)
    {
        var bridge = Bridge();
        if (bridge is { Updated: not null } && (DateTimeOffset.Now - bridge.Updated.Value).TotalSeconds < settings.RefreshSeconds)
            return bridge;
        if (DateTimeOffset.Now < nextApi) return bridge;
        nextApi = DateTimeOffset.Now.AddSeconds(Math.Max(300, settings.RefreshSeconds));
        try
        {
            string dir = settings.ClaudeDirectory ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            var credentialPath = Path.Combine(dir, ".credentials.json");
            if (!File.Exists(credentialPath)) return Failure(bridge, L.T("Otwórz Claude Code i zaloguj się"), "login_required");
            var oauth = await LoadOAuth(credentialPath, token);
            if (oauth.HasValue && UsageParser.Number(oauth.Value, "expiresAt") is double expiry
                && expiry < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000)
            {
                // The installed first-party CLI owns refresh-token rotation and credential storage.
                // Initialize its control protocol without sending any user message or model request.
                await ClaudeAuth.Refresh(dir, token);
                oauth = await LoadOAuth(credentialPath, token);
            }
            string? access = oauth.HasValue ? UsageParser.Text(oauth.Value, "accessToken") : null;
            if (string.IsNullOrWhiteSpace(access)) return Failure(bridge, L.T("Otwórz Claude Code i zaloguj się"), "login_required");
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd("AIUsageWidget/1.0");
            using var response = await http.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                errors++;
                double delay = Math.Min(3600, 300 * Math.Pow(2, Math.Min(errors, 4)));
                var retry = response.Headers.RetryAfter;
                if (retry?.Delta is TimeSpan delta) delay = Math.Max(delay, delta.TotalSeconds);
                if (retry?.Date is DateTimeOffset date) delay = Math.Max(delay, (date - DateTimeOffset.Now).TotalSeconds);
                nextApi = DateTimeOffset.Now.AddSeconds(delay);
                return Failure(bridge, L.T("Claude ograniczył odczyt · ponowię później"));
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Failure(bridge, L.T("Czekam na dane z Claude Code · otwórz sesję"));
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var windows = UsageParser.Claude(doc.RootElement, false);
            errors = 0;
            if (windows.Count == 0) return Failure(bridge, L.T("Claude nie udostępnił limitów · czekam na sesję"));
            return new("Claude", UsageParser.Text(oauth!.Value, "subscriptionType") ?? "", windows, DateTimeOffset.Now, L.T("Połączono"), true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return Failure(bridge, L.T("Brak połączenia · czekam na Claude Code")); }
    }
    static ProviderState Failure(ProviderState? bridge, string message, string? setupStatus = null) => bridge is not null
        ? bridge with { Status = message, Connected = false, Error = message, SetupStatus = setupStatus }
        : new ProviderState("Claude", "", new(), null, message, false, message) { SetupStatus = setupStatus };
    public void Dispose() => http.Dispose();
    static async Task<JsonElement?> LoadOAuth(string path, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
        return UsageParser.Object(doc.RootElement, "claudeAiOauth")?.Clone();
    }
}

public static class ClaudeAuth
{
    public const string Initialize = "{\"type\":\"control_request\",\"request_id\":\"widget_auth\",\"request\":{\"subtype\":\"initialize\",\"hooks\":null}}";
    public static ProcessStartInfo StartInfo(string exe, string configDirectory)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Settings.ProjectDirectory };
        foreach (string arg in new[] { "--print", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--no-session-persistence", "--safe-mode", "--strict-mcp-config", "--max-budget-usd", "0.000001" })
            info.ArgumentList.Add(arg);
        info.Environment["CLAUDE_CONFIG_DIR"] = configDirectory;
        return info;
    }
    public static async Task Refresh(string configDirectory, CancellationToken outer)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? exe = new[] { Path.Combine(home, ".local", "bin", "claude.exe") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(dir => Path.Combine(dir, "claude.exe")))
            .FirstOrDefault(File.Exists);
        if (exe is null) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outer);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var process = Process.Start(StartInfo(exe, configDirectory));
        if (process is null) return;
        try
        {
            // Drain output rather than recording user metadata from the initialization response.
            process.ErrorDataReceived += (_, _) => { };
            process.OutputDataReceived += (_, _) => { };
            process.BeginErrorReadLine(); process.BeginOutputReadLine();
            await process.StandardInput.WriteLineAsync(Initialize);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!outer.IsCancellationRequested) { }
        finally { try { if (!process.HasExited) process.Kill(true); } catch { } }
    }
}
