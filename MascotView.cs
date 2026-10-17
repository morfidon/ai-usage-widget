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
    public MascotView(string provider, MascotState state, bool animate)
    {
        this.provider = provider; this.state = state; this.animate = animate;
        Width = 66; Height = 50;
        ToolTip = provider + " · " + Label(state);
        System.Windows.Automation.AutomationProperties.SetName(this, (string)ToolTip);
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
    { MascotState.Sleeping => "Odpoczywa", MascotState.Idle => "Gotowy", MascotState.Working => "Pracuje", MascotState.Waiting => "Czeka na Ciebie",
        MascotState.Finished => "Zadanie zakończone", MascotState.Low => "Mało limitu", MascotState.Exhausted => "Limit wyczerpany", _ => "Brak aktualnych danych" };
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
        dc.PushTransform(new ScaleTransform(Width / 80, Height / 64));
        var dim = new SolidColorBrush(Color.FromRgb(18, 23, 31));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)), null, new Point(40, 57), 26, 3);
        dc.PushTransform(new TranslateTransform(0, bob));
        if (state == MascotState.Offline) dc.PushOpacity(0.4);
        dc.DrawDrawing(Load(provider));
        var eyes = provider == "Claude" ? dim : new SolidColorBrush(Color.FromRgb(200, 230, 255));
        double eyeY = provider == "Claude" ? 29 : 31;
        if (state == MascotState.Sleeping)
        {
            dc.DrawLine(new Pen(eyes, 2), new(29, eyeY), new(34, eyeY)); dc.DrawLine(new Pen(eyes, 2), new(46, eyeY), new(51, eyeY));
        }
        else if (state == MascotState.Exhausted)
        {
            foreach (int x in new[] { 31, 49 })
            { dc.DrawLine(new Pen(eyes, 2), new(x - 3, eyeY - 3), new(x + 3, eyeY + 3)); dc.DrawLine(new Pen(eyes, 2), new(x + 3, eyeY - 3), new(x - 3, eyeY + 3)); }
        }
        else
        {
            dc.DrawRoundedRectangle(eyes, null, new Rect(29, eyeY - 3, 4, state == MascotState.Waiting ? 8 : 6), 1, 1);
            dc.DrawRoundedRectangle(eyes, null, new Rect(47, eyeY - 3, 4, state == MascotState.Waiting ? 8 : 6), 1, 1);
        }
        if (state == MascotState.Offline) dc.Pop();
        dc.Pop();
        var gold = new SolidColorBrush(Color.FromRgb(240, 197, 110));
        string mark = state switch { MascotState.Waiting => "?", MascotState.Sleeping => "z", MascotState.Finished => "✓", MascotState.Low => "!", MascotState.Exhausted => "×", MascotState.Offline => "…", _ => "" };
        if (mark.Length > 0) dc.DrawText(new FormattedText(mark, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), state == MascotState.Waiting ? 19 : 13, gold, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(64, 0));
        if (state == MascotState.Working)
        {
            var light = new SolidColorBrush(Color.FromRgb(165, 220, 255));
            dc.DrawRoundedRectangle(dim, new Pen(light, 1), new Rect(24, 44, 32, 9), 2, 2);
            for (int i = 0; i < 4; i++) dc.DrawEllipse(light, null, new Point(29 + i * 7, 48 + (moving ? Math.Sin(phase * 3 + i) : 0)), 1.5, 1);
            for (int i = 0; i < 3; i++) dc.DrawEllipse(light, null, new Point(68 + i * 3, 42 - ((phase * 6 + i * 5) % 20)), 1, 1);
        }
        dc.Pop();
    }
}
