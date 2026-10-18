using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AIUsageWidget;

public static class WidgetTips
{
    public const int Delay = 120;
    static string Clip(string text, int limit)
    {
        if (text.Length <= limit) return text;
        int end = limit - 1;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end].TrimEnd() + "…";
    }
    public static string Line(string text, int limit = 90)
    {
        var first = (text ?? "").AsSpan();
        int newline = first.IndexOfAny('\r', '\n');
        if (newline >= 0) first = first[..newline];
        string line = Regex.Replace(first[..Math.Min(first.Length, 1024)].ToString(), @"\s+", " ").Trim();
        return Clip(line, limit);
    }
    public static string Body(string text)
    {
        text ??= "";
        using var reader = new StringReader(text[..Math.Min(text.Length, 4096)]);
        var lines = new List<string>();
        // Bound work as well as output for provider errors and accidentally pasted prompts.
        for (int i = 0; i < 8 && reader.ReadLine() is string line; i++)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (lines.Count == 6) { lines[^1] += "…"; break; }
            lines.Add(Line(line));
        }
        return Clip(string.Join("\n", lines), 420);
    }
    public static ToolTip Create(string title, string detail = "")
    {
        var content = new StackPanel { MaxWidth = 292 };
        content.Children.Add(new TextBlock { Text = Line(title, 80), Foreground = InsightsUi.Ink, FontSize = 13,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 38 });
        string body = Body(detail);
        if (body.Length > 0) content.Children.Add(new TextBlock { Text = body, FontSize = 12, Foreground = InsightsUi.Muted,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 18,
            MaxHeight = 162, Margin = new Thickness(0, 7, 0, 0) });
        return new ToolTip { Content = content, MaxWidth = 320, MaxHeight = 238,
            Placement = PlacementMode.Right, HorizontalOffset = 10, VerticalOffset = 0,
            UseLayoutRounding = true, HasDropShadow = false };
    }
    public static void Set(FrameworkElement target, string title, string detail = "")
    {
        target.ToolTip = Create(title, detail);
        AutomationProperties.SetHelpText(target, Line(title, 80) + (detail.Length == 0 ? "" : "\n" + Body(detail)));
        ToolTipService.SetInitialShowDelay(target, Delay);
        ToolTipService.SetBetweenShowDelay(target, 1000);
        ToolTipService.SetShowDuration(target, 12000);
        ToolTipService.SetShowOnDisabled(target, true);
    }
    public static void SetOverflow(TextBlock target, string title, string detail)
    {
        Set(target, title, detail);
        target.ToolTipOpening += (_, e) =>
        {
            var text = new FormattedText(target.Text, CultureInfo.CurrentUICulture, target.FlowDirection,
                new Typeface(target.FontFamily, target.FontStyle, target.FontWeight, target.FontStretch),
                target.FontSize, target.Foreground, VisualTreeHelper.GetDpi(target).PixelsPerDip);
            if (text.WidthIncludingTrailingWhitespace <= target.ActualWidth) e.Handled = true;
        };
    }
}
