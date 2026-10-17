using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AIUsageWidget;

public static class AntigravityUsageParser
{
    public static ProviderState Parse(JsonElement root)
    {
        var summary = UsageParser.Object(root, "response") ?? root;
        if (summary.ValueKind == JsonValueKind.Object && summary.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            return Summary(groups);
        var models = ParseModels(root);
        var pools = new List<UsageWindow>();
        foreach (var (prefix, group) in new[] { ("gemini", "Gemini"), ("3p", "Claude / GPT") })
        {
            var members = models.Windows.Where(w => Family(w.Label) == group).ToList();
            if (members.Count == 0) continue;
            // Legacy endpoints expose the session limit only. Never invent the weekly one.
            var tightest = members.OrderByDescending(w => w.Used).ThenBy(w => w.ResetAt ?? long.MaxValue).First();
            pools.Add(tightest with { Id = "quota:" + prefix + "-5h", Label = "5 godzin", Minutes = 300, Group = group,
                DetailText = "Wspólna pula: " + string.Join(", ", members.Select(w => w.Label)) });
        }
        return models with { Windows = pools, Status = "Odczyt zastępczy · limity 5-godzinne",
            Error = "Brak odczytu limitów tygodniowych" };
    }
    internal static string Plan(JsonElement root)
    {
        var user = UsageParser.Object(root, "userStatus");
        var tier = user.HasValue ? UsageParser.Object(user.Value, "userTier") : null;
        var status = user.HasValue ? UsageParser.Object(user.Value, "planStatus") : null;
        var info = status.HasValue ? UsageParser.Object(status.Value, "planInfo") : null;
        return (tier.HasValue ? UsageParser.Text(tier.Value, "name") : null)
            ?? (info.HasValue ? UsageParser.Text(info.Value, "planName") : null) ?? "";
    }
    static string? Family(string label) => label.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase)
        ? Regex.IsMatch(label, @"\bArgon\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "Gemini 4 Argon" : "Gemini"
        : label.StartsWith("Claude", StringComparison.OrdinalIgnoreCase) || label.StartsWith("GPT", StringComparison.OrdinalIgnoreCase) ? "Claude / GPT" : null;
    static ProviderState Summary(JsonElement groups)
    {
        var windows = new List<UsageWindow>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups.EnumerateArray())
        {
            string? name = UsageParser.Text(group, "displayName");
            if (string.IsNullOrWhiteSpace(name) || !group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) continue;
            string groupName = Family(name) ?? name;
            string description = UsageParser.Text(group, "description") ?? "";
            const string modelPrefix = "Models within this group: ";
            string detail = description.StartsWith(modelPrefix, StringComparison.OrdinalIgnoreCase)
                ? "Wspólna pula: " + description.Substring(modelPrefix.Length) : description;
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (bucket.ValueKind != JsonValueKind.Object || (bucket.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)) continue;
                string? id = UsageParser.Text(bucket, "bucketId");
                string cadence = (UsageParser.Text(bucket, "window") ?? "").ToLowerInvariant();
                // Argon has its own weekly allowance; do not merge it into Gemini or
                // synthesize a five-hour meter from the legacy per-model response.
                if (groupName == "Gemini 4 Argon" && cadence != "weekly") continue;
                string? label = cadence switch { "weekly" => "Tydzień", "5h" => "5 godzin", _ => UsageParser.Text(bucket, "displayName") };
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) continue;
                double? remaining = AdditionalUsageParser.Numeric(bucket, "remainingFraction");
                long? reset = UsageParser.Timestamp(bucket, "resetTime");
                if (reset is <= 0) reset = null;
                if (!bucket.TryGetProperty("remainingFraction", out _) && reset.HasValue) remaining = 0;
                if (remaining is not >= 0 or not <= 1 || !ids.Add(id)) continue;
                windows.Add(new("quota:" + id, label, 100 - remaining.Value * 100, reset,
                    cadence switch { "weekly" => 10080, "5h" => 300, _ => 0 }, Group: groupName, DetailText: detail));
            }
        }
        windows = windows.OrderBy(w => w.Group == "Gemini" ? 0 : w.Group == "Claude / GPT" ? 1 : 2)
            .ThenBy(w => w.Group, StringComparer.Ordinal).ThenBy(w => w.Minutes == 10080 ? 0 : 1).ThenBy(w => w.Id, StringComparer.Ordinal).ToList();
        return new("Antigravity", "", windows, DateTimeOffset.Now,
            windows.Count > 0 ? "Połączono · wspólne pule modeli" : "Konto nie udostępnia limitów", true);
    }
    static ProviderState ParseModels(JsonElement root)
    {
        var user = UsageParser.Object(root, "userStatus");
        var config = user.HasValue ? UsageParser.Object(user.Value, "cascadeModelConfigData") : null;
        var source = config ?? root;
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("clientModelConfigs", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException();
        string plan = Plan(root);
        var windows = new List<UsageWindow>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        // The server can reorder configs between reads. Keep the UI stable and Gemini first.
        foreach (var model in models.EnumerateArray()
            .OrderBy(m => (UsageParser.Text(m, "label") ?? "").StartsWith("Gemini", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => UsageParser.Text(m, "label"), StringComparer.OrdinalIgnoreCase))
        {
            if (model.ValueKind != JsonValueKind.Object || (model.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)) continue;
            string? label = UsageParser.Text(model, "label");
            if (string.IsNullOrWhiteSpace(label) || UsageParser.Object(model, "quotaInfo") is not JsonElement quota) continue;
            double? remaining = AdditionalUsageParser.Numeric(quota, "remainingFraction");
            long? reset = UsageParser.Timestamp(quota, "resetTime");
            if (reset is <= 0) reset = null;
            // Protobuf omits a zero fraction. Only an actual reset makes this an exhausted quota;
            // an empty quotaInfo (e.g. an unavailable model) must not become a fabricated bar.
            if (!quota.TryGetProperty("remainingFraction", out _) && reset.HasValue) remaining = 0;
            if (remaining is not >= 0 or not <= 1) continue;
            var alias = UsageParser.Object(model, "modelOrAlias");
            string? id = UsageParser.Text(model, "modelId");
            if (string.IsNullOrWhiteSpace(id)) id = alias.HasValue ? UsageParser.Text(alias.Value, "model") : null;
            if (string.IsNullOrWhiteSpace(id)) id = label;
            if (!ids.Add(id)) continue;
            windows.Add(new("model:" + id, label, 100 - remaining.Value * 100, reset, 0));
        }
        return new("Antigravity", plan, windows, DateTimeOffset.Now,
            windows.Count > 0 ? "Połączono · limity modeli Antigravity" : "Konto nie udostępnia limitów modeli", true);
    }
}

// Discovery uses only this user's running Antigravity language server. No credentials are
// read from Google/IDE storage or saved; the local CSRF token lives in memory for one fetch.
internal sealed class AntigravityEndpoint
{
    internal string Csrf { get; }
    internal int[] Ports { get; }
    AntigravityEndpoint(string csrf, int[] ports) { Csrf = csrf; Ports = ports; }
    internal static string? Argument(string command, string name)
    {
        var match = Regex.Match(command, @"(?:^|\s)--" + Regex.Escape(name) + "(?:=|\\s+)(?:\"([^\"]*)\"|'([^']*)'|([^\\s]+))", RegexOptions.CultureInvariant);
        return !match.Success ? null : match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
    }
    internal static bool IsAntigravity(string path, string command)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith("language_server", StringComparison.OrdinalIgnoreCase)) return false;
        return path.Replace('/', '\\').Contains("\\Antigravity\\", StringComparison.OrdinalIgnoreCase)
            || path.Replace('/', '\\').Contains("\\Antigravity IDE\\", StringComparison.OrdinalIgnoreCase)
            || Argument(command, "app_data_dir") is "antigravity" or "antigravity-ide"
            || Argument(command, "override_ide_name") == "antigravity";
    }
    internal static List<AntigravityEndpoint> Find(CancellationToken token)
    {
        var endpoints = new List<AntigravityEndpoint>();
        foreach (var process in Process.GetProcessesByName("language_server").Concat(Process.GetProcessesByName("language_server_windows_x64")))
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                IntPtr handle = OpenProcess(0x1000, false, process.Id);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var path = new StringBuilder(32768); int length = path.Capacity;
                    if (!QueryFullProcessImageName(handle, 0, path, ref length)) continue;
                    string command = CommandLine(handle);
                    if (!IsAntigravity(path.ToString(), command)) continue;
                    string? csrf = Argument(command, "csrf_token");
                    if (string.IsNullOrWhiteSpace(csrf) || csrf.Length > 4096 || csrf.Any(char.IsControl)) continue;
                    var ports = ListeningPorts(process.Id);
                    // Explicit ports, when nonzero, must also belong to this process.
                    if (int.TryParse(Argument(command, "https_server_port"), out int preferred) && ports.Remove(preferred)) ports.Insert(0, preferred);
                    if (ports.Count > 0) endpoints.Add(new(csrf, ports.Take(8).ToArray()));
                }
                catch (Exception e) when (e is not OperationCanceledException) { }
                finally { CloseHandle(handle); }
            }
        }
        return endpoints;
    }
    static string CommandLine(IntPtr handle)
    {
        NtQueryInformationProcess(handle, 60, IntPtr.Zero, 0, out int size);
        if (size <= 0 || size > 131072) return "";
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQueryInformationProcess(handle, 60, buffer, size, out _) != 0) return "";
            var text = Marshal.PtrToStructure<UnicodeString>(buffer);
            // Do not follow a pointer outside the returned buffer.
            long offset = text.Buffer.ToInt64() - buffer.ToInt64();
            return offset >= 0 && offset + text.Length <= size && text.Length % 2 == 0
                ? Marshal.PtrToStringUni(text.Buffer, text.Length / 2) ?? "" : "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    static List<int> ListeningPorts(int pid)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0); // IPv4, TCP_TABLE_OWNER_PID_LISTENER
        if (size < 4 || size > 16_000_000) return new();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0) != 0) return new();
            byte[] bytes = new byte[size]; Marshal.Copy(buffer, bytes, 0, size);
            int count = BitConverter.ToInt32(bytes, 0);
            var ports = new List<int>();
            for (int i = 0; i < count && 4L + (i + 1L) * 24 <= size; i++)
            {
                int row = 4 + i * 24;
                if (BitConverter.ToInt32(bytes, row + 20) != pid) continue;
                // Wildcard or loopback listeners only; all requests still target 127.0.0.1.
                uint address = BitConverter.ToUInt32(bytes, row + 4);
                if (address != 0 && bytes[row + 4] != 127) continue;
                int port = bytes[row + 8] * 256 + bytes[row + 9];
                if (port > 0 && !ports.Contains(port)) ports.Add(port);
            }
            return ports;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] struct UnicodeString { internal ushort Length, MaximumLength; internal IntPtr Buffer; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr buffer, int size, out int returned);
    [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
}

public sealed class AntigravityProvider : IDisposable
{
    const string Service = "/exa.language_server_pb.LanguageServerService/";
    readonly HttpClient http = new(new HttpClientHandler {
        UseProxy = false, AllowAutoRedirect = false,
        // The app's local HTTPS server uses a self-signed certificate. This exception applies
        // only to the fixed loopback origin and the read-only quota RPCs below.
        ServerCertificateCustomValidationCallback = (request, _, _, _) => IsLocalQuotaUri(request.RequestUri)
    }) { Timeout = TimeSpan.FromSeconds(4), MaxResponseContentBufferSize = 2_000_000 };
    readonly SemaphoreSlim gate = new(1, 1);
    DateTimeOffset nextRead;
    ProviderState? last;
    internal static bool IsLocalQuotaUri(Uri? uri) => uri is not null && uri.Host == "127.0.0.1"
        && uri.Port > 0 && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && (uri.AbsolutePath == Service + "RetrieveUserQuotaSummary" || uri.AbsolutePath == Service + "GetUserStatus" || uri.AbsolutePath == Service + "GetCommandModelConfigs");
    public async Task<ProviderState> Fetch(CancellationToken outer)
    {
        await gate.WaitAsync(outer);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outer);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            if (last is not null && DateTimeOffset.UtcNow < nextRead) return last;
            nextRead = DateTimeOffset.UtcNow.AddMinutes(2);
            var endpoints = await Task.Run(() => AntigravityEndpoint.Find(timeout.Token), timeout.Token);
            if (endpoints.Count == 0) return last = ProviderState.Empty("Antigravity") with { Status = "Uruchom Antigravity i zaloguj się na konto Google" };
            foreach (var endpoint in endpoints)
                foreach (int port in endpoint.Ports)
                    foreach (string scheme in new[] { "https", "http" })
                        foreach (string method in new[] { "RetrieveUserQuotaSummary", "GetUserStatus", "GetCommandModelConfigs" })
                        {
                            timeout.Token.ThrowIfCancellationRequested();
                            try
                            {
                                var uri = new Uri($"{scheme}://127.0.0.1:{port}{Service}{method}");
                                if (!IsLocalQuotaUri(uri)) continue;
                                using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                                request.Headers.Add("x-codeium-csrf-token", endpoint.Csrf);
                                request.Headers.Add("Connect-Protocol-Version", "1");
                                request.Content = new StringContent(method == "RetrieveUserQuotaSummary" ? "{}"
                                    : "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\"}}", Encoding.UTF8, "application/json");
                                using var response = await http.SendAsync(request, timeout.Token);
                                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                                { nextRead = DateTimeOffset.UtcNow.AddMinutes(10); return Failure("Antigravity ograniczył odczyty · ponowię połączenie"); }
                                if (!response.IsSuccessStatusCode) continue;
                                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                                var parsed = AntigravityUsageParser.Parse(document.RootElement);
                                if (method == "RetrieveUserQuotaSummary")
                                {
                                    // Read only the plan from the same server. Summary quotas are authoritative;
                                    // failure to read optional plan information must not discard the four limits.
                                    try
                                    {
                                        using var planRequest = new HttpRequestMessage(HttpMethod.Post, new Uri($"{scheme}://127.0.0.1:{port}{Service}GetUserStatus"));
                                        planRequest.Headers.Add("x-codeium-csrf-token", endpoint.Csrf);
                                        planRequest.Headers.Add("Connect-Protocol-Version", "1");
                                        planRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                                        using var planResponse = await http.SendAsync(planRequest, timeout.Token);
                                        if (planResponse.IsSuccessStatusCode)
                                        {
                                            using var planDocument = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync(timeout.Token));
                                            parsed = parsed with { Plan = AntigravityUsageParser.Plan(planDocument.RootElement) };
                                        }
                                    }
                                    catch (OperationCanceledException) when (outer.IsCancellationRequested) { throw; }
                                    catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { }
                                }
                                return last = parsed;
                            }
                            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { throw; }
                            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException) { }
                        }
            return Failure("Brak odczytu Antigravity · sprawdź logowanie w aplikacji");
        }
        catch (OperationCanceledException) when (outer.IsCancellationRequested) { throw; }
        catch { return Failure("Brak odczytu Antigravity · ponowię połączenie"); }
        finally { gate.Release(); }
    }
    ProviderState Failure(string status) => last = ProviderState.Empty("Antigravity") with { Status = status };
    public void Dispose() => http.Dispose();
}
