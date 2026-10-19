using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly string provider;
    readonly MascotState state;
    readonly bool animate;
    double phase;
    static SolidColorBrush FrozenBrush(Color color) { var b = new SolidColorBrush(color); b.Freeze(); return b; }
    static Pen FrozenPen(Brush brush, double width) { var p = new Pen(brush, width); p.Freeze(); return p; }
    static readonly Brush Dim = FrozenBrush(Color.FromRgb(18, 23, 31));
    static readonly Brush Shadow = FrozenBrush(Color.FromArgb(65, 0, 0, 0));
    static readonly Brush RobotEyes = FrozenBrush(Color.FromRgb(200, 230, 255));
    static readonly Brush Gold = FrozenBrush(Color.FromRgb(240, 197, 110));
    static readonly Brush Light = FrozenBrush(Color.FromRgb(165, 220, 255));
    static readonly Pen DarkEyePen = FrozenPen(Dim, 2), LightEyePen = FrozenPen(RobotEyes, 2), KeyboardPen = FrozenPen(Light, 1);
    static readonly Typeface MarkFace = new("Segoe UI Semibold");
    ScaleTransform? scale;
    FormattedText? markText;
    DrawingGroup? bodyDrawing, markDrawing;
    double markDpi;
    internal void ResetPhase()
    {
        phase = 0; InvalidateVisual();
        if (IsLoaded) { timer.Stop(); UpdateTimer(); }
    }
    public MascotView(string provider, MascotState state, bool animate)
    {
        this.provider = provider; this.state = state; this.animate = animate;
        Width = 66; Height = 50;
        WidgetTips.Set(this, provider, Label(state));
        System.Windows.Automation.AutomationProperties.SetName(this, provider + " · " + Label(state));
        timer.Tick += (_, _) => { phase += 0.35; InvalidateVisual(); };
        Loaded += (_, _) => UpdateTimer(); Unloaded += (_, _) => timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }
    void UpdateTimer()
    {
        if (animate && SystemParameters.ClientAreaAnimation && IsVisible && state is not (MascotState.Offline or MascotState.Exhausted)) timer.Start();
        else timer.Stop();
    }
    public static string Label(MascotState state) => state switch
    { MascotState.Sleeping => L.T("Odpoczywa"), MascotState.Idle => L.T("Gotowy"), MascotState.Working => L.T("Pracuje"), MascotState.Waiting => L.T("Czeka na Ciebie"),
        MascotState.Finished => L.T("Zadanie zakończone"), MascotState.Low => L.T("Mało limitu"), MascotState.Exhausted => L.T("Limit wyczerpany"), _ => L.T("Brak aktualnych danych") };
    public static MascotState Choose(ProviderState provider, SessionSnapshot sessions, bool finished = false)
    {
        var active = sessions.Sessions.Where(s => s.Provider == provider.Name).ToList();
        if (active.Any(s => s.State == "waiting")) return MascotState.Waiting;
        bool fresh = provider.Connected && provider.Updated.HasValue && DateTimeOffset.Now - provider.Updated < TimeSpan.FromMinutes(10);
        var quotas = provider.Windows.Where(w => !w.Expired && (provider.Name != "Codex" || w.Id.StartsWith("codex:"))).ToList();
        if (fresh && quotas.Any(w => w.Remaining <= 0)) return MascotState.Exhausted;
        if (active.Any(s => s.State == "working")) return MascotState.Working;
        if (finished) return MascotState.Finished;
        if (!fresh) return MascotState.Offline;
        if (quotas.Any(w => w.Remaining <= 10)) return MascotState.Low;
        return active.Count > 0 ? MascotState.Idle : MascotState.Sleeping;
    }
    static DrawingGroup Load(string provider)
    {
        if (drawings.TryGetValue(provider, out var drawing)) return drawing;
        // The bundled SVGs use the shared SVG/WPF path subset and remain editable source assets.
        var uri = new Uri("pack://application:,,,/assets/" + provider.ToLowerInvariant() + ".svg");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        var xml = XDocument.Load(stream);
        drawing = new DrawingGroup();
        foreach (var p in xml.Descendants().Where(e => e.Name.LocalName == "path"))
        {
            var geometry = Geometry.Parse(p.Attribute("d")!.Value);
            var brush = (Brush)new BrushConverter().ConvertFromString(p.Attribute("fill")!.Value)!;
            drawing.Children.Add(new GeometryDrawing(brush, null, geometry));
        }
        drawing.Freeze(); drawings[provider] = drawing; return drawing;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        bool moving = animate && SystemParameters.ClientAreaAnimation;
        double bob = moving ? Math.Sin(phase * (state == MascotState.Working ? 2 : 1)) * (state == MascotState.Working ? 2.5 : 0.7) : 0;
        if (scale is null || scale.ScaleX != Width / 80 || scale.ScaleY != Height / 64)
        { scale = new ScaleTransform(Width / 80, Height / 64); scale.Freeze(); }
        dc.PushTransform(scale);
        dc.DrawEllipse(Shadow, null, new Point(40, 57), 26, 3);
        dc.PushTransform(new TranslateTransform(0, bob));
        dc.DrawDrawing(BodyDrawing());
        dc.Pop();
        string mark = state switch { MascotState.Waiting => "?", MascotState.Sleeping => "z", MascotState.Finished => "✓", MascotState.Low => "!", MascotState.Exhausted => "×", MascotState.Offline => "…", _ => "" };
        if (mark.Length > 0)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (markDrawing is null || markDpi != dpi)
            {
                markText = new FormattedText(mark, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    MarkFace, state == MascotState.Waiting ? 19 : 13, Gold, dpi); markDpi = dpi;
                markDrawing = new DrawingGroup();
                using (var textContext = markDrawing.Open()) textContext.DrawText(markText, new Point(64, 0));
                markDrawing.Freeze();
            }
            dc.DrawDrawing(markDrawing);
        }
        if (state == MascotState.Working)
        {
            dc.DrawRoundedRectangle(Dim, KeyboardPen, new Rect(24, 44, 32, 9), 2, 2);
            for (int i = 0; i < 4; i++) dc.DrawEllipse(Light, null, new Point(29 + i * 7, 48 + (moving ? Math.Sin(phase * 3 + i) : 0)), 1.5, 1);
            for (int i = 0; i < 3; i++) dc.DrawEllipse(Light, null, new Point(68 + i * 3, 42 - ((phase * 6 + i * 5) % 20)), 1, 1);
        }
        dc.Pop();
    }
    DrawingGroup BodyDrawing()
    {
        if (bodyDrawing is not null) return bodyDrawing;
        bodyDrawing = new DrawingGroup();
        using (var dc = bodyDrawing.Open())
        {
            if (state == MascotState.Offline) dc.PushOpacity(0.4);
            dc.DrawDrawing(Load(provider));
            var eyes = provider == "Claude" ? Dim : RobotEyes;
            var eyePen = provider == "Claude" ? DarkEyePen : LightEyePen;
            double eyeY = provider == "Claude" ? 29 : 31;
            if (state == MascotState.Sleeping)
            {
                dc.DrawLine(eyePen, new(29, eyeY), new(34, eyeY)); dc.DrawLine(eyePen, new(46, eyeY), new(51, eyeY));
            }
            else if (state == MascotState.Exhausted)
            {
                foreach (int x in new[] { 31, 49 })
                { dc.DrawLine(eyePen, new(x - 3, eyeY - 3), new(x + 3, eyeY + 3)); dc.DrawLine(eyePen, new(x + 3, eyeY - 3), new(x - 3, eyeY + 3)); }
            }
            else
            {
                dc.DrawRoundedRectangle(eyes, null, new Rect(29, eyeY - 3, 4, state == MascotState.Waiting ? 8 : 6), 1, 1);
                dc.DrawRoundedRectangle(eyes, null, new Rect(47, eyeY - 3, 4, state == MascotState.Waiting ? 8 : 6), 1, 1);
            }
            if (state == MascotState.Offline) dc.Pop();
        }
        bodyDrawing.Freeze(); return bodyDrawing;
    }
}
