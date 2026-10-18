using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsageWidget;

// Offline paired measurements: the same fixture, render workload and result hashes in both builds.
public static class PerformanceBenchmarks
{
    static object Measure(Action action, int repetitions)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetTotalAllocatedBytes(true); var watch = Stopwatch.StartNew();
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        for (int i = 0; i < repetitions; i++) action();
        return new { Repetitions = repetitions, Milliseconds = watch.Elapsed.TotalMilliseconds,
            CpuMilliseconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
            AllocatedBytes = GC.GetTotalAllocatedBytes(true) - before };
    }
    public static int Run(string[] args)
    {
        string root = args[Array.IndexOf(args, "--benchmark") + 1];
        if (args.Contains("--animate-window")) return AnimateWindow();
        Directory.CreateDirectory(root);
        string cp = Path.Combine(root, "claude"), xp = Path.Combine(root, "codex"), ap = Path.Combine(root, "archive");
        if (!Directory.Exists(cp)) Generate(cp, xp, ap);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = new Dictionary<string, object>();
        using (var analyzer = new TranscriptAnalytics(new Settings(), cp, xp, ap))
        {
            AnalyticsSnapshot snapshot = AnalyticsSnapshot.Empty;
            result["InitialIndex"] = Measure(() => { do { snapshot = analyzer.Scan(CancellationToken.None); } while (snapshot.Loading); }, 1);
            result["UnchangedScans"] = Measure(() => snapshot = analyzer.Scan(CancellationToken.None), 15);
            result["AnalyticsHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                Entries = snapshot.Entries.OrderBy(e => e.Provider).ThenBy(e => e.Session).ThenBy(e => e.At).ThenBy(e => e.Output),
                Tools = snapshot.Tools.OrderBy(e => e.Name).ThenBy(e => e.At), snapshot.Loading, snapshot.Skipped }))));
            result["Requests"] = snapshot.Entries.Count;
            GC.Collect(); result["IndexedManagedBytes"] = GC.GetTotalMemory(true);
        }
        var main = new MainWindow(new Settings { Compact = true, Topmost = false }, true);
        main.Measure(new Size(356, 1440)); main.Arrange(new Rect(0, 0, 356, main.DesiredSize.Height)); main.UpdateLayout();
        var render = (Action)typeof(MainWindow).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.CreateDelegate(typeof(Action), main);
        result["UnchangedCardRefreshes"] = Measure(() => { render(); main.UpdateLayout(); }, 100);
        main.Close();
        var frameHashes = new List<string>(); var views = new List<MascotView>();
        foreach (string provider in new[] { "Claude", "Codex" })
            foreach (MascotState state in Enum.GetValues<MascotState>())
            {
                var view = new MascotView(provider, state, true);
                view.Measure(new Size(66, 50)); view.Arrange(new Rect(0, 0, 66, 50));
                views.Add(view);
                foreach (double phase in new[] { 0, 0.35, 1.75, 30.1 })
                {
                    typeof(MascotView).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, phase);
                    frameHashes.Add(FrameHash(view));
                }
            }
        var drawCalls = views.Select(v => (Action<DrawingContext>)typeof(MascotView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(Action<DrawingContext>), v)).ToArray();
        result["MascotFrames"] = Measure(() => { foreach (var draw in drawCalls) { var visual = new DrawingVisual(); using var dc = visual.RenderOpen(); draw(dc); } }, 300);
        result["MascotPixelHashes"] = frameHashes;
        result["FixtureBytes"] = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        string output = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(output); app.Shutdown(); return 0;
    }
    static int AnimateWindow()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(new Settings { Compact = true, Topmost = false, Left = 20, Top = 20 }, true);
        window.Loaded += async (_, _) =>
        {
            await System.Threading.Tasks.Task.Delay(2000);
            var p = Process.GetCurrentProcess(); var cpu = p.TotalProcessorTime.TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(true); var watch = Stopwatch.StartNew();
            await System.Threading.Tasks.Task.Delay(15000);
            Console.WriteLine(JsonSerializer.Serialize(new { ElapsedSeconds = watch.Elapsed.TotalSeconds,
                CpuMilliseconds = p.TotalProcessorTime.TotalMilliseconds - cpu, AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                WorkingSetBytes = p.WorkingSet64 }));
            window.Close(); app.Shutdown();
        };
        window.Show(); app.Run(); return 0;
    }
    static string FrameHash(MascotView view)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            typeof(MascotView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, new object[] { dc });
        var bitmap = new RenderTargetBitmap(66, 50, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        byte[] pixels = new byte[66 * 50 * 4]; bitmap.CopyPixels(pixels, 66 * 4, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
    static void Generate(string cp, string xp, string ap)
    {
        Directory.CreateDirectory(cp); Directory.CreateDirectory(xp); Directory.CreateDirectory(ap);
        var at = DateTimeOffset.UtcNow.AddHours(-12);
        string filler = new('x', 6000);
        for (int f = 0; f < 80; f++)
        {
            using var stream = new StreamWriter(Path.Combine(cp, f + ".jsonl"), false, new UTF8Encoding(false));
            for (int i = 0; i < 400; i++)
            {
                stream.WriteLine(JsonSerializer.Serialize(new { type = "user", message = filler }));
                stream.WriteLine(JsonSerializer.Serialize(new { type = "assistant", timestamp = at.AddSeconds(i).ToString("O"), cwd = "C:/fixture/project-" + f % 12, sessionId = "s-" + f,
                    message = new { id = "m-" + f + "-" + i, model = "claude-test", usage = new { input_tokens = i + 10, cache_read_input_tokens = 12000, cache_creation_input_tokens = 40, output_tokens = 100 },
                        content = new[] { new { type = "tool_use", id = "t-" + f + "-" + i, name = "mcp__example__read" } } } }));
            }
        }
    }
}
