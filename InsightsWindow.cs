using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AIUsageWidget;

public static class InsightsUi
{
    public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(236, 241, 248));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(164, 174, 191));
    public static readonly Color Blue = Color.FromRgb(124, 184, 255), Purple = Color.FromRgb(194, 171, 255);
    public static TextBlock Text(string text, double size = 12, Brush? brush = null) => new()
    { Text = text, FontSize = size, Foreground = brush ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) };
    public static ComboBox Choice(IEnumerable<string> values)
    {
        var c = new ComboBox { MinWidth = 145, Margin = new Thickness(0, 0, 12, 10), Padding = new Thickness(8, 5, 8, 5) };
        foreach (var v in values) c.Items.Add(v);
        c.SelectedIndex = 0; return c;
    }
}

public sealed class InsightsWindow : Window
{
    readonly UsageHistory history;
    readonly TabControl tabs = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    readonly StackPanel historyBody = new() { Margin = new Thickness(6, 18, 6, 6) };
    readonly ComboBox provider = InsightsUi.Choice(new[] { "Codex", "Claude" });
    readonly ComboBox period = InsightsUi.Choice(new[] { "Ostatnie 24 godziny", "Ostatnie 7 dni" });
    readonly StackPanel projectBody = new(), diagnosticBody = new(), sessionBody = new();
    readonly ComboBox projectProvider = InsightsUi.Choice(new[] { "Codex", "Claude" });
    readonly ComboBox projectPeriod = InsightsUi.Choice(new[] { "Ostatnie 7 dni", "Ostatnie 24 godziny" });
    AnalyticsSnapshot analysis = AnalyticsSnapshot.Empty;
    SessionSnapshot sessions = SessionSnapshot.Empty;
    public InsightsWindow(UsageHistory history)
    {
        this.history = history;
        Title = "AI Usage · historia i szczegóły"; Width = 860; Height = 660; MinWidth = 620; MinHeight = 440;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        Background = new SolidColorBrush(Color.FromRgb(24, 28, 35)); Foreground = InsightsUi.Ink;
        FontFamily = new FontFamily("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WidgetTheme.Apply(this);
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var title = InsightsUi.Text("Twoje limity w czasie", 23); title.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var note = InsightsUi.Text("Odczyty konta • lokalna historia • bez wywoływania modeli", 11, InsightsUi.Muted);
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        root.Children.Add(tabs);
        var page = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var filters = new StackPanel { Orientation = Orientation.Horizontal }; filters.Children.Add(provider); filters.Children.Add(period);
        DockPanel.SetDock(filters, Dock.Top); page.Children.Add(filters);
        page.Children.Add(new ScrollViewer { Content = historyBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tabs.Items.Add(new TabItem { Header = "Historia limitów", Content = page, Padding = new Thickness(14, 8, 14, 8) });
        var projects = new DockPanel { Margin = new Thickness(6, 16, 6, 6) };
        var projectFilters = new StackPanel { Orientation = Orientation.Horizontal };
        projectFilters.Children.Add(projectProvider); projectFilters.Children.Add(projectPeriod);
        DockPanel.SetDock(projectFilters, Dock.Top); projects.Children.Add(projectFilters);
        projects.Children.Add(new ScrollViewer { Content = projectBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tabs.Items.Add(new TabItem { Header = "Projekty", Content = projects, Padding = new Thickness(14, 8, 14, 8) });
        tabs.Items.Add(new TabItem { Header = "Diagnostyka Claude", Content = new ScrollViewer { Content = diagnosticBody, Margin = new Thickness(6, 16, 6, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(14, 8, 14, 8) });
        tabs.Items.Add(new TabItem { Header = "Sesje", Content = new ScrollViewer { Content = sessionBody, Margin = new Thickness(6, 16, 6, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(14, 8, 14, 8) });
        projectProvider.SelectionChanged += (_, _) => RefreshProjects(); projectPeriod.SelectionChanged += (_, _) => RefreshProjects();
        provider.SelectionChanged += (_, _) => RefreshHistory(); period.SelectionChanged += (_, _) => RefreshHistory();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        RefreshHistory();
    }
    public void SelectSessions() => tabs.SelectedIndex = 3;
    internal void SelectTab(int tab) => tabs.SelectedIndex = tab;
    public void UpdateDetails(AnalyticsSnapshot analytics, SessionSnapshot snapshot)
    {
        if (!ReferenceEquals(analysis, analytics)) { analysis = analytics; RefreshProjects(); RefreshDiagnostics(); }
        if (!ReferenceEquals(sessions, snapshot)) { sessions = snapshot; RefreshSessions(); }
    }
    public static string Tokens(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.##} mln" : n >= 1000 ? $"{n / 1000.0:0.#} tys." : n.ToString("N0");
    public static string Count(int n, string one, string few, string many) => n.ToString("N0") + " "
        + (n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14) ? few : many);
    void RefreshProjects()
    {
        projectBody.Children.Clear();
        string name = (string)projectProvider.SelectedItem;
        var since = DateTimeOffset.Now.AddDays(projectPeriod.SelectedIndex == 0 ? -7 : -1);
        var entries = analysis.Entries.Where(e => e.Provider == name && e.At >= since).ToList();
        projectBody.Children.Add(InsightsUi.Text("Gdzie trafiają tokeny", 19));
        projectBody.Children.Add(InsightsUi.Text("Lokalne rozmowy na tym komputerze. Udział w tokenach nie jest udziałem w limicie abonamentu ani rachunkiem — modele mają różne ceny, a cache obniża koszt.", 11, InsightsUi.Muted));
        projectBody.Children.Add(InsightsUi.Text(analysis.Loading ? "Indeksowanie historii… wyniki są na razie częściowe." : $"Odczyt: {analysis.At.LocalDateTime:HH:mm:ss}", 11, InsightsUi.Muted));
        long total = entries.Sum(e => e.Total);
        foreach (var g in entries.GroupBy(e => e.Project).OrderByDescending(g => g.Sum(e => e.Total)).Take(20))
        {
            var box = new StackPanel { Margin = new Thickness(0, 12, 0, 6) };
            string project = System.IO.Path.GetFileName(g.Key.TrimEnd('\\', '/'));
            var title = InsightsUi.Text($"{project}   ·   {(total > 0 ? g.Sum(e => e.Total) * 100.0 / total : 0):0.#}%", 14);
            title.ToolTip = g.Key; box.Children.Add(title);
            box.Children.Add(new ProgressBar { Value = total > 0 ? g.Sum(e => e.Total) * 100.0 / total : 0, Height = 5,
                Foreground = new SolidColorBrush(name == "Codex" ? InsightsUi.Blue : InsightsUi.Purple), Background = new SolidColorBrush(Color.FromRgb(53, 62, 76)), BorderThickness = new Thickness(0) });
            box.Children.Add(InsightsUi.Text($"{Tokens(g.Sum(e => e.Total))} tokenów   ·   {Count(g.Select(e => e.Session).Distinct().Count(), "sesja", "sesje", "sesji")}   ·   {Count(g.Count(), "odpowiedź", "odpowiedzi", "odpowiedzi")}", 11, InsightsUi.Muted));
            box.Children.Add(InsightsUi.Text($"Wejście {Tokens(g.Sum(e => e.Input))}   •   z cache {Tokens(g.Sum(e => e.Cached))}   •   zapis cache {Tokens(g.Sum(e => e.CacheWrite))}   •   wyjście {Tokens(g.Sum(e => e.Output))}", 11, InsightsUi.Muted));
            box.Children.Add(InsightsUi.Text(string.Join("  ·  ", g.GroupBy(e => e.Model).OrderByDescending(m => m.Sum(e => e.Total)).Select(m => m.Key + ": " + Tokens(m.Sum(e => e.Total)))), 10, InsightsUi.Muted));
            projectBody.Children.Add(box);
        }
        if (total == 0) projectBody.Children.Add(InsightsUi.Text("Brak zapisanych danych tokenów w wybranym okresie.", 12, InsightsUi.Muted));
        if (analysis.Skipped > 0) projectBody.Children.Add(InsightsUi.Text($"Pominięte pliki lub nieczytelne rekordy: {analysis.Skipped}. Pozostałe dane są dostępne.", 11, InsightsUi.Muted));
    }
    void RefreshDiagnostics()
    {
        diagnosticBody.Children.Clear();
        var entries = analysis.Entries.Where(e => e.Provider == "Claude").ToList();
        diagnosticBody.Children.Add(InsightsUi.Text("Claude · ostatnie 7 dni", 19));
        diagnosticBody.Children.Add(InsightsUi.Text("Statystyki z lokalnych zapisów odpowiedzi. Bez promptów i bez uruchamiania modelu. Liczby wywołań MCP, umiejętności i subagentów opisują aktywność, a nie przypisany im procent abonamentu.", 11, InsightsUi.Muted));
        if (analysis.Loading) diagnosticBody.Children.Add(InsightsUi.Text("Indeksowanie… statystyki są częściowe.", 11, InsightsUi.Muted));
        long input = entries.Sum(e => e.Input + e.Cached + e.CacheWrite), cached = entries.Sum(e => e.Cached), total = entries.Sum(e => e.Total);
        diagnosticBody.Children.Add(InsightsUi.Text($"{Count(entries.Count, "odpowiedź", "odpowiedzi", "odpowiedzi")} · {Count(entries.Select(e => e.Session).Distinct().Count(), "sesja", "sesje", "sesji")} · {Tokens(total)} tokenów", 14));
        diagnosticBody.Children.Add(InsightsUi.Text($"Cache: {(input > 0 ? 100.0 * cached / input : 0):0.#}% tokenów wejściowych odczytano z cache.\nDuży kontekst (>200 tys. tokenów): {Count(entries.Count(e => e.Input + e.Cached + e.CacheWrite > 200_000), "odpowiedź", "odpowiedzi", "odpowiedzi")}.\nSubagenci: {Tokens(entries.Where(e => e.Subagent).Sum(e => e.Total))} tokenów.", 13));
        diagnosticBody.Children.Add(InsightsUi.Text("Stały kontekst: sam zapis tokenów nie pozwala rzetelnie oddzielić instrukcji i definicji narzędzi. Pełne przypisanie sprawdzisz w /usage w Claude Code.", 11, InsightsUi.Muted));
        foreach (var category in new[] { "MCP", "Umiejętności", "Subagenci" })
        {
            diagnosticBody.Children.Add(InsightsUi.Text(category, 15));
            var groups = analysis.Tools.Where(t => t.Provider == "Claude" && t.Kind == category).GroupBy(t => t.Name).OrderByDescending(g => g.Count()).Take(10).ToList();
            foreach (var g in groups) diagnosticBody.Children.Add(InsightsUi.Text($"{g.Key}   ·   {Count(g.Count(), "wywołanie", "wywołania", "wywołań")}", 12, InsightsUi.Muted));
            if (groups.Count == 0) diagnosticBody.Children.Add(InsightsUi.Text("Brak zarejestrowanych wywołań.", 11, InsightsUi.Muted));
        }
    }
    void RefreshSessions()
    {
        sessionBody.Children.Clear();
        sessionBody.Children.Add(InsightsUi.Text("Aktywne sesje na tym komputerze", 19));
        foreach (var name in new[] { "Codex", "Claude" })
        {
            sessionBody.Children.Add(InsightsUi.Text(name, 16, new SolidColorBrush(name == "Codex" ? InsightsUi.Blue : InsightsUi.Purple)));
            var rows = sessions.Sessions.Where(s => s.Provider == name).OrderByDescending(s => s.State == "waiting").ToList();
            foreach (var s in rows)
            {
                sessionBody.Children.Add(InsightsUi.Text((s.State == "working" ? "▶  " : s.State == "waiting" ? "?  " : "○  ") + s.Name, 13));
                sessionBody.Children.Add(InsightsUi.Text(s.StateLabel + (s.Reason.Length > 0 ? " · " + s.Reason : "") + "\n" + s.Directory, 11, InsightsUi.Muted));
            }
            if (rows.Count == 0) sessionBody.Children.Add(InsightsUi.Text(sessions.ErrorFor(name) ?? "Brak aktywnych sesji.", 12, InsightsUi.Muted));
        }
        sessionBody.Children.Add(InsightsUi.Text(sessions.At == DateTimeOffset.MinValue ? "Oczekiwanie na pierwszy odczyt…" : $"Odczyt: {sessions.At.LocalDateTime:HH:mm:ss}", 10, InsightsUi.Muted));
    }
    public void RefreshHistory()
    {
        historyBody.Children.Clear();
        var now = DateTimeOffset.Now;
        var start = now.AddDays(period.SelectedIndex == 0 ? -1 : -7);
        var rows = history.Samples.Where(s => s.Provider == (string)provider.SelectedItem && s.At >= start).ToList();
        historyBody.Children.Add(InsightsUi.Text("Wykorzystanie limitu", 15));
        if (history.Error is not null) historyBody.Children.Add(InsightsUi.Text(history.Error, 11, Brushes.Salmon));
        historyBody.Children.Add(new HistoryChart(rows, start, now) { Height = 285, Margin = new Thickness(0, 6, 0, 10) });
        foreach (var group in rows.GroupBy(s => s.Id))
        {
            var last = group.Last();
            historyBody.Children.Add(InsightsUi.Text($"●  {last.Label}   ·   ostatnio {last.Used:0.#}% wykorzystano   ·   {last.At.LocalDateTime:dd.MM HH:mm}", 12,
                new SolidColorBrush(HistoryChart.SeriesColor(rows.Select(s => s.Id).Distinct().ToList().IndexOf(group.Key)))));
        }
        historyBody.Children.Add(InsightsUi.Text(rows.Count == 0
            ? "Historia zacznie się od pierwszego aktualnego odczytu. Nie odtwarzamy dawnych procentów z liczby tokenów."
            : "Najedź na punkt, aby zobaczyć odczyt. Przerwy oznaczają brak danych; linie nie łączą odczytów przez reset limitu. Oś czasu: czas lokalny.", 11, InsightsUi.Muted));
    }
}

public sealed class HistoryChart : FrameworkElement
{
    readonly List<UsageSample> rows;
    readonly DateTimeOffset start, end;
    readonly List<string> ids;
    public static Color SeriesColor(int i) => (i % 5) switch
    { 0 => InsightsUi.Blue, 1 => InsightsUi.Purple, 2 => Color.FromRgb(105, 220, 196), 3 => Color.FromRgb(244, 187, 113), _ => Color.FromRgb(244, 149, 172) };
    public HistoryChart(List<UsageSample> rows, DateTimeOffset start, DateTimeOffset end)
    {
        this.rows = rows; this.start = start; this.end = end; ids = rows.Select(s => s.Id).Distinct().ToList();
        MouseMove += (_, e) =>
        {
            if (rows.Count == 0) return;
            var pos = e.GetPosition(this);
            var nearest = rows.MinBy(s => (Position(s) - pos).Length)!;
            ToolTip = $"{nearest.Provider} · {nearest.Label}\n{nearest.At.LocalDateTime:dd.MM.yyyy HH:mm:ss}\n{nearest.Used:0.#}% wykorzystano · {100 - nearest.Used:0.#}% pozostało";
        };
    }
    Point Position(UsageSample s) => new(42 + (s.At - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds) * Math.Max(1, ActualWidth - 60),
        12 + (100 - s.Used) / 100 * Math.Max(1, ActualHeight - 46));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(31, 37, 46)), null, new Rect(RenderSize), 10, 10);
        void Label(string value, Point at) => dc.DrawText(new FormattedText(value, CultureInfo.GetCultureInfo("pl-PL"), FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, InsightsUi.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip), at);
        for (int p = 0; p <= 100; p += 25)
        {
            var y = Position(new("", "", "", start, p, null)).Y;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(53, 62, 76)), 1), new(42, y), new(Math.Max(42, ActualWidth - 18), y));
            Label(p + "%", new(4, y - 7));
        }
        for (int i = 0; i <= 4; i++)
        {
            var at = start + (end - start) * (i / 4.0);
            Label(at.LocalDateTime.ToString((end - start).TotalDays > 2 ? "dd.MM HH:mm" : "HH:mm"),
                new(42 + i / 4.0 * Math.Max(1, ActualWidth - 105), Math.Max(0, ActualHeight - 22)));
        }
        foreach (var group in rows.GroupBy(s => s.Id))
        {
            var brush = new SolidColorBrush(SeriesColor(ids.IndexOf(group.Key))); var pen = new Pen(brush, 2);
            UsageSample? prev = null;
            foreach (var s in group.OrderBy(s => s.At))
            {
                if (prev is not null && UsageHistory.CanConnect(prev, s)) dc.DrawLine(pen, Position(prev), Position(s));
                dc.DrawEllipse(brush, null, Position(s), 2.2, 2.2); prev = s;
            }
        }
    }
}
