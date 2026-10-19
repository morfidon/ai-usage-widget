using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace AIUsageWidget;

public enum MascotState { Sleeping, Idle, Working, Waiting, Finished, Low, Exhausted, Offline }

public sealed class MascotView : FrameworkElement
{
    static readonly Dictionary<string, DrawingGroup> drawings = new();
    static readonly Stopwatch animationClock = Stopwatch.StartNew();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
    readonly string provider;
    readonly MascotState state;
    readonly bool animate;
    double phase;
    static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255) { var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b)); brush.Freeze(); return brush; }
    static Pen Pen(Brush brush, double width) { var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pen.Freeze(); return pen; }
    static Geometry Shape(string path) { var g = Geometry.Parse(path); g.Freeze(); return g; }
    static readonly Brush Ink = Brush(30, 42, 57), Shadow = Brush(0, 0, 0, 55), Blue = Brush(145, 201, 255), Peach = Brush(243, 184, 155);
    static readonly Brush Glow = Brush(216, 241, 255), Gold = Brush(255, 212, 131), Mint = Brush(135, 231, 203), Pink = Brush(250, 147, 157, 180);
    static readonly Brush Screen = Brush(36, 57, 76), Muted = Brush(152, 171, 189), Bubble = Brush(58, 48, 39);
    static readonly Pen DarkPen = Pen(Ink, 2), LightPen = Pen(Glow, 2), GoldPen = Pen(Gold, 1.8), MintPen = Pen(Mint, 2);
    static readonly Pen AntennaPen = Pen(Blue, 3), KeyboardOutline = Pen(Blue, 1), KeyboardSpace = Pen(Muted, 1.2), BlueArm = Pen(Blue, 6), PeachArm = Pen(Peach, 6);
    static readonly Geometry Smile = Shape("M36 37 Q40 42 44 37"), HappyLeft = Shape("M29 33 Q32 27 35 33"), HappyRight = Shape("M45 33 Q48 27 51 33");
    static readonly Geometry SleepLeft = Shape("M29 31 Q32 34 35 31"), SleepRight = Shape("M45 31 Q48 34 51 31");
    static readonly Geometry Check = Shape("M65 12 L69 16 L76 7"), Spark = Shape("M0 -4 L1 -1 L4 0 L1 1 L0 4 L-1 1 L-4 0 L-1 -1 Z");
    FormattedText? question, sleep;
    double textDpi;
    ScaleTransform? scale;
    bool Moving => animate && SystemParameters.ClientAreaAnimation && state is not (MascotState.Offline or MascotState.Exhausted);

    public MascotView(string provider, MascotState state, bool animate)
    {
        this.provider = provider; this.state = state; this.animate = animate;
        Width = 80; Height = 64; phase = animationClock.Elapsed.TotalSeconds;
        WidgetTips.Set(this, provider, Label(state));
        System.Windows.Automation.AutomationProperties.SetName(this, provider + " · " + Label(state));
        timer.Tick += (_, _) => { phase = animationClock.Elapsed.TotalSeconds; UpdateTimer(); InvalidateVisual(); };
        Loaded += (_, _) => UpdateTimer(); Unloaded += (_, _) => timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }
    void UpdateTimer()
    {
        if (Moving && IsLoaded && IsVisible) timer.Start(); else timer.Stop();
    }
    public static string Label(MascotState state) => state switch
    { MascotState.Sleeping => L.T("Odpoczywa"), MascotState.Idle => L.T("Gotowy"), MascotState.Working => L.T("Pracuje"), MascotState.Waiting => L.T("Czeka na Ciebie"),
        MascotState.Finished => L.T("Zadanie zakończone"), MascotState.Low => L.T("Mało limitu"), MascotState.Exhausted => L.T("Limit wyczerpany"), _ => L.T("Brak aktualnych danych") };
    public static MascotState Choose(ProviderState provider, SessionSnapshot sessions, bool finished = false)
    {
        var active = sessions.Sessions.Where(s => s.Provider == provider.Name).ToList();
        if (active.Any(s => s.State == "waiting")) return MascotState.Waiting;
        // Actual session activity takes precedence over a depleted or stale quota reading.
        if (active.Any(s => s.State == "working")) return MascotState.Working;
        bool fresh = provider.Connected && provider.Updated.HasValue && DateTimeOffset.Now - provider.Updated < TimeSpan.FromMinutes(10);
        var quotas = provider.Windows.Where(w => !w.Expired && (provider.Name != "Codex" || w.Id.StartsWith("codex:"))).ToList();
        if (fresh && quotas.Any(w => w.Remaining <= 0)) return MascotState.Exhausted;
        if (finished) return MascotState.Finished;
        if (!fresh || sessions.ErrorFor(provider.Name) is not null) return MascotState.Offline;
        if (quotas.Any(w => w.Remaining <= 10)) return MascotState.Low;
        return active.Count > 0 ? MascotState.Idle : MascotState.Sleeping;
    }
    static DrawingGroup Load(string provider)
    {
        if (drawings.TryGetValue(provider, out var drawing)) return drawing;
        // Editable, dependency-free SVG silhouettes; faces and limbs are animated in WPF.
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/assets/" + provider.ToLowerInvariant() + ".svg"))!.Stream;
        var xml = XDocument.Load(stream); drawing = new DrawingGroup();
        foreach (var p in xml.Descendants().Where(e => e.Name.LocalName == "path"))
            drawing.Children.Add(new GeometryDrawing((Brush)new BrushConverter().ConvertFromString(p.Attribute("fill")!.Value)!, null, Geometry.Parse(p.Attribute("d")!.Value)));
        drawing.Freeze(); drawings[provider] = drawing; return drawing;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // The static frame remains expressive when animations are off or Windows reduces motion.
        double t = Moving ? phase + (provider == "Claude" ? .45 : 0) : 0;
        bool robot = provider == "Codex";
        var skin = robot ? Blue : Peach;
        var eyePen = robot ? LightPen : DarkPen;
        var eyes = robot ? Glow : Ink;
        double bob = state switch { MascotState.Working => Math.Sin(t * 8) * 1.2, MascotState.Waiting => Math.Sin(t * 3) * 1.3,
            MascotState.Finished => -Math.Abs(Math.Sin(t * 4)) * 5, MascotState.Sleeping => Math.Sin(t * 1.8) * .8, _ => Math.Sin(t * 2) * .6 };
        if (scale is null || scale.ScaleX != ActualWidth / 80 || scale.ScaleY != ActualHeight / 64) {
            scale = new ScaleTransform(ActualWidth / 80, ActualHeight / 64); scale.Freeze();
        }
        dc.PushTransform(scale);
        dc.DrawEllipse(Shadow, null, new Point(40, 58), 24 + bob * .4, 3);
        if (state == MascotState.Offline) dc.PushOpacity(.6);
        dc.PushTransform(new TranslateTransform(0, bob + (state == MascotState.Exhausted ? 3 : 0)));
        double breath = state == MascotState.Sleeping ? Math.Sin(t * 1.8) * .025 : 0;
        dc.PushTransform(new ScaleTransform(1 + breath, 1 - breath, 40, 50));
        // Rounded feet and soft little arms keep each silhouette readable at widget size.
        dc.DrawEllipse(skin, null, new(30, 51), 6, 4); dc.DrawEllipse(skin, null, new(50, 51), 6, 4);
        if (state != MascotState.Working) {
            Arm(dc, skin, 20, 36, state == MascotState.Finished ? 11 : 12, state == MascotState.Finished ? 25 : 43);
            double wave = state == MascotState.Waiting ? Math.Sin(t * 7) * 5 : 0;
            Arm(dc, skin, 60, 35, state is MascotState.Waiting or MascotState.Finished ? 69 + wave : 68,
                state is MascotState.Waiting or MascotState.Finished ? 22 : 43);
        }
        if (robot) {
            dc.DrawLine(AntennaPen, new(40, 17), new(40 + Math.Sin(t * 3) * 2, 10));
            dc.DrawEllipse(state == MascotState.Working ? Mint : Blue, null, new(40 + Math.Sin(t * 3) * 2, 8), 4, 3.5);
        }
        dc.DrawDrawing(Load(provider));
        dc.DrawEllipse(Pink, null, new(26, 37), 3, 1.7); dc.DrawEllipse(Pink, null, new(54, 37), 3, 1.7);
        bool blink = Moving && t % 4.6 > 4.43;
        if (state is MascotState.Sleeping or MascotState.Exhausted || blink) {
            dc.DrawGeometry(null, eyePen, SleepLeft); dc.DrawGeometry(null, eyePen, SleepRight);
        } else if (state == MascotState.Finished) {
            dc.DrawGeometry(null, eyePen, HappyLeft); dc.DrawGeometry(null, eyePen, HappyRight);
        } else {
            double gaze = state == MascotState.Waiting ? 1.4 : state == MascotState.Working ? Math.Sin(t * 2.5) * .7 : 0;
            double height = state == MascotState.Low ? 2 : 3.8;
            foreach (double x in new[] { 32.0, 48.0 }) {
                dc.DrawEllipse(eyes, null, new(x + gaze, 31.5), 2.7, height);
                if (!robot && height > 2) dc.DrawEllipse(Glow, null, new(x + gaze - .7, 30.3), .9, 1.1);
            }
        }
        if (state == MascotState.Waiting) dc.DrawEllipse(null, eyePen, new(40, 38), 1.8, 2);
        else if (state is MascotState.Low or MascotState.Exhausted or MascotState.Offline) dc.DrawLine(eyePen, new(37, 39), new(42, 39));
        else dc.DrawGeometry(null, eyePen, Smile);
        dc.Pop(); dc.Pop();
        if (state == MascotState.Working) {
            // Hands alternate over a keyboard; a running dot is activity, never a progress percentage.
            dc.DrawRoundedRectangle(Screen, KeyboardOutline, new(19, 47, 42, 12), 4, 4);
            for (int i = 0; i < 6; i++) dc.DrawRoundedRectangle(i == (int)(t * 7) % 6 ? Mint : Muted, null, new(24 + i * 5.5, 51, 3, 2), .7, .7);
            dc.DrawLine(KeyboardSpace, new(32, 55), new(48, 55));
            for (int i = 0; i < 2; i++) {
                double tap = Math.Max(0, Math.Sin(t * 12 + i * Math.PI)) * 4;
                dc.DrawEllipse(skin, null, new(29 + i * 22, 45 + tap), 5.5, 3.5);
            }
            dc.DrawRoundedRectangle(Ink, null, new(63, 35, 13, 7), 3, 3);
            for (int i = 0; i < 3; i++) dc.DrawEllipse(i == (int)(t * 4) % 3 ? Mint : Muted, null, new(66 + i * 3.5, 38.5), 1, 1);
        }
        DrawBadge(dc, t);
        if (state == MascotState.Offline) dc.Pop();
        dc.Pop();
    }
    static void Arm(DrawingContext dc, Brush skin, double x, double y, double endX, double endY)
    { dc.DrawLine(ReferenceEquals(skin, Blue) ? BlueArm : PeachArm, new(x, y), new(endX, endY)); dc.DrawEllipse(skin, null, new(endX, endY), 3.6, 3.6); }
    void DrawBadge(DrawingContext dc, double t)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (question is null || textDpi != dpi) {
            question = new FormattedText("?", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Bold"), 15, Gold, dpi);
            sleep = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 12, Muted, dpi);
            textDpi = dpi;
        }
        if (state == MascotState.Waiting) {
            dc.DrawRoundedRectangle(Bubble, GoldPen, new(60, 1, 18, 20), 7, 7);
            dc.DrawLine(GoldPen, new(64, 21), new(61, 24)); dc.DrawText(question, new(65, 1));
        } else if (state == MascotState.Sleeping) {
            double drift = t % 2.8 / 2.8;
            dc.PushOpacity(Moving ? .9 - drift * .6 : .8);
            dc.DrawText(sleep, new(64 + drift * 4, 16 - drift * 11)); dc.Pop();
            dc.DrawText(sleep, new(57, 22));
        } else if (state == MascotState.Finished) {
            dc.DrawGeometry(null, MintPen, Check);
            foreach (var point in new[] { new Point(12, 15), new Point(66, 40) }) {
                dc.PushTransform(new TranslateTransform(point.X, point.Y + Math.Sin(t * 4) * 2));
                dc.DrawGeometry(Gold, null, Spark); dc.Pop();
            }
        } else if (state is MascotState.Low or MascotState.Exhausted) {
            dc.DrawRoundedRectangle(Ink, GoldPen, new(61, 5, 15, 10), 2, 2);
            dc.DrawLine(GoldPen, new(78, 8), new(78, 12));
            if (state == MascotState.Low) dc.DrawRectangle(Gold, null, new(64, 8, 3, 4));
        } else if (state == MascotState.Offline) {
            for (int i = 0; i < 3; i++) dc.DrawEllipse(Muted, null, new(64 + i * 4, 11), 1.3, 1.3);
        }
    }
}
