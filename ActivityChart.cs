using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace AIUsageWidget;

// Equal time buckets, a zero baseline and an explicit token scale.
public sealed class ActivityChart : FrameworkElement
{
    readonly long[] buckets;
    readonly DateTimeOffset start, end;
    readonly Brush accent;
    readonly Pen grid = new(InsightsUi.Line, .6);
    readonly double maximum;
    int selected = -1;
    const double Left = 60, Top = 12;
    double Right => Math.Max(Left + 24, ActualWidth - 8);
    double Bottom => Math.Max(Top + 1, ActualHeight - 28);

    internal static long[] Bucket(IEnumerable<TokenEntry> entries, DateTimeOffset start, DateTimeOffset end)
    {
        var result = new long[24];
        if (end <= start) return result;
        foreach (var entry in entries.Where(e => e.At >= start && e.At <= end))
        {
            int index = Math.Min(23, (int)((entry.At - start).TotalSeconds / (end - start).TotalSeconds * 24));
            result[index] += entry.Total;
        }
        return result;
    }

    public ActivityChart(IEnumerable<TokenEntry> entries, DateTimeOffset start, DateTimeOffset end, Color color)
    {
        this.start = start; this.end = end; buckets = Bucket(entries, start, end);
        maximum = Math.Ceiling(Math.Max(2, buckets.Max()) / 2.0) * 2;
        accent = new SolidColorBrush(color); accent.Freeze(); grid.Freeze();
        Height = 150; Focusable = true;
        AutomationProperties.SetName(this, L.T("Aktywność w czasie"));
        AutomationProperties.SetHelpText(this, L.T("Strzałki w lewo i prawo: odczyty tokenów."));
        MouseMove += (_, e) => {
            var p = e.GetPosition(this);
            Select(p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom
                ? Math.Min(23, (int)((p.X - Left) / (Right - Left) * 24)) : -1);
        };
        MouseLeave += (_, _) => { if (!IsKeyboardFocused) Select(-1); };
        GotKeyboardFocus += (_, _) => Select(selected < 0 ? 0 : selected);
        LostKeyboardFocus += (_, _) => Select(-1);
        Unloaded += (_, _) => { if (ToolTip is System.Windows.Controls.ToolTip tip) tip.IsOpen = false; };
        KeyDown += (_, e) => {
            if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
            Select(e.Key switch { Key.Home => 0, Key.End => 23, Key.Left => Math.Max(0, selected - 1), _ => Math.Min(23, selected + 1) });
            e.Handled = true;
        };
    }

    void Select(int index)
    {
        if (selected == index) return;
        selected = index;
        if (ToolTip is System.Windows.Controls.ToolTip previous) previous.IsOpen = false;
        if (index < 0) { ToolTip = null; AutomationProperties.SetName(this, L.T("Aktywność w czasie")); InvalidateVisual(); return; }
        string range = (start + (end - start) * (index / 24.0)).LocalDateTime.ToString(L.ShortTimestampPattern, L.Culture)
            + " – " + (start + (end - start) * ((index + 1) / 24.0)).LocalDateTime.ToString(L.ShortTimestampPattern, L.Culture);
        string amount = buckets[index].ToString("N0", L.Culture) + L.T(" tokenów");
        WidgetTips.Set(this, amount, range);
        AutomationProperties.SetName(this, L.T("Aktywność w czasie") + ": " + range + ", " + amount);
        if (IsKeyboardFocused && ToolTip is System.Windows.Controls.ToolTip tip) { tip.PlacementTarget = this; tip.IsOpen = true; }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        void Label(string value, double x, double y, double align = 0)
        {
            var text = new FormattedText(value, L.Culture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11,
                InsightsUi.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(x - text.Width * align, y));
        }
        foreach (double fraction in new[] { 0.0, .5, 1.0 })
        {
            double y = Bottom - (Bottom - Top) * fraction;
            dc.DrawLine(grid, new(Left, y), new(Right, y));
            Label(InsightsWindow.Tokens((long)Math.Ceiling(maximum * fraction)), Left - 8, y - 7, 1);
        }
        double width = (Right - Left) / 24;
        for (int i = 0; i < buckets.Length; i++)
        {
            double x = Left + i * width, height = (Bottom - Top) * buckets[i] / maximum;
            if (i == selected) dc.DrawRectangle(InsightsUi.Line, null, new Rect(x, Top, width, Bottom - Top));
            if (height > 0) dc.DrawRectangle(accent, null, new Rect(x + 2, Bottom - height, Math.Max(1, width - 4), height));
        }
        foreach (double fraction in new[] { 0.0, .5, 1.0 })
        {
            var at = start + (end - start) * fraction;
            string format = (end - start).TotalHours > 24 ? (L.IsPolish ? "dd.MM HH:mm" : "d MMM HH:mm") : "HH:mm";
            Label(at.LocalDateTime.ToString(format, L.Culture), Left + (Right - Left) * fraction, Bottom + 9, fraction);
        }
    }
}
