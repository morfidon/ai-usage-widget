using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;

namespace AIUsageWidget;

public static class InsightsUi
{
    public static Brush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    public static readonly Brush Ink = Brush("#EDF2F7"), Muted = Brush("#ACB8C8"), Surface = Brush("#1D2632"), Line = Brush("#344153");
    public static readonly Color Blue = Color.FromRgb(126, 184, 250), Purple = Color.FromRgb(232, 175, 143);
    public static TextBlock Text(string text, double size = 12, Brush? brush = null) => new()
    { Text = text, FontSize = size, Foreground = brush ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) };
    public static ComboBox Choice(IEnumerable<string> values)
    {
        var c = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(8, 5, 8, 5), FontSize = 13 };
        foreach (var v in values) c.Items.Add(v);
        c.SelectedIndex = 0; return c;
    }
    public static Border Card(UIElement child) => new() { Child = child, Background = Surface, CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), BorderBrush = Line, BorderThickness = new Thickness(1) };
    public static FrameworkElement Metric(string label, string value)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
        p.Children.Add(Text(label, 11, Muted)); var number = Text(value, 24); number.FontWeight = FontWeights.SemiBold; p.Children.Add(number); return p;
    }
    public static FrameworkElement Metrics(params (string Label, string Value)[] values)
    {
        var p = new WrapPanel(); foreach (var (label, value) in values) p.Children.Add(Metric(label, value)); return p;
    }
    public static Expander Details(string label, UIElement content) => new() { Header = label, Content = content,
        Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
}

public sealed class InsightsWindow : Window
{
    readonly UsageHistory history;
    readonly TabControl tabs = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    readonly StackPanel historyBody = new() { Margin = new Thickness(6, 6, 6, 6) };
    readonly ComboBox provider = InsightsUi.Choice(new[] { "Codex", "Claude" });
    readonly ComboBox period = InsightsUi.Choice(new[] { "24 godziny", "7 dni" });
    readonly StackPanel projectBody = new(), diagnosticBody = new(), sessionBody = new(), overviewBody = new();
    readonly Dictionary<string, ProviderState> quotas = new();
    bool overviewDirty = true;
    public string SelectedProvider => (string)provider.SelectedItem;
    DateTimeOffset Since => DateTimeOffset.Now.AddDays(period.SelectedIndex == 0 ? -1 : -7);
    List<TokenEntry> Entries
    {
        get
        {
            var since = Since; var name = SelectedProvider;
            return analysis.Entries.Where(e => e.Provider == name && e.At >= since).ToList();
        }
    }
    AnalyticsSnapshot analysis = AnalyticsSnapshot.Empty;
    SessionSnapshot sessions = SessionSnapshot.Empty;
    bool projectsDirty = true, diagnosticsDirty = true, sessionsDirty = true;
    readonly Dictionary<string, bool> expanded = new();
    Expander Details(string key, string label, UIElement content, bool initiallyOpen = false)
    {
        var view = InsightsUi.Details(label, content);
        view.IsExpanded = expanded.GetValueOrDefault(key, initiallyOpen);
        view.Expanded += (_, _) => expanded[key] = true;
        view.Collapsed += (_, _) => expanded[key] = false;
        return view;
    }
    public InsightsWindow(UsageHistory history)
    {
        this.history = history;
        Title = "AI Usage · szczegóły"; Width = 820; Height = 760; MinWidth = 620; MinHeight = 440;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        Background = InsightsUi.Brush("#141B24"); Foreground = InsightsUi.Ink;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WidgetTheme.Apply(this);
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var title = InsightsUi.Text("Aktywność", 24); title.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        title.Margin = new Thickness(0, 0, 0, 18);
        AutomationProperties.SetName(provider, "Dostawca"); AutomationProperties.SetName(period, "Okres statystyk");
        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(provider); filters.Children.Add(period);
        DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
        root.Children.Add(tabs);
        void Tab(string label, StackPanel body) => tabs.Items.Add(new TabItem { Header = label,
            Content = new ScrollViewer { Content = body, Margin = new Thickness(6, 14, 6, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Padding = new Thickness(11, 8, 11, 8) });
        Tab("Przegląd", overviewBody); Tab("Limity", historyBody); Tab("Projekty", projectBody);
        Tab("Modele i tokeny", diagnosticBody); Tab("Sesje", sessionBody);
        provider.SelectionChanged += (_, _) => ChangeScope(); period.SelectionChanged += (_, _) => ChangeScope();
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) RefreshSelectedDetails(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        ChangeScope();
    }
    void ChangeScope()
    {
        Title = "AI Usage · " + SelectedProvider;
        overviewDirty = projectsDirty = diagnosticsDirty = sessionsDirty = true;
        RefreshHistory(); RefreshSelectedDetails();
    }
    public void SelectProvider(string name, bool overview = true)
    {
        if (name is not ("Codex" or "Claude")) return;
        provider.SelectedItem = name;
        if (overview) tabs.SelectedIndex = 0;
        RefreshSelectedDetails();
    }
    public void SelectSessions() => tabs.SelectedIndex = 4;
    internal void SelectTab(int tab) => tabs.SelectedIndex = tab + 1;
    public void UpdateQuotas(params ProviderState[] states)
    {
        foreach (var state in states)
        {
            if (!quotas.TryGetValue(state.Name, out var old) || !ReferenceEquals(old, state))
            { quotas[state.Name] = state; overviewDirty = true; }
        }
        RefreshSelectedDetails();
    }
    public void UpdateDetails(AnalyticsSnapshot analytics, SessionSnapshot snapshot)
    {
        if (!ReferenceEquals(analysis, analytics))
        {
            diagnosticsDirty |= !ReferenceEquals(analysis.Entries, analytics.Entries) || !ReferenceEquals(analysis.Tools, analytics.Tools)
                || analysis.Loading != analytics.Loading || analysis.Skipped != analytics.Skipped || analysis.At != analytics.At;
            analysis = analytics; projectsDirty = overviewDirty = true;
        }
        if (!ReferenceEquals(sessions, snapshot)) { sessions = snapshot; sessionsDirty = overviewDirty = true; }
        RefreshSelectedDetails();
    }
    void RefreshSelectedDetails()
    {
        if (tabs.SelectedIndex == 0 && overviewDirty) RefreshOverview();
        else if (tabs.SelectedIndex == 2 && projectsDirty) RefreshProjects();
        else if (tabs.SelectedIndex == 3 && diagnosticsDirty) RefreshDiagnostics();
        else if (tabs.SelectedIndex == 4 && sessionsDirty) RefreshSessions();
    }
    public static string Tokens(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.##} mln" : n >= 1000 ? $"{n / 1000.0:0.#} tys." : n.ToString("N0");
    public static string Count(int n, string one, string few, string many) => n.ToString("N0") + " "
        + (n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14) ? few : many);
    static Grid ValueRow(string label, string value)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = InsightsUi.Text(label, 14); name.Margin = new Thickness(0, 3, 14, 6);
        name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(name);
        var amount = InsightsUi.Text(value, 14); amount.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(amount, 1); row.Children.Add(amount); return row;
    }
    Button Navigate(string text, int tab)
    {
        var button = new Button { Content = text + "  →", HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = new SolidColorBrush(SelectedProvider == "Codex" ? InsightsUi.Blue : InsightsUi.Purple), Margin = new Thickness(0, 6, 0, 6) };
        button.Click += (_, _) => tabs.SelectedIndex = tab; return button;
    }
    void RefreshOverview()
    {
        overviewDirty = false; overviewBody.Children.Clear();
        var live = sessions.Sessions.Where(s => s.Provider == SelectedProvider).ToList();
        int waiting = live.Count(s => s.State == "waiting"), working = live.Count(s => s.State == "working");
        var sessionBlock = new StackPanel();
        sessionBlock.Children.Add(InsightsUi.Text("Sesje teraz", 16));
        sessionBlock.Children.Add(InsightsUi.Text(sessions.ErrorFor(SelectedProvider) is not null && live.Count == 0 ? "Brak odczytu" : sessions.At == DateTimeOffset.MinValue ? "Oczekiwanie na odczyt" : $"Pracuje: {working} · Czeka: {waiting}", 14));
        if (waiting > 0)
        {
            var waitingProjects = string.Join(", ", live.Where(s => s.State == "waiting")
                .Select(s => System.IO.Path.GetFileName(s.Directory.TrimEnd('\\', '/'))).Distinct().Take(3));
            sessionBlock.Children.Add(InsightsUi.Text(waitingProjects, 12, InsightsUi.Brush("#F1C879")));
        }
        if (sessions.ErrorFor(SelectedProvider) is string error) sessionBlock.Children.Add(InsightsUi.Text(error, 12, InsightsUi.Muted));
        sessionBlock.Children.Add(Navigate(waiting > 0 ? "Sprawdź oczekujące sesje" : "Otwórz sesje", 4));
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition());
        var liveCard = InsightsUi.Card(sessionBlock); liveCard.Margin = new Thickness(0, 0, 6, 12);
        top.Children.Add(liveCard); overviewBody.Children.Add(top);
        if (quotas.TryGetValue(SelectedProvider, out var state) && state.Windows.Count > 0)
        {
            var quota = new StackPanel();
            quota.Children.Add(InsightsUi.Text("Pozostały limit" + (state.Plan.Length > 0 ? " · " + state.Plan : ""), 16));
            foreach (var window in state.Windows)
            {
                quota.Children.Add(ValueRow((window.Group is null ? "" : window.Group + " · ") + window.Label, window.Expired ? "—" : $"{window.Remaining:0.#}%"));
                quota.Children.Add(InsightsUi.Text(window.Renews ? ResetFormatter.Format(window.ResetAt, DateTimeOffset.Now, TimeZoneInfo.Local).Countdown : "Bez resetu miesięcznego", 11, InsightsUi.Muted));
            }
            if (!state.Connected || !state.Updated.HasValue || DateTimeOffset.Now - state.Updated > TimeSpan.FromMinutes(10))
                quota.Children.Add(InsightsUi.Text("Ostatni zapis · oczekiwanie na aktualny odczyt", 12, InsightsUi.Brush("#F1C879")));
            quota.Children.Add(Navigate("Historia limitów", 1));
            var quotaCard = InsightsUi.Card(quota); quotaCard.Margin = new Thickness(6, 0, 0, 12); Grid.SetColumn(quotaCard, 1); top.Children.Add(quotaCard);
        }
        else { var link = Navigate("Historia limitów", 1); Grid.SetColumn(link, 1); top.Children.Add(link); }
        var entries = Entries;
        overviewBody.Children.Add(InsightsUi.Text("Lokalna aktywność · " + (string)period.SelectedItem, 16));
        if (analysis.Loading) overviewBody.Children.Add(InsightsUi.Text("Indeksowanie… wyniki częściowe", 12, InsightsUi.Muted));
        overviewBody.Children.Add(InsightsUi.Metrics(("Tokeny", Tokens(entries.Sum(e => e.Total))),
            ("Odpowiedzi", entries.Count.ToString("N0")), ("Projekty", entries.Select(e => e.Project).Distinct().Count().ToString("N0"))));
        if (entries.Count > 0)
        {
            var projects = new StackPanel(); projects.Children.Add(InsightsUi.Text("Najwięcej tokenów", 14, InsightsUi.Muted));
            foreach (var group in entries.GroupBy(e => e.Project).OrderByDescending(g => g.Sum(e => e.Total)).Take(3))
                projects.Children.Add(ValueRow(System.IO.Path.GetFileName(group.Key.TrimEnd('\\', '/')), Tokens(group.Sum(e => e.Total))));
            projects.Children.Add(Navigate("Wszystkie projekty", 2)); overviewBody.Children.Add(InsightsUi.Card(projects));
        }
        else overviewBody.Children.Add(InsightsUi.Text("Brak zapisanych odpowiedzi w tym okresie.", 12, InsightsUi.Muted));
        overviewBody.Children.Add(Navigate("Modele i skład tokenów", 3));
    }
    void RefreshProjects()
    {
        projectsDirty = false;
        projectBody.Children.Clear();
        string name = SelectedProvider;
        var entries = Entries;
        long total = entries.Sum(e => e.Total);
        var groups = entries.GroupBy(e => e.Project).OrderByDescending(g => g.Sum(e => e.Total)).ToList();
        projectBody.Children.Add(InsightsUi.Metrics(("Tokeny", Tokens(total)), ("Projekty", groups.Count.ToString()),
            ("Sesje", entries.Select(e => e.Session).Distinct().Count().ToString())));
        var note = InsightsUi.Text("Udział w lokalnych tokenach · nie w limicie abonamentu", 11, InsightsUi.Muted);
        projectBody.Children.Add(note);
        if (analysis.Loading) projectBody.Children.Add(InsightsUi.Text("Indeksowanie… wyniki częściowe", 12, InsightsUi.Muted));
        foreach (var g in groups.Take(20))
        {
            long tokens = g.Sum(e => e.Total);
            double share = total > 0 ? tokens * 100.0 / total : 0;
            var box = new StackPanel();
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string project = System.IO.Path.GetFileName(g.Key.TrimEnd('\\', '/'));
            var title = InsightsUi.Text(project, 15); title.FontWeight = FontWeights.SemiBold;
            title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Margin = new Thickness(0, 0, 14, 6); WidgetTips.Set(title, "Folder projektu", g.Key); heading.Children.Add(title);
            var amount = InsightsUi.Text($"{Tokens(tokens)}  ·  {share:0.#}%", 14); Grid.SetColumn(amount, 1); heading.Children.Add(amount);
            box.Children.Add(heading);
            box.Children.Add(new ProgressBar { Value = share, Height = 5, Margin = new Thickness(0, 4, 0, 8),
                Foreground = new SolidColorBrush(name == "Codex" ? InsightsUi.Blue : InsightsUi.Purple), Background = InsightsUi.Line, BorderThickness = new Thickness(0) });
            var detail = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            detail.Children.Add(InsightsUi.Text($"Wejście: {Tokens(g.Sum(e => e.Input))}   ·   Wyjście: {Tokens(g.Sum(e => e.Output))}", 12, InsightsUi.Muted));
            detail.Children.Add(InsightsUi.Text($"Odczyt cache: {Tokens(g.Sum(e => e.Cached))}   ·   Zapis cache: {Tokens(g.Sum(e => e.CacheWrite))}", 12, InsightsUi.Muted));
            detail.Children.Add(InsightsUi.Text(string.Join("  ·  ", g.GroupBy(e => e.Model).OrderByDescending(m => m.Sum(e => e.Total)).Select(m => m.Key + ": " + Tokens(m.Sum(e => e.Total)))), 11, InsightsUi.Muted));
            box.Children.Add(Details(name + ":" + g.Key, $"{Count(g.Select(e => e.Session).Distinct().Count(), "sesja", "sesje", "sesji")} · {Count(g.Count(), "odpowiedź", "odpowiedzi", "odpowiedzi")} · szczegóły", detail));
            projectBody.Children.Add(InsightsUi.Card(box));
        }
        if (total == 0) projectBody.Children.Add(InsightsUi.Card(InsightsUi.Text("Brak danych tokenów w tym okresie.", 14, InsightsUi.Muted)));
        if (groups.Count > 20) projectBody.Children.Add(InsightsUi.Text($"20 z {groups.Count} projektów · udział liczony ze wszystkich", 11, InsightsUi.Muted));
        if (analysis.Skipped > 0) projectBody.Children.Add(InsightsUi.Text($"Pominięte pliki lub rekordy: {analysis.Skipped}", 11, InsightsUi.Muted));
    }

    void RefreshDiagnostics()
    {
        diagnosticsDirty = false;
        diagnosticBody.Children.Clear();
        var since = Since; var selected = SelectedProvider;
        var entries = Entries;
        diagnosticBody.Children.Add(InsightsUi.Text("Skład zużycia tokenów", 19));
        if (analysis.Loading) diagnosticBody.Children.Add(InsightsUi.Text("Indeksowanie… wyniki częściowe", 12, InsightsUi.Muted));
        long input = entries.Sum(e => e.Input + e.Cached + e.CacheWrite), cached = entries.Sum(e => e.Cached), total = entries.Sum(e => e.Total);
        diagnosticBody.Children.Add(InsightsUi.Card(InsightsUi.Metrics(("Tokeny", Tokens(total)), ("Odpowiedzi", entries.Count.ToString("N0")),
            ("Sesje", entries.Select(e => e.Session).Distinct().Count().ToString("N0")))));
        diagnosticBody.Children.Add(InsightsUi.Card(InsightsUi.Metrics(("Wejście bez cache", Tokens(entries.Sum(e => e.Input))),
            ("Odczyt cache", Tokens(cached)), ("Wyjście", Tokens(entries.Sum(e => e.Output))))));
        if (SelectedProvider == "Claude") diagnosticBody.Children.Add(InsightsUi.Metrics(("Zapis cache", Tokens(entries.Sum(e => e.CacheWrite))),
            ("Tokeny subagentów", Tokens(entries.Where(e => e.Subagent).Sum(e => e.Total)))));
        diagnosticBody.Children.Add(InsightsUi.Text(input > 0 ? $"{100.0 * cached / input:0.#}% wejścia odczytano z cache" : "Brak danych wejścia", 12, InsightsUi.Muted));
        diagnosticBody.Children.Add(InsightsUi.Text("Modele", 17));
        foreach (var model in entries.GroupBy(e => e.Model).OrderByDescending(g => g.Sum(e => e.Total)))
        {
            var content = new StackPanel();
            content.Children.Add(ValueRow(model.Key, Tokens(model.Sum(e => e.Total)) + " tokenów"));
            content.Children.Add(InsightsUi.Text($"{Count(model.Count(), "odpowiedź", "odpowiedzi", "odpowiedzi")} · {Count(model.Select(e => e.Session).Distinct().Count(), "sesja", "sesje", "sesji")}", 11, InsightsUi.Muted));
            diagnosticBody.Children.Add(InsightsUi.Card(content));
        }
        if (entries.Count == 0) diagnosticBody.Children.Add(InsightsUi.Text("Brak zapisanych odpowiedzi w tym okresie.", 12, InsightsUi.Muted));
        var explanation = InsightsUi.Text("Tokeny pochodzą z lokalnych zapisów rozmów. Nie określają procentu abonamentu ani kosztu. Wejście bez cache, odczyt cache, zapis cache i wyjście to rozłączne części sumy. Liczba wywołań narzędzi nie określa ich kosztu."
            + (SelectedProvider == "Claude" ? $"\nWejście >200 tys. tokenów: {Count(entries.Count(e => e.Input + e.Cached + e.CacheWrite > 200_000), "odpowiedź", "odpowiedzi", "odpowiedzi")}. Stały kontekst sprawdzisz w /usage w Claude Code." : ""), 12, InsightsUi.Muted);
        diagnosticBody.Children.Add(Details("help", "Jak czytać te dane", explanation));
        foreach (var category in new[] { "MCP", "Umiejętności", "Subagenci" })
        {
            var content = new StackPanel();
            var groups = analysis.Tools.Where(t => t.Provider == selected && t.At >= since && t.Kind == category).GroupBy(t => t.Name).OrderByDescending(g => g.Count()).ToList();
            if (groups.Count == 0) continue;
            foreach (var g in groups.Take(10))
            {
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = InsightsUi.Text(g.Key, 12); label.Margin = new Thickness(0, 4, 12, 4); row.Children.Add(label);
                var count = InsightsUi.Text(Count(g.Count(), "wywołanie", "wywołania", "wywołań"), 12, InsightsUi.Muted);
                Grid.SetColumn(count, 1); row.Children.Add(count); content.Children.Add(row);
            }
            if (groups.Count > 10) content.Children.Add(InsightsUi.Text($"10 z {groups.Count} narzędzi", 11, InsightsUi.Muted));
            var section = Details(SelectedProvider + ":tools:" + category, $"{category} · {Count(groups.Sum(g => g.Count()), "wywołanie", "wywołania", "wywołań")}", content);
            diagnosticBody.Children.Add(InsightsUi.Card(section));
        }
    }

    void RefreshSessions()
    {
        sessionsDirty = false;
        sessionBody.Children.Clear();
        sessionBody.Children.Add(InsightsUi.Text("Aktywne sesje na tym komputerze", 19));
        foreach (var name in new[] { SelectedProvider })
        {
            var rows = sessions.Sessions.Where(s => s.Provider == name).OrderByDescending(s => s.State == "waiting").ToList();
            foreach (var s in rows)
            {
                var content = new StackPanel();
                var heading = new DockPanel();
                var status = InsightsUi.Text(s.StateLabel, 12, InsightsUi.Brush(s.State == "waiting" ? "#F1C879" : "#89D9C1"));
                DockPanel.SetDock(status, Dock.Right); heading.Children.Add(status);
                var title = InsightsUi.Text(WidgetTips.Line(s.Name, 100), 14); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 3, 14, 6); heading.Children.Add(title);
                content.Children.Add(heading);
                var path = InsightsUi.Text(s.Directory, 11, InsightsUi.Muted); path.TextWrapping = TextWrapping.NoWrap;
                path.TextTrimming = TextTrimming.CharacterEllipsis; WidgetTips.SetOverflow(path, "Folder sesji", s.Directory); content.Children.Add(path);
                if (s.Reason.Length > 0) content.Children.Add(InsightsUi.Text(s.Reason, 12, InsightsUi.Muted));
                sessionBody.Children.Add(InsightsUi.Card(content));
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
        if (history.Error is not null) historyBody.Children.Add(InsightsUi.Text(history.Error, 12, InsightsUi.Brush("#F1C879")));
        if (rows.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(8, 24, 8, 24) };
            empty.Children.Add(InsightsUi.Text("Jeszcze bez odczytów", 20));
            empty.Children.Add(InsightsUi.Text("Wykres pojawi się po pierwszym odczycie limitów w tym okresie.", 13, InsightsUi.Muted));
            historyBody.Children.Add(InsightsUi.Card(empty)); return;
        }
        int index = 0;
        foreach (var group in rows.GroupBy(s => s.Id))
        {
            var samples = group.OrderBy(s => s.At).ToList(); var last = samples[^1];
            var color = HistoryChart.SeriesColor(index++);
            var content = new StackPanel(); var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = InsightsUi.Text(last.Label, 15); label.FontWeight = FontWeights.SemiBold; header.Children.Add(label);
            var value = InsightsUi.Text($"{last.Used:0.#}% wykorzystano", 19, new SolidColorBrush(color));
            value.FontWeight = FontWeights.SemiBold; Grid.SetColumn(value, 1); header.Children.Add(value); content.Children.Add(header);
            content.Children.Add(InsightsUi.Text($"Ostatni odczyt · {last.At.LocalDateTime:dd.MM HH:mm}", 11, InsightsUi.Muted));
            content.Children.Add(new HistoryChart(samples, start, now, color) { Height = 130, Margin = new Thickness(0, 8, 0, 0) });
            historyBody.Children.Add(InsightsUi.Card(content));
        }
        var help = InsightsUi.Text("Najedź na wykres, aby zobaczyć odczyt. Przerwy oznaczają reset lub brak danych.", 11, InsightsUi.Muted);
        historyBody.Children.Add(help);
    }
}

// Each panel owns one quota series; resets and missing samples always break the line.
public sealed class HistoryChart : FrameworkElement
{
    readonly List<UsageSample> rows;
    readonly DateTimeOffset start, end;
    readonly Brush stroke, area;
    readonly Pen seriesPen, gridPen;
    DrawingGroup? plot;
    Size plotSize;
    UsageSample? hover;
    public static Color SeriesColor(int i) => (i % 5) switch
    { 0 => InsightsUi.Blue, 1 => Color.FromRgb(122, 210, 185), 2 => InsightsUi.Purple, 3 => Color.FromRgb(190, 170, 240), _ => Color.FromRgb(232, 195, 125) };
    public HistoryChart(List<UsageSample> rows, DateTimeOffset start, DateTimeOffset end, Color? color = null)
    {
        this.rows = rows.OrderBy(s => s.At).ToList(); this.start = start; this.end = end;
        var tint = color ?? InsightsUi.Blue;
        stroke = new SolidColorBrush(tint); stroke.Freeze();
        area = new SolidColorBrush(Color.FromArgb(20, tint.R, tint.G, tint.B)); area.Freeze();
        seriesPen = new Pen(stroke, 2); seriesPen.Freeze(); gridPen = new Pen(InsightsUi.Line, .7); gridPen.Freeze();
        ClipToBounds = true;
        AutomationProperties.SetName(this, rows.Count == 0 ? "Brak odczytów" : $"{rows[0].Label}: historia wykorzystania od 0 do 100 procent");
        MouseMove += (_, e) =>
        {
            var pos = e.GetPosition(this);
            var nearest = this.rows.Count == 0 || pos.X < LeftEdge || pos.X > RightEdge || pos.Y > BottomEdge ? null
                : this.rows.MinBy(s => Math.Abs(Position(s).X - pos.X));
            if (nearest is not null && Math.Abs(Position(nearest).X - pos.X) > 14) nearest = null;
            if (ReferenceEquals(nearest, hover)) return;
            hover = nearest;
            if (nearest is null) ToolTip = null;
            else WidgetTips.Set(this, $"{nearest.Used:0.#}% wykorzystano", $"{nearest.At.LocalDateTime:dd.MM.yyyy HH:mm}");
            InvalidateVisual();
        };
        MouseLeave += (_, _) => { if (hover is null) return; hover = null; ToolTip = null; InvalidateVisual(); };
    }
    const double LeftEdge = 40, TopEdge = 10;
    double RightEdge => Math.Max(LeftEdge + 1, ActualWidth - 20);
    double BottomEdge => Math.Max(TopEdge + 1, ActualHeight - 28);
    Point Position(UsageSample s) => new(LeftEdge + (s.At - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds) * (RightEdge - LeftEdge),
        TopEdge + (100 - s.Used) / 100 * (BottomEdge - TopEdge));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (plot is null || plotSize != RenderSize)
        {
            plot = new DrawingGroup(); plotSize = RenderSize;
            using (var drawing = plot.Open()) DrawPlot(drawing);
            plot.Freeze();
        }
        dc.DrawDrawing(plot);
        if (hover is not null)
        {
            var point = Position(hover);
            dc.DrawLine(gridPen, new(point.X, TopEdge), new(point.X, BottomEdge));
            dc.DrawEllipse(InsightsUi.Surface, seriesPen, point, 5, 5);
        }
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    { plot = null; base.OnDpiChanged(oldDpi, newDpi); InvalidateVisual(); }
    void DrawPlot(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        void Label(string value, Point at, double align = 0)
        {
            var text = new FormattedText(value, CultureInfo.GetCultureInfo("pl-PL"), FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, InsightsUi.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(at.X - text.Width * align, at.Y));
        }
        foreach (int p in new[] { 0, 50, 100 })
        {
            double y = TopEdge + (100 - p) / 100.0 * (BottomEdge - TopEdge);
            dc.DrawLine(gridPen, new(LeftEdge, y), new(RightEdge, y)); Label(p + "%", new(0, y - 7));
        }
        for (int i = 0; i <= 4; i++)
        {
            var at = start + (end - start) * (i / 4.0);
            Label(at.LocalDateTime.ToString((end - start).TotalDays > 2 ? "dd.MM" : "HH:mm"),
                new(LeftEdge + i / 4.0 * (RightEdge - LeftEdge), BottomEdge + 9), i == 0 ? 0 : i == 4 ? 1 : .5);
        }
        var segments = new List<List<UsageSample>>();
        foreach (var sample in rows)
        {
            if (segments.Count == 0 || !UsageHistory.CanConnect(segments[^1][^1], sample)) segments.Add(new());
            segments[^1].Add(sample);
        }
        foreach (var segment in segments)
        {
            if (segment.Count > 1)
            {
                var fill = new StreamGeometry();
                using (var c = fill.Open())
                {
                    c.BeginFigure(new(Position(segment[0]).X, BottomEdge), true, true);
                    foreach (var sample in segment) c.LineTo(Position(sample), true, false);
                    c.LineTo(new(Position(segment[^1]).X, BottomEdge), true, false);
                }
                fill.Freeze(); dc.DrawGeometry(area, null, fill);
                var line = new StreamGeometry();
                using (var c = line.Open())
                {
                    c.BeginFigure(Position(segment[0]), false, false);
                    foreach (var sample in segment.Skip(1)) c.LineTo(Position(sample), true, false);
                }
                line.Freeze(); dc.DrawGeometry(null, seriesPen, line);
            }
            // Only endpoints are dotted: dense histories remain a readable line.
            dc.DrawEllipse(stroke, null, Position(segment[0]), 2.5, 2.5);
            if (segment.Count > 1) dc.DrawEllipse(stroke, null, Position(segment[^1]), 3, 3);
        }
    }
}
