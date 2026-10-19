using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Threading.Tasks;

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
    readonly ComboBox period = InsightsUi.Choice(new[] { L.T("24 godziny"), L.T("7 dni"), L.T("Dzisiaj"), L.T("Ostatnia godzina") });
    readonly StackPanel projectBody = new(), diagnosticBody = new(), sessionBody = new(), overviewBody = new();
    readonly TextBox projectSearch = new() { MinWidth = 180, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 10, 10), Background = InsightsUi.Surface, Foreground = InsightsUi.Ink, BorderBrush = InsightsUi.Line };
    readonly ComboBox projectSort = InsightsUi.Choice(new[] { L.T("Najwięcej tokenów"), L.T("Ostatnia aktywność"), L.T("Nazwa projektu") });
    readonly ComboBox projectLimit = InsightsUi.Choice(new[] { L.T("20 projektów"), L.T("50 projektów"), L.T("Wszystkie") });
    readonly CheckBox compactProjects = new() { Content = L.T("Widok zwarty"), Foreground = InsightsUi.Ink, Margin = new Thickness(0, 6, 10, 10) };
    readonly ComboBox sessionState = InsightsUi.Choice(new[] { L.T("Wszystkie stany"), L.T("Pracuje"), L.T("Czeka na Ciebie"), L.T("Bezczynne") });
    readonly WrapPanel projectOptions = new(), liveOptions = new();
    readonly TextBlock scopeNote = InsightsUi.Text("", 11, InsightsUi.Muted);
    readonly Dictionary<string, ProviderState> quotas = new();
    string? selectedProject, selectedProjectProvider;
    bool projectModels;
    int projectPage;
    readonly Dictionary<string, string> sessionTitles = new();
    readonly HashSet<string> requestedTitles = new();
    bool closed;
    readonly bool allowSessionTitles;
    readonly Func<string, string?> openCodexSession;
    readonly Func<string, string?> openClaudeSession;
    bool overviewDirty = true;
    public string SelectedProvider => (string)provider.SelectedItem;
    internal int SelectedTabIndex => tabs.SelectedIndex;
    internal static DateTimeOffset PeriodStart(int index, DateTimeOffset now) => index switch
    { 1 => now.AddDays(-7), 2 => new DateTimeOffset(now.Date, TimeZoneInfo.Local.GetUtcOffset(now.Date)), 3 => now.AddHours(-1), _ => now.AddDays(-1) };
    DateTimeOffset Since => PeriodStart(period.SelectedIndex, DateTimeOffset.Now);
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
    public InsightsWindow(UsageHistory history, bool allowSessionTitles = true, Func<string, string?>? openCodexSession = null,
        Func<string, string?>? openClaudeSession = null, IEnumerable<string>? enabledProviders = null)
    {
        this.allowSessionTitles = allowSessionTitles;
        this.openCodexSession = openCodexSession ?? CodexNavigation.Open;
        this.openClaudeSession = openClaudeSession ?? ClaudeNavigation.Open;
        this.history = history;
        if (enabledProviders is not null) {
            provider.Items.Clear();
            foreach (string name in new[] { "Codex", "Claude" }.Where(p => enabledProviders.Contains(p, StringComparer.OrdinalIgnoreCase))) provider.Items.Add(name);
            if (provider.Items.Count == 0) { provider.Items.Add(L.T("Brak Codex / Claude")); provider.IsEnabled = false; }
            provider.SelectedIndex = 0;
        }
        Title = L.T("AI Usage · szczegóły"); Width = 820; Height = 760; MinWidth = 620; MinHeight = 440;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        Background = InsightsUi.Brush("#141B24"); Foreground = InsightsUi.Ink;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WidgetTheme.Apply(this);
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var title = InsightsUi.Text(L.T("Aktywność"), 24); title.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        title.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetName(provider, L.T("Dostawca")); AutomationProperties.SetName(period, L.T("Okres statystyk"));
        var filters = new WrapPanel();
        filters.Children.Add(provider); filters.Children.Add(period);
        DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
        AutomationProperties.SetName(projectSearch, L.T("Szukaj projektu"));
        projectSearch.ToolTip = L.T("Szukaj projektu");
        AutomationProperties.SetName(projectSort, L.T("Sortowanie projektów"));
        AutomationProperties.SetName(projectLimit, L.T("Liczba projektów"));
        AutomationProperties.SetName(sessionState, L.T("Stan sesji"));
        projectOptions.Children.Add(InsightsUi.Text(L.T("Szukaj projektu"), 12, InsightsUi.Muted));
        projectOptions.Children.Add(projectSearch); projectOptions.Children.Add(projectSort);
        projectOptions.Children.Add(projectLimit); projectOptions.Children.Add(compactProjects);
        liveOptions.Children.Add(sessionState);
        DockPanel.SetDock(scopeNote, Dock.Top); root.Children.Add(scopeNote);
        root.Children.Add(tabs);
        projectSearch.TextChanged += (_, _) => { projectsDirty = true; RefreshSelectedDetails(); };
        projectSort.SelectionChanged += (_, _) => { projectsDirty = true; RefreshSelectedDetails(); };
        projectLimit.SelectionChanged += (_, _) => { projectsDirty = true; RefreshSelectedDetails(); };
        compactProjects.Checked += (_, _) => { projectsDirty = true; RefreshSelectedDetails(); };
        compactProjects.Unchecked += (_, _) => { projectsDirty = true; RefreshSelectedDetails(); };
        sessionState.SelectionChanged += (_, _) => { sessionsDirty = true; RefreshSelectedDetails(); };
        void Tab(string label, StackPanel body) => tabs.Items.Add(new TabItem { Header = label,
            Content = new ScrollViewer { Content = body, Margin = new Thickness(6, 14, 6, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Padding = new Thickness(11, 8, 11, 8) });
        Tab(L.T("Przegląd"), overviewBody); Tab(L.T("Limity"), historyBody); Tab(L.T("Projekty"), projectBody);
        Tab(L.T("Modele i tokeny"), diagnosticBody); Tab(L.T("Sesje"), sessionBody);
        provider.SelectionChanged += (_, _) => ChangeScope(); period.SelectionChanged += (_, _) => ChangeScope();
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) RefreshSelectedDetails(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => closed = true;
        ChangeScope();
    }
    void ChangeScope()
    {
        if (selectedProjectProvider != SelectedProvider) selectedProject = null;
        projectPage = 0;
        Title = "AI Usage · " + SelectedProvider;
        overviewDirty = projectsDirty = diagnosticsDirty = sessionsDirty = true;
        RefreshHistory(); RefreshSelectedDetails();
    }
    public void SelectProvider(string name, bool overview = true)
    {
        if (name is not ("Codex" or "Claude") || !provider.Items.Contains(name)) return;
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
        if (!ReferenceEquals(sessions, snapshot)) { sessions = snapshot; sessionsDirty = overviewDirty = true; projectsDirty |= selectedProject is not null; }
        RefreshSelectedDetails();
    }
    void RefreshSelectedDetails()
    {
        projectOptions.Visibility = tabs.SelectedIndex == 2 && selectedProject is null ? Visibility.Visible : Visibility.Collapsed;
        liveOptions.Visibility = tabs.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        period.IsEnabled = tabs.SelectedIndex != 4;
        scopeNote.Text = L.T(tabs.SelectedIndex == 4 ? "Stan na żywo · niezależny od okresu statystyk" : tabs.SelectedIndex == 1 ? "Limity konta · odczyty dostawcy, nie tokeny" : "Tokeny lokalne · nie oznaczają kosztu ani procentu abonamentu");
        if (tabs.SelectedIndex == 0 && overviewDirty) RefreshOverview();
        else if (tabs.SelectedIndex == 2 && projectsDirty) RefreshProjects();
        else if (tabs.SelectedIndex == 3 && diagnosticsDirty) RefreshDiagnostics();
        else if (tabs.SelectedIndex == 4 && sessionsDirty) RefreshSessions();
    }
    public static string Tokens(long n) => n >= 1_000_000 ? L.F("{0:0.##} mln", n / 1_000_000.0) : n >= 1000 ? L.F("{0:0.#} tys.", n / 1000.0) : n.ToString("N0");
    public static string Count(int n, string one, string few, string many) => n.ToString("N0") + " "
        + (n == 1 ? one : !L.IsPolish ? many : n % 10 is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14) ? few : many);
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
        sessionBlock.Children.Add(InsightsUi.Text(L.T("Sesje teraz"), 16));
        sessionBlock.Children.Add(InsightsUi.Text(sessions.ErrorFor(SelectedProvider) is not null && live.Count == 0 ? L.T("Brak odczytu") : sessions.At == DateTimeOffset.MinValue ? L.T("Oczekiwanie na odczyt") : L.F("Pracuje: {0} · Czeka: {1}", working, waiting), 14));
        if (waiting > 0)
        {
            var waitingProjects = string.Join(", ", live.Where(s => s.State == "waiting")
                .Select(s => System.IO.Path.GetFileName(s.Directory.TrimEnd('\\', '/'))).Distinct().Take(3));
            sessionBlock.Children.Add(InsightsUi.Text(waitingProjects, 12, InsightsUi.Brush("#F1C879")));
        }
        if (sessions.ErrorFor(SelectedProvider) is string error) sessionBlock.Children.Add(InsightsUi.Text(L.Display(error), 12, InsightsUi.Muted));
        sessionBlock.Children.Add(Navigate(waiting > 0 ? L.T("Sprawdź oczekujące sesje") : L.T("Otwórz sesje"), 4));
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition());
        var liveCard = InsightsUi.Card(sessionBlock); liveCard.Margin = new Thickness(0, 0, 6, 12);
        top.Children.Add(liveCard); overviewBody.Children.Add(top);
        if (quotas.TryGetValue(SelectedProvider, out var state) && state.Windows.Count > 0)
        {
            var quota = new StackPanel();
            quota.Children.Add(InsightsUi.Text(L.T("Pozostały limit") + (state.Plan.Length > 0 ? " · " + state.Plan : ""), 16));
            foreach (var window in state.Windows)
            {
                quota.Children.Add(ValueRow((window.Group is null ? "" : window.Group + " · ") + window.Label, window.Expired ? "—" : $"{window.Remaining:0.#}%"));
                quota.Children.Add(InsightsUi.Text(window.Renews ? ResetFormatter.Format(window.ResetAt, DateTimeOffset.Now, TimeZoneInfo.Local).Countdown : L.T("Bez resetu miesięcznego"), 11, InsightsUi.Muted));
            }
            if (!state.Connected || !state.Updated.HasValue || DateTimeOffset.Now - state.Updated > TimeSpan.FromMinutes(10))
                quota.Children.Add(InsightsUi.Text(L.T("Ostatni zapis · oczekiwanie na aktualny odczyt"), 12, InsightsUi.Brush("#F1C879")));
            quota.Children.Add(Navigate(L.T("Historia limitów"), 1));
            var quotaCard = InsightsUi.Card(quota); quotaCard.Margin = new Thickness(6, 0, 0, 12); Grid.SetColumn(quotaCard, 1); top.Children.Add(quotaCard);
        }
        else { var link = Navigate(L.T("Historia limitów"), 1); Grid.SetColumn(link, 1); top.Children.Add(link); }
        var entries = Entries;
        overviewBody.Children.Add(InsightsUi.Text(L.T("Lokalna aktywność · ") + (string)period.SelectedItem, 16));
        if (analysis.Loading) overviewBody.Children.Add(InsightsUi.Text(L.T("Indeksowanie… wyniki częściowe"), 12, InsightsUi.Muted));
        overviewBody.Children.Add(InsightsUi.Card(InsightsUi.Metrics((L.T("Tokeny"), Tokens(entries.Sum(e => e.Total))),
            (L.T("Odpowiedzi"), entries.Count.ToString("N0")), (L.T("Projekty"), entries.Select(e => e.Project).Distinct().Count().ToString("N0")))));
        if (entries.Count > 0)
        {
            var activity = new StackPanel();
            activity.Children.Add(InsightsUi.Text(L.T("Aktywność w czasie"), 16));
            var chart = new System.Windows.Controls.Primitives.UniformGrid { Columns = 24, Height = 100, Margin = new Thickness(0, 8, 0, 8) };
            var start = Since; var end = DateTimeOffset.Now;
            double duration = Math.Max(1, (end - start).TotalSeconds);
            var buckets = new long[24];
            foreach (var entry in entries) buckets[Math.Clamp((int)((entry.At - start).TotalSeconds / duration * 24), 0, 23)] += entry.Total;
            long maximum = Math.Max(1, buckets.Max());
            for (int i = 0; i < buckets.Length; i++)
            {
                var bar = new Border { Background = InsightsUi.Brush("#18222E"), Margin = new Thickness(2, 0, 2, 0), CornerRadius = new CornerRadius(3) };
                bar.Child = new Border { Background = new SolidColorBrush(SelectedProvider == "Codex" ? InsightsUi.Blue : InsightsUi.Purple),
                    Height = buckets[i] == 0 ? 0 : Math.Max(3, 100.0 * buckets[i] / maximum), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3) };
                var at = start.AddSeconds(duration * i / 24);
                string label = at.LocalDateTime.ToString(L.ShortTimestampPattern, L.Culture) + " – " + start.AddSeconds(duration * (i + 1) / 24).LocalDateTime.ToString(L.ShortTimestampPattern, L.Culture);
                WidgetTips.Set(bar, Tokens(buckets[i]) + L.T(" tokenów"), label);
                AutomationProperties.SetName(bar, label + ": " + Tokens(buckets[i]) + L.T(" tokenów"));
                chart.Children.Add(bar);
            }
            activity.Children.Add(chart);
            activity.Children.Add(ValueRow(start.LocalDateTime.ToString(L.ShortTimestampPattern, L.Culture), end.LocalDateTime.ToString(L.ShortTimestampPattern, L.Culture)));
            activity.Children.Add(InsightsUi.Text(L.T("Brak słupka oznacza brak zapisanych tokenów, nie potwierdzoną bezczynność."), 11, InsightsUi.Muted));
            overviewBody.Children.Add(InsightsUi.Card(activity));
            overviewBody.Children.Add(Navigate(L.T("Wszystkie projekty"), 2));
        }
        else overviewBody.Children.Add(InsightsUi.Text(L.T("Brak zapisanych odpowiedzi w tym okresie."), 12, InsightsUi.Muted));
        overviewBody.Children.Add(Navigate(L.T("Modele i skład tokenów"), 3));
    }
    void RefreshProjects()
    {
        projectsDirty = false;
        projectOptions.Visibility = selectedProject is null ? Visibility.Visible : Visibility.Collapsed;
        if (selectedProject is not null) { projectBody.Children.Clear(); RenderProject(); return; }
        // Keep focused controls attached while typing or changing a filter.
        if (projectBody.Children.Count > 0 && ReferenceEquals(projectBody.Children[0], projectOptions))
            projectBody.Children.RemoveRange(1, projectBody.Children.Count - 1);
        else { projectBody.Children.Clear(); projectBody.Children.Add(projectOptions); }
        string name = SelectedProvider;
        var entries = Entries;
        long total = entries.Sum(e => e.Total);
        var allGroups = entries.GroupBy(e => e.Project).ToList();
        var matching = allGroups.Where(g => g.Key.Contains(projectSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        var groups = (projectSort.SelectedIndex switch {
            1 => matching.OrderByDescending(g => g.Max(e => e.At)),
            2 => matching.OrderBy(g => System.IO.Path.GetFileName(g.Key.TrimEnd('\\', '/')), StringComparer.CurrentCultureIgnoreCase),
            _ => matching.OrderByDescending(g => g.Sum(e => e.Total))
        }).ToList();
        int limit = projectLimit.SelectedIndex switch { 1 => 50, 2 => int.MaxValue, _ => 20 };
        projectBody.Children.Add(ValueRow(L.T("Tokeny we wszystkich projektach"), Tokens(total)));
        if (analysis.Loading) projectBody.Children.Add(InsightsUi.Text(L.T("Indeksowanie… wyniki częściowe"), 12, InsightsUi.Muted));
        foreach (var g in groups.Take(limit))
        {
            long tokens = g.Sum(e => e.Total);
            double share = total > 0 ? tokens * 100.0 / total : 0;
            var box = new StackPanel();
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string project = System.IO.Path.GetFileName(g.Key.TrimEnd('\\', '/'));
            var title = InsightsUi.Text(project, 16); title.FontWeight = FontWeights.SemiBold;
            title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Margin = new Thickness(0, 0, 14, 6);
            var open = ActionButton(title, () => OpenProject(g.Key)); open.Padding = new Thickness(0);
            AutomationProperties.SetName(open, L.T("Otwórz projekt ") + g.Key); heading.Children.Add(open);
            var amount = InsightsUi.Text(L.F("{0} tokenów  ·  {1:0.#}%", Tokens(tokens), share), 15); Grid.SetColumn(amount, 1); heading.Children.Add(amount);
            box.Children.Add(heading);
            box.Children.Add(new ProgressBar { Value = share, Height = 5, Margin = new Thickness(0, 4, 0, 8),
                Foreground = new SolidColorBrush(name == "Codex" ? InsightsUi.Blue : InsightsUi.Purple), Background = InsightsUi.Line, BorderThickness = new Thickness(0) });
            var actionRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            WidgetTips.Set(open, L.T("Sesje i modele  →"), g.Key);
            actionRow.Children.Add(InsightsUi.Text($"{Count(g.Select(e => e.Session).Distinct().Count(), L.T("sesja"), L.T("sesje"), L.T("sesji"))} · {Count(g.Count(), L.T("odpowiedź"), L.T("odpowiedzi"), L.T("odpowiedzi"))}", 13, InsightsUi.Muted));
            box.Children.Add(actionRow);
            if (compactProjects.IsChecked != true) box.Children.Add(Details(name + ":" + g.Key, L.T("Skład tokenów"), TokenComposition(TokenTotals.From(g))));
            var card = InsightsUi.Card(box);
            if (compactProjects.IsChecked == true) { card.Padding = new Thickness(12, 8, 12, 8); card.Margin = new Thickness(0, 0, 0, 6); }
            projectBody.Children.Add(card);
        }
        if (total == 0) projectBody.Children.Add(InsightsUi.Card(InsightsUi.Text(L.T("Brak danych tokenów w tym okresie."), 14, InsightsUi.Muted)));
        if (groups.Count == 0 && entries.Count > 0) projectBody.Children.Add(InsightsUi.Text(L.T("Brak projektów pasujących do wyszukiwania."), 14, InsightsUi.Muted));
        projectBody.Children.Add(InsightsUi.Text(L.F("Pokazano {0} z {1} · udział ze wszystkich projektów", Math.Min(limit, groups.Count), groups.Count), 11, InsightsUi.Muted));
        if (analysis.Skipped > 0) projectBody.Children.Add(InsightsUi.Text(L.F("Pominięte pliki lub rekordy: {0}", analysis.Skipped), 11, InsightsUi.Muted));
    }

    static Button ActionButton(object label, Action action)
    {
        var button = new Button { Content = label, FontSize = 13, Foreground = new SolidColorBrush(InsightsUi.Blue),
            HorizontalContentAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => action(); return button;
    }
    void OpenProject(string path)
    {
        selectedProject = path; selectedProjectProvider = SelectedProvider; projectModels = false; projectPage = 0;
        RefreshProjects();
        ScrollProjectsToTop();
    }
    void ScrollProjectsToTop()
    {
        for (DependencyObject? p = projectBody; p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer scroll) { scroll.ScrollToTop(); break; }
    }
    async void LoadSessionTitles(List<SessionUsage> page)
    {
        if (!allowSessionTitles || SelectedProvider != "Codex") return;
        var ids = page.Select(s => s.Id).Where(id => !string.IsNullOrWhiteSpace(id) && requestedTitles.Add(id)).ToArray();
        if (ids.Length == 0) return;
        var project = selectedProject;
        try
        {
            var titles = await Task.Run(() => LocalSqlite.Read(System.IO.Path.Combine(LocalPaths.Codex, "state_5.sqlite"),
                "SELECT id,title FROM threads WHERE id IN (" + string.Join(",", ids.Select(id => "'" + id.Replace("'", "''") + "'")) + ")", 2));
            if (closed) return;
            foreach (var row in titles) if (!string.IsNullOrWhiteSpace(row[1])) sessionTitles[row[0]] = WidgetTips.Line(row[1], 80);
            if (titles.Count > 0 && SelectedProvider == "Codex" && selectedProject == project && !projectModels && tabs.SelectedIndex == 2) RefreshProjects();
        }
        catch { /* Historical titles are optional; identifiers and dates remain usable. */ }
    }
    static FrameworkElement TokenComposition(TokenTotals tokens)
    {
        var cells = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(-4, 8, -4, 0) };
        foreach (var (label, value) in new[] { (L.T("Wejście bez cache"), tokens.Input), (L.T("Odczyt cache"), tokens.Cached), (L.T("Wyjście"), tokens.Output), (L.T("Zapis cache"), tokens.CacheWrite) })
        {
            var cell = new StackPanel(); cell.Children.Add(InsightsUi.Text(label, 13, InsightsUi.Muted));
            var number = InsightsUi.Text(Tokens(value), 21, label == L.T("Odczyt cache") ? InsightsUi.Brush("#89D9C1") : InsightsUi.Ink);
            number.FontWeight = FontWeights.SemiBold; cell.Children.Add(number);
            cells.Children.Add(new Border { Child = cell, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4),
                Background = InsightsUi.Brush("#16202C"), CornerRadius = new CornerRadius(6) });
        }
        return cells;
    }
    Expander LazyDetails(string key, string label, Func<UIElement> build)
    {
        var content = new StackPanel(); var view = Details(key, label, content);
        void Fill() { if (content.Children.Count == 0) content.Children.Add(build()); }
        view.Expanded += (_, _) => Fill(); if (view.IsExpanded) Fill(); return view;
    }
    void RenderProject()
    {
        var data = ProjectBreakdown.Build(analysis.Entries, SelectedProvider, selectedProject!, Since);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var back = ActionButton(L.T("← Projekty"), () => { selectedProject = null; RefreshProjects(); ScrollProjectsToTop(); });
        back.Margin = new Thickness(0, 0, 12, 0); top.Children.Add(back);
        var heading = InsightsUi.Text(System.IO.Path.GetFileName(selectedProject!.TrimEnd('\\', '/')), 20);
        heading.FontWeight = FontWeights.SemiBold; heading.TextWrapping = TextWrapping.NoWrap; heading.TextTrimming = TextTrimming.CharacterEllipsis;
        WidgetTips.Set(heading, L.T("Folder projektu"), selectedProject); top.Children.Add(heading); projectBody.Children.Add(top);
        projectBody.Children.Add(InsightsUi.Text(L.F("{0} tokenów · {1}", Tokens(data.Tokens.Total), Count(data.Tokens.Responses, L.T("odpowiedź"), L.T("odpowiedzi"), L.T("odpowiedzi"))), 18));
        if (analysis.Loading) projectBody.Children.Add(InsightsUi.Text(L.T("Indeksowanie… wyniki częściowe"), 13, InsightsUi.Muted));
        if (data.Tokens.Total > 0)
            projectBody.Children.Add(InsightsUi.Text(L.F("{0:0.#}% tokenów to odczyt cache · udział nie oznacza kosztu", 100.0 * data.Tokens.Cached / data.Tokens.Total), 13, InsightsUi.Muted));
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) };
        var sessionChoice = ActionButton(L.F("Sesje ({0})", data.Sessions.Count), () => { projectModels = false; projectPage = 0; RefreshProjects(); });
        var modelChoice = ActionButton(L.F("Modele ({0})", data.Models.Count), () => { projectModels = true; projectPage = 0; RefreshProjects(); });
        sessionChoice.Background = projectModels ? Brushes.Transparent : InsightsUi.Brush("#2C405B");
        modelChoice.Background = projectModels ? InsightsUi.Brush("#2C405B") : Brushes.Transparent;
        AutomationProperties.SetName(sessionChoice, L.T("Ranking sesji projektu")); AutomationProperties.SetName(modelChoice, L.T("Ranking modeli projektu"));
        choices.Children.Add(sessionChoice); choices.Children.Add(modelChoice); projectBody.Children.Add(choices);
        projectBody.Children.Add(InsightsUi.Text(L.T("Największe zużycie · udział w tokenach projektu"), 13, InsightsUi.Muted));
        if (data.Sessions.Count == 0) { projectBody.Children.Add(InsightsUi.Text(L.T("Brak odpowiedzi w wybranym okresie."), 14)); return; }
        if (projectModels)
        {
            foreach (var model in data.Models)
            {
                var row = new StackPanel(); row.Children.Add(RankingHeading(model.Name, model.Tokens.Total, data.Tokens.Total));
                row.Children.Add(InsightsUi.Text($"{Count(model.Sessions, L.T("sesja"), L.T("sesje"), L.T("sesji"))} · {Count(model.Tokens.Responses, L.T("odpowiedź"), L.T("odpowiedzi"), L.T("odpowiedzi"))}", 13, InsightsUi.Muted));
                row.Children.Add(LazyDetails($"{SelectedProvider}:{selectedProject}:model:{model.Name}", L.T("Skład tokenów"), () => TokenComposition(model.Tokens)));
                projectBody.Children.Add(InsightsUi.Card(row));
            }
            return;
        }
        const int pageSize = 15;
        projectPage = Math.Clamp(projectPage, 0, (data.Sessions.Count - 1) / pageSize);
        if (data.Sessions.Count > pageSize) projectBody.Children.Add(ProjectPager(data.Sessions.Count, pageSize));
        var visibleSessions = data.Sessions.Skip(projectPage * pageSize).Take(pageSize).ToList();
        foreach (var session in visibleSessions)
        {
            var row = new StackPanel();
            var live = sessions.Sessions.FirstOrDefault(s => s.Provider == SelectedProvider && s.Id == session.Id);
            string id = session.Id.Length > 16 ? session.Id[..8] + "…" + session.Id[^6..] : session.Id;
            string title = live is not null && !string.IsNullOrWhiteSpace(live.Name) ? WidgetTips.Line(live.Name, 70)
                : SelectedProvider == "Codex" && sessionTitles.TryGetValue(session.Id, out var savedTitle) ? savedTitle : L.T("Sesja ") + id;
            var launchError = InsightsUi.Text("", 13, InsightsUi.Brush("#F1BE88"));
            launchError.Visibility = Visibility.Collapsed;
            var sessionHeading = (Grid)RankingHeading(title, session.Tokens.Total, data.Tokens.Total);
            if (SelectedProvider is "Codex" or "Claude")
            {
                string targetProvider = SelectedProvider;
                var openSession = targetProvider == "Codex" ? openCodexSession : openClaudeSession;
                var label = sessionHeading.Children.OfType<TextBlock>().First();
                sessionHeading.Children.Remove(label);
                label.Text += " ↗"; label.Foreground = new SolidColorBrush(targetProvider == "Codex" ? InsightsUi.Blue : InsightsUi.Purple);
                var open = new Button { Content = label, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetName(open, L.T("Otwórz w ") + targetProvider + ": " + title);
                WidgetTips.Set(open, L.T("Otwórz rozmowę w ") + (targetProvider == "Claude" ? "Claude Desktop" : "Codex"), "");
                open.Click += async (_, _) =>
                {
                    open.IsEnabled = false; launchError.Visibility = Visibility.Collapsed;
                    try
                    {
                        string? error = await Task.Run(() => openSession(session.Id));
                        if (error is not null) { launchError.Text = error; launchError.Visibility = Visibility.Visible; }
                    }
                    catch { launchError.Text = L.T("Nie udało się otworzyć ") + targetProvider + L.T(". Spróbuj ponownie lub skopiuj ID sesji."); launchError.Visibility = Visibility.Visible; }
                    finally { open.IsEnabled = true; }
                };
                sessionHeading.Children.Add(open);
            }
            row.Children.Add(sessionHeading);
            row.Children.Add(launchError);
            var meta = new DockPanel();
            var copy = new Button { Content = L.T("Kopiuj ID"), FontSize = 13, Foreground = new SolidColorBrush(InsightsUi.Blue) };
            copy.Click += (_, _) => { try { Clipboard.SetText(session.Id); copy.Content = L.T("Skopiowano"); } catch (System.Runtime.InteropServices.ExternalException) { copy.Content = L.T("Spróbuj ponownie"); } };
            DockPanel.SetDock(copy, Dock.Right); meta.Children.Add(copy);
            AutomationProperties.SetName(copy, L.T("Kopiuj ID sesji ") + session.Id);
            string first = session.First.LocalDateTime.ToString(L.ShortTimestampPattern, L.Culture), last = session.Last.LocalDateTime.ToString(L.ShortTimestampPattern, L.Culture);
            meta.Children.Add(InsightsUi.Text((first == last ? first : first + " – " + last) + " · " + Count(session.Tokens.Responses, L.T("odpowiedź"), L.T("odpowiedzi"), L.T("odpowiedzi")), 13, InsightsUi.Muted));
            row.Children.Add(meta);
            row.Children.Add(LazyDetails($"{SelectedProvider}:{selectedProject}:session:{session.Id}", L.F("Modele ({0}) i skład tokenów", session.Models.Count), () =>
            {
                var content = new StackPanel(); content.Children.Add(TokenComposition(session.Tokens));
                content.Children.Add(InsightsUi.Text(L.T("Modele w tej sesji"), 14));
                foreach (var model in session.Models) content.Children.Add(RankingHeading(model.Name, model.Tokens.Total, session.Tokens.Total));
                return content;
            }));
            projectBody.Children.Add(InsightsUi.Card(row));
        }
        if (data.Sessions.Count > pageSize) projectBody.Children.Add(ProjectPager(data.Sessions.Count, pageSize));
        LoadSessionTitles(visibleSessions);
    }
    FrameworkElement ProjectPager(int count, int pageSize)
    {
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
        var previous = ActionButton("←", () => { projectPage--; RefreshProjects(); ScrollProjectsToTop(); }); previous.IsEnabled = projectPage > 0;
        var next = ActionButton("→", () => { projectPage++; RefreshProjects(); ScrollProjectsToTop(); }); next.IsEnabled = (projectPage + 1) * pageSize < count;
        AutomationProperties.SetName(previous, L.T("Poprzednia strona sesji")); AutomationProperties.SetName(next, L.T("Następna strona sesji"));
        pager.Children.Add(previous);
        pager.Children.Add(InsightsUi.Text(L.F(" {0}–{1} z {2} ", projectPage * pageSize + 1, Math.Min((projectPage + 1) * pageSize, count), count), 13));
        pager.Children.Add(next); return pager;
    }
    static FrameworkElement RankingHeading(string title, long total, long denominator)
    {
        var row = ValueRow(title, $"{Tokens(total)}  ·  {(denominator > 0 ? 100.0 * total / denominator : 0):0.#}%");
        foreach (var text in row.Children.OfType<TextBlock>()) text.FontSize = 15;
        return row;
    }

    void RefreshDiagnostics()
    {
        diagnosticsDirty = false;
        diagnosticBody.Children.Clear();
        var since = Since; var selected = SelectedProvider;
        var entries = Entries;
        diagnosticBody.Children.Add(InsightsUi.Text(L.T("Skład zużycia tokenów"), 19));
        if (analysis.Loading) diagnosticBody.Children.Add(InsightsUi.Text(L.T("Indeksowanie… wyniki częściowe"), 12, InsightsUi.Muted));
        long input = entries.Sum(e => e.Input + e.Cached + e.CacheWrite), cached = entries.Sum(e => e.Cached), total = entries.Sum(e => e.Total);
        diagnosticBody.Children.Add(InsightsUi.Card(TokenComposition(TokenTotals.From(entries))));
        if (SelectedProvider == "Claude") diagnosticBody.Children.Add(InsightsUi.Text(L.T("Tokeny subagentów") + ": " + Tokens(entries.Where(e => e.Subagent).Sum(e => e.Total)), 12, InsightsUi.Muted));
        diagnosticBody.Children.Add(InsightsUi.Text(input > 0 ? L.F("{0:0.#}% wejścia odczytano z cache", 100.0 * cached / input) : L.T("Brak danych wejścia"), 12, InsightsUi.Muted));
        diagnosticBody.Children.Add(InsightsUi.Text(L.T("Modele"), 17));
        foreach (var model in entries.GroupBy(e => e.Model).OrderByDescending(g => g.Sum(e => e.Total)))
        {
            var content = new StackPanel();
            content.Children.Add(ValueRow(model.Key, Tokens(model.Sum(e => e.Total)) + L.T(" tokenów")));
            content.Children.Add(InsightsUi.Text($"{Count(model.Count(), L.T("odpowiedź"), L.T("odpowiedzi"), L.T("odpowiedzi"))} · {Count(model.Select(e => e.Session).Distinct().Count(), L.T("sesja"), L.T("sesje"), L.T("sesji"))}", 11, InsightsUi.Muted));
            diagnosticBody.Children.Add(InsightsUi.Card(content));
        }
        if (entries.Count == 0) diagnosticBody.Children.Add(InsightsUi.Text(L.T("Brak zapisanych odpowiedzi w tym okresie."), 12, InsightsUi.Muted));
        var explanation = InsightsUi.Text(L.T("Tokeny pochodzą z lokalnych zapisów rozmów. Nie określają procentu abonamentu ani kosztu. Wejście bez cache, odczyt cache, zapis cache i wyjście to rozłączne części sumy. Liczba wywołań narzędzi nie określa ich kosztu.")
            + (SelectedProvider == "Claude" ? L.F("\nWejście >200 tys. tokenów: {0}. Stały kontekst sprawdzisz w /usage w Claude Code.", Count(entries.Count(e => e.Input + e.Cached + e.CacheWrite > 200_000), L.T("odpowiedź"), L.T("odpowiedzi"), L.T("odpowiedzi"))) : ""), 12, InsightsUi.Muted);
        diagnosticBody.Children.Add(Details("help", L.T("Jak czytać te dane"), explanation));
        foreach (var category in new[] { "MCP", "Umiejętności", "Subagenci" })
        {
            var content = new StackPanel();
            var groups = analysis.Tools.Where(t => t.Provider == selected && t.At >= since && t.Kind == category).GroupBy(t => t.Name).OrderByDescending(g => g.Count()).ToList();
            if (groups.Count == 0) continue;
            foreach (var g in groups.Take(10))
            {
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = InsightsUi.Text(g.Key, 12); label.Margin = new Thickness(0, 4, 12, 4); row.Children.Add(label);
                var count = InsightsUi.Text(Count(g.Count(), L.T("wywołanie"), L.T("wywołania"), L.T("wywołań")), 12, InsightsUi.Muted);
                Grid.SetColumn(count, 1); row.Children.Add(count); content.Children.Add(row);
            }
            if (groups.Count > 10) content.Children.Add(InsightsUi.Text(L.F("10 z {0} narzędzi", groups.Count), 11, InsightsUi.Muted));
            var section = Details(SelectedProvider + ":tools:" + category, $"{L.T(category)} · {Count(groups.Sum(g => g.Count()), L.T("wywołanie"), L.T("wywołania"), L.T("wywołań"))}", content);
            diagnosticBody.Children.Add(InsightsUi.Card(section));
        }
    }

    void RefreshSessions()
    {
        sessionsDirty = false;
        if (sessionBody.Children.Count > 0 && ReferenceEquals(sessionBody.Children[0], liveOptions))
            sessionBody.Children.RemoveRange(1, sessionBody.Children.Count - 1);
        else { sessionBody.Children.Clear(); sessionBody.Children.Add(liveOptions); }
        sessionBody.Children.Add(InsightsUi.Text(L.T("Aktywne sesje na tym komputerze"), 19));
        foreach (var name in new[] { SelectedProvider })
        {
            var rows = sessions.Sessions.Where(s => s.Provider == name && (sessionState.SelectedIndex switch {
                1 => s.State == "working", 2 => s.State == "waiting", 3 => s.State != "working" && s.State != "waiting", _ => true
            })).OrderByDescending(s => s.State == "waiting").ToList();
            foreach (var s in rows)
            {
                var content = new StackPanel();
                var heading = new DockPanel();
                var status = InsightsUi.Text(s.StateLabel, 12, InsightsUi.Brush(s.State == "waiting" ? "#F1C879" : "#89D9C1"));
                DockPanel.SetDock(status, Dock.Right); heading.Children.Add(status);
                var title = InsightsUi.Text(WidgetTips.Line(s.Name, 100), 14); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 3, 14, 6); heading.Children.Add(title);
                content.Children.Add(heading);
                var path = InsightsUi.Text(s.Directory, 11, InsightsUi.Muted); path.TextWrapping = TextWrapping.NoWrap;
                path.TextTrimming = TextTrimming.CharacterEllipsis; WidgetTips.SetOverflow(path, L.T("Folder sesji"), s.Directory); content.Children.Add(path);
                if (s.Reason.Length > 0) content.Children.Add(InsightsUi.Text(L.Display(s.Reason), 12, InsightsUi.Muted));
                sessionBody.Children.Add(InsightsUi.Card(content));
            }
            if (rows.Count == 0) sessionBody.Children.Add(InsightsUi.Text(L.Display(sessions.ErrorFor(name) ?? L.T(sessionState.SelectedIndex == 0 ? "Brak aktywnych sesji." : "Brak sesji w wybranym stanie.")), 12, InsightsUi.Muted));
        }
        sessionBody.Children.Add(InsightsUi.Text(sessions.At == DateTimeOffset.MinValue ? L.T("Oczekiwanie na pierwszy odczyt…") : L.F("Odczyt: {0:HH:mm:ss}", sessions.At.LocalDateTime), 10, InsightsUi.Muted));
    }
    public void RefreshHistory()
    {
        historyBody.Children.Clear();
        var now = DateTimeOffset.Now;
        var start = PeriodStart(period.SelectedIndex, now);
        var rows = history.Samples.Where(s => s.Provider == (string)provider.SelectedItem && s.At >= start).ToList();
        if (history.Error is not null) historyBody.Children.Add(InsightsUi.Text(L.Display(history.Error), 12, InsightsUi.Brush("#F1C879")));
        if (rows.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(8, 24, 8, 24) };
            empty.Children.Add(InsightsUi.Text(L.T("Jeszcze bez odczytów"), 20));
            empty.Children.Add(InsightsUi.Text(L.T("Wykres pojawi się po pierwszym odczycie limitów w tym okresie."), 13, InsightsUi.Muted));
            historyBody.Children.Add(InsightsUi.Card(empty)); return;
        }
        int index = 0;
        foreach (var group in rows.GroupBy(s => s.Id))
        {
            var samples = group.OrderBy(s => s.At).ToList(); var last = samples[^1];
            var color = HistoryChart.SeriesColor(index++);
            var content = new StackPanel(); var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = InsightsUi.Text(L.QuotaLabel(last.Label), 15); label.FontWeight = FontWeights.SemiBold; header.Children.Add(label);
            var value = InsightsUi.Text(L.F("{0:0.#}% wykorzystano", last.Used), 19, new SolidColorBrush(color));
            value.FontWeight = FontWeights.SemiBold; Grid.SetColumn(value, 1); header.Children.Add(value); content.Children.Add(header);
            content.Children.Add(InsightsUi.Text(L.F("Ostatni odczyt · {0:dd.MM HH:mm}", last.At.LocalDateTime), 11, InsightsUi.Muted));
            content.Children.Add(new HistoryChart(samples, start, now, color) { Height = 130, Margin = new Thickness(0, 8, 0, 0) });
            historyBody.Children.Add(InsightsUi.Card(content));
        }
        var help = InsightsUi.Text(L.T("Najedź na wykres, aby zobaczyć odczyt. Przerwy oznaczają reset lub brak danych."), 11, InsightsUi.Muted);
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
        AutomationProperties.SetName(this, rows.Count == 0 ? L.T("Brak odczytów") : L.F("{0}: historia wykorzystania od 0 do 100 procent", L.QuotaLabel(rows[0].Label)));
        MouseMove += (_, e) =>
        {
            var pos = e.GetPosition(this);
            var nearest = this.rows.Count == 0 || pos.X < LeftEdge || pos.X > RightEdge || pos.Y > BottomEdge ? null
                : this.rows.MinBy(s => Math.Abs(Position(s).X - pos.X));
            if (nearest is not null && Math.Abs(Position(nearest).X - pos.X) > 14) nearest = null;
            if (ReferenceEquals(nearest, hover)) return;
            hover = nearest;
            if (nearest is null) ToolTip = null;
            else WidgetTips.Set(this, L.F("{0:0.#}% wykorzystano", nearest.Used), nearest.At.LocalDateTime.ToString(L.TimestampPattern, L.Culture));
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
            var text = new FormattedText(value, L.Culture, FlowDirection.LeftToRight,
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
            Label(at.LocalDateTime.ToString((end - start).TotalDays > 2 ? L.IsPolish ? "dd.MM" : "d MMM" : "HH:mm", L.Culture),
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
