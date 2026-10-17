using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Microsoft.Win32;

namespace AIUsageWidget;

public static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "AIUsageWidget";
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name) is string; }
    }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) key.SetValue(Name, $"\"{Path.Combine(AppContext.BaseDirectory, "AIUsageWidget.exe")}\" --startup");
        else key.DeleteValue(Name, false);
    }
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Directory.CreateDirectory(Settings.DataDirectory);
        if (args.Contains("--self-test")) return SelfTests.Run();
        if (args.Contains("--diagnose")) return Diagnose().GetAwaiter().GetResult();
        if (args.Contains("--diagnose-antigravity")) return DiagnoseAntigravity().GetAwaiter().GetResult();
        if (args.Contains("--diagnose-activity")) return DiagnoseActivity(args.Contains("--full-history")).GetAwaiter().GetResult();
        if (args.Contains("--visual-test")) return VisualVerification.Run();
        using var mutex = new Mutex(true, @"Local\AIUsageWidget.Video.v1", out bool first);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AIUsageWidget.Show.v1");
        if (!first) { showEvent.Set(); return 0; }
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            File.AppendAllText(Path.Combine(Settings.DataDirectory, "errors.log"), $"{DateTimeOffset.Now:O} {e.Exception.GetType().Name}\n");
            System.Windows.MessageBox.Show("Widget napotkał błąd. Uruchom go ponownie. Szczegóły zapisano w data/errors.log.", "AI Usage Widget");
        };
        var window = new MainWindow(Settings.Load());
        var registration = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => app.Dispatcher.BeginInvoke(() => window.Restore()), null, Timeout.Infinite, false);
        if (args.Contains("--capture"))
        {
            window.Loaded += async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(12));
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth * 2, (int)window.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(Settings.DataDirectory, "preview.png")); encoder.Save(stream);
                File.WriteAllText(Path.Combine(Settings.DataDirectory, "window-check.json"), JsonSerializer.Serialize(new {
                    Visible = IsWindowVisible(new WindowInteropHelper(window).Handle), window.Left, window.Top,
                    window.ActualWidth, window.ActualHeight, window.Topmost
                }));
            };
        }
        app.Run(window);
        registration.Unregister(null);
        return 0;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr handle);
    static async Task<int> DiagnoseAntigravity()
    {
        using var provider = new AntigravityProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var state = await provider.Fetch(timeout.Token);
        string result = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(Settings.DataDirectory, "antigravity-diagnostic.json"), result);
        Console.WriteLine(result);
        return state.Connected && state.Windows.Count > 0 ? 0 : 2;
    }
    static async Task<int> DiagnoseActivity(bool fullHistory)
    {
        var settings = Settings.Load(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(fullHistory ? 240 : 45));
        var sessions = await new SessionMonitor(settings).Read(timeout.Token);
        using var analytics = new TranscriptAnalytics(settings);
        var stats = await Task.Run(() => analytics.Scan(timeout.Token), timeout.Token);
        while (fullHistory && stats.Loading) stats = await Task.Run(() => analytics.Scan(timeout.Token), timeout.Token);
        var result = new {
            Sessions = new[] { "Codex", "Claude" }.Select(name => new { Provider = name, Count = sessions.Sessions.Count(s => s.Provider == name),
                Working = sessions.Sessions.Count(s => s.Provider == name && s.State == "working"), Waiting = sessions.Sessions.Count(s => s.Provider == name && s.State == "waiting"), Error = sessions.ErrorFor(name) }),
            Analytics = new[] { "Codex", "Claude" }.Select(name => new { Provider = name, Requests = stats.Entries.Count(s => s.Provider == name), Tokens = stats.Entries.Where(s => s.Provider == name).Sum(s => s.Total) }),
            stats.Loading, stats.Skipped, CollectionMode = "Quota and local metadata reads; no model inference"
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(Settings.DataDirectory, "activity-diagnostic.json"), json); Console.WriteLine(json);
        return sessions.CodexError is null && sessions.ClaudeError is null ? 0 : 2;
    }
    static async Task<int> Diagnose()
    {
        var settings = Settings.Load();
        using var codex = new CodexProvider(settings);
        using var claude = new ClaudeProvider(settings);
        using var devin = new DevinProvider();
        using var air = new AirProvider();
        using var antigravity = new AntigravityProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var a = codex.Fetch(timeout.Token); var b = claude.Fetch(timeout.Token);
        var c = devin.Fetch(timeout.Token);
        var d = air.Fetch(timeout.Token);
        var e = antigravity.Fetch(timeout.Token);
        await Task.WhenAll((Task)a, (Task)b, (Task)c, (Task)d, (Task)e);
        var states = new[] { await a, await b, await c, await d, await e };
        string result = JsonSerializer.Serialize(states, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(Settings.DataDirectory, "diagnostic.json"), result);
        Console.WriteLine(result);
        return states.All(x => x?.Windows.Count > 0) ? 0 : 2;
    }
}
