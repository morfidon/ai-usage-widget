using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIUsageWidget;

// Windows' built-in SQLite library. Only read-only connections and fixed queries are used.
public static class DesktopState
{
    public static Dictionary<string, string> ReadDevin(string path)
    {
        IntPtr db = IntPtr.Zero, statement = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(path, out db, 1, IntPtr.Zero) != 0) throw new IOException();
            sqlite3_busy_timeout(db, 1000);
            const string sql = "SELECT key,value FROM ItemTable WHERE key IN ('windsurfAuthStatus','codeium.windsurf')";
            if (sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero) != 0) throw new IOException();
            var result = new Dictionary<string, string>();
            int rc;
            while ((rc = sqlite3_step(statement)) == 100)
            {
                if (sqlite3_column_bytes(statement, 1) > 2_000_000) throw new IOException();
                string key = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0)) ?? "";
                result[key] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 1)) ?? "";
            }
            if (rc != 101) throw new IOException();
            return result;
        }
        finally
        {
            if (statement != IntPtr.Zero) sqlite3_finalize(statement);
            if (db != IntPtr.Zero) sqlite3_close(db);
        }
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
}

public static class AdditionalUsageParser
{
    public static double? Numeric(JsonElement root, string key)
    {
        var number = UsageParser.Number(root, key);
        if (number.HasValue) return number;
        return double.TryParse(UsageParser.Text(root, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) ? value : null;
    }
    public static long? Unix(JsonElement root, string key)
    {
        var number = Numeric(root, key);
        return number is > 0 and < 253402300799 ? (long)number.Value : null;
    }
    public static ProviderState Devin(JsonElement root)
    {
        var user = UsageParser.Object(root, "userStatus");
        var status = user.HasValue ? UsageParser.Object(user.Value, "planStatus") : null;
        if (status is not JsonElement s) throw new InvalidDataException();
        var info = UsageParser.Object(s, "planInfo") ?? UsageParser.Object(root, "planInfo");
        string plan = info.HasValue ? UsageParser.Text(info.Value, "planName") ?? "" : "";
        string strategy = info.HasValue ? UsageParser.Text(info.Value, "billingStrategy") ?? "" : "";
        var windows = new List<UsageWindow>();
        // Protobuf omits zero-valued scalar fields. A real reset identifies an active quota.
        // Missing/reset=0 or negative sentinels must never become a fabricated 0%/100% bar.
        if (strategy == "BILLING_STRATEGY_QUOTA")
            foreach (var (id, label, minutes) in new[] { ("daily", "Dzień", 1440), ("weekly", "Tydzień", 10080) })
            {
                long? reset = Unix(s, id + "QuotaResetAtUnix");
                double? remaining = Numeric(s, id + "QuotaRemainingPercent");
                if (reset.HasValue && remaining is null) remaining = 0;
                if (reset.HasValue && remaining is >= 0 and <= 100)
                    windows.Add(new(id, label, 100 - remaining.Value, reset, minutes));
            }
        return new("Devin", plan, windows, DateTimeOffset.Now,
            windows.Count > 0 ? "Połączono · konto Devin" : "Konto nie udostępnia limitów procentowych", true);
    }
    public static ProviderState Air(JsonElement root)
    {
        var windows = new List<UsageWindow>();
        double? milliseconds = Numeric(root, "refillNext");
        long? reset = milliseconds is > 0 and < 253402300799000 ? (long)(milliseconds.Value / 1000) : null;
        string Balance(double remaining, double maximum) => remaining.ToString("0.##", CultureInfo.GetCultureInfo("pl-PL"))
            + " / " + maximum.ToString("0.##", CultureInfo.GetCultureInfo("pl-PL")) + " kredytów AI";
        void Bucket(JsonElement bucket, string id, string label, bool renews)
        {
            double? maximum = Numeric(bucket, "maximum"), available = Numeric(bucket, "available"), current = Numeric(bucket, "current");
            if (maximum is not > 0 || available is < 0 || current is < 0) return;
            if (available is null && current is >= 0) available = maximum.Value - current.Value;
            if (available is null) return;
            double remaining = Math.Clamp(available.Value, 0, maximum.Value);
            windows.Add(new(id, label, 100 - remaining / maximum.Value * 100, renews ? reset : null, 0,
                Balance(remaining, maximum.Value), renews));
        }
        if (UsageParser.Object(root, "tariffQuota") is JsonElement tariff)
            Bucket(tariff, "monthly", "Miesiąc", true);
        else
        {
            double? maximum = Numeric(root, "maxDollars"), used = Numeric(root, "usedDollars");
            if (maximum is > 0 && used is >= 0)
            {
                double remaining = Math.Clamp(maximum.Value - used.Value, 0, maximum.Value);
                windows.Add(new("credits", "Kredyty AI", 100 - remaining / maximum.Value * 100, reset, 0, Balance(remaining, maximum.Value)));
            }
        }
        if (UsageParser.Object(root, "topUpQuota") is JsonElement topup) Bucket(topup, "topup", "Dodatkowe", false);
        // licenseName may be the license owner's name, rather than a subscription tier.
        return new("JetBrains Air", "JetBrains AI", windows, DateTimeOffset.Now,
            windows.Count > 0 ? "Połączono · kredyty JetBrains AI" : "Konto nie udostępnia limitu procentowego", true);
    }
}

public sealed class DevinProvider : IDisposable
{
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    readonly SemaphoreSlim gate = new(1, 1);
    DateTimeOffset nextRead;
    ProviderState? last;
    public async Task<ProviderState> Fetch(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (last is not null && DateTimeOffset.UtcNow < nextRead) return last;
            nextRead = DateTimeOffset.UtcNow.AddMinutes(2);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "devin", "User", "globalStorage", "state.vscdb");
            if (!File.Exists(path)) return last = ProviderState.Empty("Devin") with { Status = "Nie znaleziono danych aplikacji Devin", SetupStatus = "login_required" };
            var state = DesktopState.ReadDevin(path);
            if (!state.TryGetValue("windsurfAuthStatus", out var authText))
                return last = ProviderState.Empty("Devin") with { Status = "Zaloguj się w aplikacji Devin", SetupStatus = "login_required" };
            using var auth = JsonDocument.Parse(authText);
            string? key = UsageParser.Text(auth.RootElement, "apiKey");
            if (string.IsNullOrWhiteSpace(key)) return last = ProviderState.Empty("Devin") with { Status = "Zaloguj się w aplikacji Devin", SetupStatus = "login_required" };
            string server = "https://server.self-serve.windsurf.com";
            if (state.TryGetValue("codeium.windsurf", out var configuration))
            {
                using var config = JsonDocument.Parse(configuration);
                server = UsageParser.Text(config.RootElement, "apiServerUrl") ?? server;
            }
            if (!AllowedServer(server)) return last = ProviderState.Empty("Devin") with { Status = "Nieobsługiwany serwer konta Devin" };
            string version = "1.99.3";
            string product = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Devin", "resources", "app", "package.json");
            if (File.Exists(product))
            {
                using var package = JsonDocument.Parse(File.ReadAllText(product));
                version = UsageParser.Text(package.RootElement, "version") ?? version;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, server.TrimEnd('/') + "/exa.seat_management_pb.SeatManagementService/GetUserStatus");
            request.Headers.Add("Connect-Protocol-Version", "1");
            request.Headers.UserAgent.ParseAdd("AIUsageWidget/1.1");
            request.Content = new StringContent(JsonSerializer.Serialize(new { metadata = new { apiKey = key,
                ideName = "devin", ideVersion = version, extensionVersion = version, locale = "en" } }), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return last = ProviderState.Empty("Devin") with { Status = "Zaloguj się ponownie w aplikacji Devin", SetupStatus = "login_required" };
            if (response.StatusCode == HttpStatusCode.TooManyRequests) nextRead = DateTimeOffset.UtcNow.AddMinutes(10);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return last = AdditionalUsageParser.Devin(document.RootElement);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return last = ProviderState.Empty("Devin") with { Status = "Brak odczytu Devin · ponowię połączenie" }; }
        finally { gate.Release(); }
    }
    public static bool AllowedServer(string server) => Uri.TryCreate(server, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0
        && uri.Fragment.Length == 0 && uri.AbsolutePath == "/"
        && (uri.Host == "server.self-serve.windsurf.com" || uri.Host == "server.codeium.com");
    public void Dispose() => http.Dispose();
}

public sealed class AirProvider : IDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    DateTimeOffset nextRead;
    ProviderState? last;
    public static string? Locate()
    {
        string bundled = Path.Combine(Settings.ProjectDirectory, "tools", "central", "central.exe");
        if (File.Exists(bundled)) return bundled;
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "central.exe");
        if (File.Exists(installed)) return installed;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d.Trim('"'), "central.exe")).FirstOrDefault(File.Exists);
    }
    public async Task<ProviderState> Fetch(CancellationToken outer)
    {
        await gate.WaitAsync(outer);
        Process? process = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outer);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            if (last is not null && DateTimeOffset.UtcNow < nextRead) return last;
            nextRead = DateTimeOffset.UtcNow.AddMinutes(2);
            string? exe = Locate();
            if (exe is null) return last = ProviderState.Empty("JetBrains Air") with { Status = "Brak narzędzia JetBrains Central", SetupStatus = "not_installed" };
            // Native Central manages its encrypted credentials and refreshes them itself.
            // This command reads quota; it neither starts the proxy nor wires any agent.
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Settings.ProjectDirectory };
            start.ArgumentList.Add("limit"); start.ArgumentList.Add("--json");
            process = Process.Start(start) ?? throw new IOException();
            process.StandardInput.Close();
            process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
            string json = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
            {
                bool signedIn = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jetbrains-central", "tokens.enc"));
                return last = ProviderState.Empty("JetBrains Air") with { Status = signedIn
                    ? "Brak odczytu JetBrains · ponowię połączenie" : "Dokończ logowanie JetBrains w przeglądarce", SetupStatus = signedIn ? "error" : "login_required" };
            }
            using var document = JsonDocument.Parse(json);
            return last = AdditionalUsageParser.Air(document.RootElement);
        }
        catch (OperationCanceledException) when (outer.IsCancellationRequested) { throw; }
        catch { return last = ProviderState.Empty("JetBrains Air") with { Status = "Brak odczytu JetBrains · ponowię połączenie" }; }
        finally
        {
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                process.Dispose();
            }
            gate.Release();
        }
    }
    public void Dispose() { }
}
