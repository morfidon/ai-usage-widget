using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Automation;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AIUsageWidget;

public sealed class MainWindow : Window
{
    static readonly Color Mint = Color.FromRgb(105, 220, 196);
    static readonly Color CodexAccent = Color.FromRgb(124, 184, 255);
    static readonly Color ClaudeAccent = Color.FromRgb(232, 175, 143);
    static readonly Color DevinAccent = Color.FromRgb(105, 220, 196);
    static readonly Color AirAccent = Color.FromRgb(244, 167, 205);
    static readonly Color AntigravityAccent = Color.FromRgb(146, 192, 255);
    static readonly Brush ForegroundBrush = Brush("#F0F2F6");
    static readonly Brush Muted = Brush("#ACB8C8");
    readonly Settings settings;
    readonly CodexProvider codex;
    readonly ClaudeProvider claude;
    readonly DevinProvider devin;
    readonly AirProvider air;
    readonly AntigravityProvider antigravity;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly StackPanel body = new();
    readonly ScrollViewer providerScroll = new();
    readonly Forms.NotifyIcon tray;
    readonly HashSet<string> notified = new();
    readonly UsageHistory history;
    readonly bool preview;
    InsightsWindow? insights;
    readonly SessionMonitor sessionMonitor;
    readonly TranscriptAnalytics analytics;
    SessionSnapshot sessions = SessionSnapshot.Empty;
    AnalyticsSnapshot analysis = AnalyticsSnapshot.Empty;
    bool activityBusy, analyticsBusy;
    DateTimeOffset lastActivity = DateTimeOffset.MinValue, lastAnalytics = DateTimeOffset.MinValue;
    readonly HealthMonitor healthMonitor = new();
    ServiceHealth codexHealth = ServiceHealth.Empty("Codex"), claudeHealth = ServiceHealth.Empty("Claude");
    bool healthBusy;
    DateTimeOffset lastHealth = DateTimeOffset.MinValue;
    string? trayKey;
    readonly Dictionary<string, DateTimeOffset> finishedUntil = new();
    sealed record CachedCard(ProviderState Source, bool Compact, bool Animate, string Resets, bool Stale,
        string Expired, ServiceHealth? Health, MascotState Mascot, LiveSession[] Sessions, string? SessionError,
        FrameworkElement Element, MascotView[] Views);
    readonly Dictionary<string, CachedCard> cards = new();
    ProviderState codexState = ProviderState.Empty("Codex"), claudeState = ProviderState.Empty("Claude");
    ProviderState devinState = ProviderState.Empty("Devin");
    ProviderState airState = ProviderState.Empty("JetBrains Air");
    ProviderState antigravityState = ProviderState.Empty("Antigravity");
    bool busy;
    DateTimeOffset lastFetch = DateTimeOffset.MinValue;
    DateTimeOffset lastPaint = DateTimeOffset.MinValue;
    string? bridgeFingerprint;
    Button refreshButton = null!, pinButton = null!, modeButton = null!;
    TextBlock footer = null!;
    TextBlock brandSubtitle = null!;

    public MainWindow(Settings settings, bool preview = false)
    {
        this.settings = settings;
        this.preview = preview;
        WidgetTheme.Apply(this);
        history = new(preview ? Path.Combine(Settings.DataDirectory, "visual-test-history.json") : null);
        sessionMonitor = new(settings); analytics = new(settings);
        settings.RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 30, 3600);
        codex = new(settings); claude = new(settings); devin = new(); air = new(); antigravity = new();
        Title = "AI Usage · Codex + Claude + Devin + Air + Antigravity";
        Width = 356; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = settings.Topmost; FontFamily = new FontFamily("Segoe UI");
        var outer = new Border { Margin = new Thickness(9), Padding = new Thickness(16), CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1), BorderBrush = Brush("#353B45"),
            Background = new LinearGradientBrush(Color.FromRgb(22, 29, 39), Color.FromRgb(18, 24, 33), 90),
            Effect = new DropShadowEffect { BlurRadius = 15, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black } };
        Content = outer;
        var layout = new StackPanel(); outer.Child = layout;
        layout.Children.Add(Header());
        providerScroll.Content = body; providerScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        providerScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        providerScroll.MaxHeight = Math.Max(120, SystemParameters.WorkArea.Height - 130);
        layout.Children.Add(providerScroll);
        layout.Children.Add(Footer());
        ContextMenu = BuildMenu();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        tray = new Forms.NotifyIcon { Icon = CreateIcon(), Text = Title, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.BeginInvoke(() => { if (IsVisible) Hide(); else Restore(); }); };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Pokaż widget", null, (_, _) => Dispatcher.BeginInvoke(Restore));
        menu.Items.Add("Odśwież limity", null, (_, _) => Dispatcher.BeginInvoke(() => _ = Refresh()));
        menu.Items.Add("Tryb kompaktowy / pełny", null, (_, _) => Dispatcher.BeginInvoke(ToggleCompact));
        menu.Items.Add("Historia i szczegóły", null, (_, _) => Dispatcher.BeginInvoke(OpenInsights));
        var trayProvider = new Forms.ToolStripMenuItem("Procent przy zegarze");
        foreach (string name in new[] { "Codex", "Claude" }.Where(settings.IsEnabled))
            trayProvider.DropDownItems.Add(name, null, (_, _) => Dispatcher.BeginInvoke(() => { settings.TrayProvider = name; SaveSettings(); Render(); }));
        menu.Items.Add(trayProvider);
        menu.Items.Add(new Forms.ToolStripSeparator());
        var startup = new Forms.ToolStripMenuItem("Uruchamiaj z Windows") { CheckOnClick = true, Checked = Startup.Enabled };
        startup.Click += (_, _) => { try { Startup.Set(startup.Checked); } catch { startup.Checked = Startup.Enabled; } };
        menu.Items.Add(startup);
        menu.Items.Add("Zakończ", null, (_, _) => Dispatcher.BeginInvoke(Close));
        menu.Opening += (_, _) => startup.Checked = Startup.Enabled;
        tray.ContextMenuStrip = menu;
        if (preview) SetPreviewData(); else LoadCache();
        Render();
        Loaded += async (_, _) =>
        {
            PlaceWindow();
            // Background launchers use SW_HIDE for their process; explicitly show the widget itself.
            ShowWindow(new WindowInteropHelper(this).Handle, 4);
            if (preview) return;
            heartbeat.Tick += Tick; heartbeat.Start();
            _ = RefreshActivity(); _ = RefreshAnalytics();
            _ = RefreshHealth();
            await Refresh();
        };
        LocationChanged += (_, _) => { if (IsLoaded) { settings.Left = Left; settings.Top = Top; } };
        Closing += (_, _) =>
        {
            heartbeat.Stop(); lifetime.Cancel();
            insights?.Close();
            SaveSettings(); tray.Visible = false; var icon = tray.Icon; tray.Dispose(); icon?.Dispose();
            healthMonitor.Dispose(); codex.Dispose(); claude.Dispose(); devin.Dispose(); air.Dispose(); antigravity.Dispose();
        };
    }
    static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    static TextBlock Text(string value, double size = 12, Brush? color = null, FontWeight? weight = null) => new()
    { Text = value, FontSize = size, Foreground = color ?? ForegroundBrush, FontWeight = weight ?? FontWeights.Normal };
    static void SetTip(FrameworkElement target, string title, string detail = "") => WidgetTips.Set(target, title, detail);
    static Brush QuotaColor(UsageWindow window, bool stale) => window.Expired || stale ? Muted
        : window.Remaining <= 10 ? Brush("#F18C99") : window.Remaining <= 20 ? Brush("#EAC06C") : ForegroundBrush;
    static string UpdatedText(ProviderState state) => state.Updated.HasValue
        ? state.Updated.Value.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss") : "Brak odczytu";
    static string QuotaDetail(UsageWindow window, ProviderState state) =>
        (window.Expired ? "Czekam na odczyt po resecie.\n" : "")
        + (string.IsNullOrEmpty(window.DetailText) ? "" : WidgetTips.Line(window.DetailText) + "\n")
        + $"Odczyt: {UpdatedText(state)}";
    static ResetPresentation Reset(UsageWindow window) => window.Renews
        ? ResetFormatter.Format(window.ResetAt, DateTimeOffset.Now, TimeZoneInfo.Local)
        : new("Bez resetu miesięcznego", "", "");
    Button Button(string label, string tooltip, Action action)
    {
        var button = new Button { Content = label, Width = 27, Height = 27,
            Foreground = Muted, Background = Brush("#282E38"), BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Margin = new Thickness(3, 0, 0, 0), FontSize = 13 };
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content); template.VisualTree = border; button.Template = template;
        SetTip(button, tooltip);
        button.MouseEnter += (_, _) => button.Background = Brush("#363E4B");
        button.MouseLeave += (_, _) => button.Background = Brush("#282E38");
        button.Click += (_, _) => action();
        return button;
    }
    FrameworkElement Header()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 16), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        var logo = new Canvas { Width = 30, Height = 30 }; Fish(logo, 14, 15, Mint, 1.0); brand.Children.Add(logo);
        var texts = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
        texts.Children.Add(Text("AI Usage", 15, weight: FontWeights.SemiBold));
        brandSubtitle = Text("LIMITY KONT AI", 8, Muted); texts.Children.Add(brandSubtitle);
        texts.VerticalAlignment = VerticalAlignment.Center; brand.Children.Add(texts); grid.Children.Add(brand);
        grid.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock or Canvas or Grid or StackPanel) { try { DragMove(); } catch { } SaveSettings(); } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(actions, 1);
        pinButton = Button("◆", "Zawsze na wierzchu", TogglePin);
        refreshButton = Button("↻", "Odśwież limity", () => _ = Refresh());
        modeButton = Button("▤", "Przełącz tryb kompaktowy", ToggleCompact);
        actions.Children.Add(pinButton); actions.Children.Add(Button("◷", "Historia limitów i szczegóły", OpenInsights));
        actions.Children.Add(refreshButton); actions.Children.Add(modeButton);
        actions.Children.Add(Button("−", "Ukryj przy zegarze · kliknij ikonę, aby przywrócić", Hide));
        grid.Children.Add(actions); return grid;
    }
    FrameworkElement Footer()
    {
        var grid = new Grid { Margin = new Thickness(0, 13, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer = Text("Łączenie z kontami…", 10, Muted); grid.Children.Add(footer);
        var more = Text("•••", 12, Muted); Grid.SetColumn(more, 1); more.Cursor = Cursors.Hand;
        SetTip(more, "Ustawienia", "Widok, zawsze na wierzchu i autostart Windows.");
        more.MouseLeftButtonDown += (_, _) => { ContextMenu = BuildMenu(); ContextMenu.IsOpen = true; };
        grid.Children.Add(more); return grid;
    }
    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Item(string title, Action action, bool check = false, bool value = false)
        {
            var i = new MenuItem { Header = title, IsCheckable = check, IsChecked = value };
            i.Click += (_, _) => action(); menu.Items.Add(i);
        }
        Item("Odśwież limity", () => _ = Refresh());
        Item("Tryb kompaktowy", ToggleCompact, true, settings.Compact);
        Item("Historia i szczegóły", OpenInsights);
        if (settings.IsEnabled("Codex")) Item("Procent przy zegarze · Codex", () => { settings.TrayProvider = "Codex"; SaveSettings(); Render(); });
        if (settings.IsEnabled("Claude")) Item("Procent przy zegarze · Claude", () => { settings.TrayProvider = "Claude"; SaveSettings(); Render(); });
        Item("Animacje postaci", () => { settings.AnimateMascots = !settings.AnimateMascots; SaveSettings(); Render(); }, true, settings.AnimateMascots);
        Item("Zawsze na wierzchu", TogglePin, true, settings.Topmost);
        Item("Uruchamiaj z Windows", () => { try { Startup.Set(!Startup.Enabled); } catch { System.Windows.MessageBox.Show("Nie można zmienić autostartu."); } }, true, Startup.Enabled);
        menu.Items.Add(new Separator());
        Item("Ustaw w prawym dolnym rogu", () => { settings.Left = null; settings.Top = null; PlaceWindow(); SaveSettings(); });
        Item("Claude · strona limitów", () => OpenUrl("https://claude.ai/settings/usage"));
        Item("Codex · strona limitów", () => OpenUrl("https://chatgpt.com/codex/settings/usage"));
        Item("Devin · strona limitów", () => OpenUrl("https://app.devin.ai/settings/usage"));
        Item("JetBrains Air · kredyty konta", () => OpenUrl("https://air.jetbrains.cloud"));
        Item("JetBrains Air · połącz konto", () => Process.Start(new ProcessStartInfo(
            Path.Combine(Settings.ProjectDirectory, "Connect JetBrains Air.cmd")) { UseShellExecute = true, WorkingDirectory = Settings.ProjectDirectory }));
        Item("Otwórz folder widgetu", () => Process.Start(new ProcessStartInfo(Settings.ProjectDirectory) { UseShellExecute = true }));
        menu.Items.Add(new Separator());
        Item("Ukryj przy zegarze", Hide); Item("Zakończ", Close);
        return menu;
    }
    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    void TogglePin() { settings.Topmost = !settings.Topmost; Topmost = settings.Topmost; if (insights is not null) insights.Topmost = Topmost; SaveSettings(); Render(); }
    void ToggleCompact() { settings.Compact = !settings.Compact; SaveSettings(); Render(); Dispatcher.BeginInvoke(ClampPosition, DispatcherPriority.Loaded); }
    void SaveSettings() { if (preview) return; try { settings.Save(); } catch { } }
    void PlaceWindow()
    {
        var work = SystemParameters.WorkArea;
        Left = settings.Left ?? work.Right - Width - 16;
        Top = settings.Top ?? work.Bottom - ActualHeight - 16;
        ClampPosition();
    }
    void ClampPosition()
    {
        // WorkArea is in WPF device-independent units, matching Left/Top at any DPI.
        var work = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - ActualWidth));
        Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - ActualHeight));
    }
    public void Restore() { Show(); ShowWindow(new WindowInteropHelper(this).Handle, 4); ClampPosition(); Activate(); }
    static bool Stale(ProviderState s) => !s.Connected || !s.Updated.HasValue || (DateTimeOffset.Now - s.Updated.Value).TotalMinutes > 10;
    void OpenInsights()
    {
        if (insights is not null) { insights.Show(); insights.Activate(); return; }
        insights = new InsightsWindow(history, !preview) { Owner = this, Topmost = Topmost };
        insights.UpdateDetails(analysis, sessions);
        insights.UpdateQuotas(codexState, claudeState);
        insights.Closed += (_, _) => insights = null;
        insights.Show();
    }
    void OpenProvider(string name, bool showSessions = false)
    {
        OpenInsights(); insights!.SelectProvider(name);
        if (showSessions) insights.SelectSessions();
    }
    FrameworkElement ProviderLink(StackPanel content, ProviderState state)
    {
        if (state.Name is not ("Codex" or "Claude")) return content;
        var button = new Button { Content = content, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, "Otwórz szczegóły " + state.Name);
        SetTip(button, "Otwórz przegląd konta");
        button.Click += (_, _) => OpenProvider(state.Name);
        return button;
    }
    internal void SetCompactForTest(bool compact) { settings.Compact = compact; Render(); }
    internal InsightsWindow InsightsForTest(int tab) { OpenInsights(); insights!.SelectTab(tab); return insights; }
    void SetPreviewData()
    {
        var now = DateTimeOffset.Now;
        codexState = new("Codex", "Pro", new() { new("codex:primary", "5 godzin", 38, now.AddHours(2).ToUnixTimeSeconds(), 300), new("codex:secondary", "Tydzień", 64, now.AddDays(3).ToUnixTimeSeconds(), 10080) }, now, "Połączono", true);
        claudeState = new("Claude", "Max", new() { new("five_hour", "5 godzin", 22, now.AddHours(4).ToUnixTimeSeconds(), 300), new("seven_day", "Tydzień", 48, now.AddDays(2).ToUnixTimeSeconds(), 10080) }, now, "Połączono", true);
        devinState = new("Devin", "Pro", new() { new("day", "Dzień", 16, now.AddHours(8).ToUnixTimeSeconds(), 1440) }, now, "Połączono", true);
        airState = new("JetBrains Air", "AI Pro", new() { new("month", "Miesiąc", 24, now.AddDays(12).ToUnixTimeSeconds(), 0, "26,6 / 35 kredytów AI") }, now, "Połączono", true);
        antigravityState = new("Antigravity", "Pro", new() { new("gemini", "5 godzin", 12, now.AddHours(3).ToUnixTimeSeconds(), 300, Group: "Gemini") }, now, "Połączono", true);
        sessions = new(new() { new("Codex", "x1", "Poprawa panelu", "O:/projekty/ai-usage", "working"), new("Codex", "x2", "Weryfikacja tłumaczeń", "O:/projekty/snake-game", "waiting", "Odpowiedź na pytanie"), new("Codex", "x3", "Nowa mapa", "O:/projekty/paper-world", "working"), new("Claude", "c1", "Przegląd interfejsu", "O:/projekty/ai-usage", "working") }, now);
        codexHealth = new("Codex", "none", "Brak zgłoszonych awarii", now, true); claudeHealth = new("Claude", "none", "Brak zgłoszonych awarii", now, true);
        history.Samples.Clear();
        foreach (string name in new[] { "Codex", "Claude" })
            for (int i = 0; i <= 288; i++)
            {
                var at = now.AddMinutes(-5 * (288 - i));
                history.Samples.Add(new(name, "five", "5 godzin", at, i % 60 * 1.25, at.AddMinutes((60 - i % 60) * 5).ToUnixTimeSeconds()));
                history.Samples.Add(new(name, "week", "Tydzień", at, 24 + i / 8.0, now.AddDays(3).ToUnixTimeSeconds()));
            }
        analysis = new(new() { new("Codex", "O:/projekty/ai-usage", "a", "codex", now, 120000, 890000, 0, 47000, false), new("Codex", "O:/projekty/snake-game", "b", "codex", now, 48000, 320000, 0, 19000, false), new("Claude", "O:/projekty/ai-usage", "c", "claude", now, 17000, 620000, 8000, 23000, false) },
            new() { new("Claude", "MCP", "context7", now), new("Claude", "Umiejętności", "find-docs", now), new("Claude", "Subagenci", "reviewer", now) }, now, false, 0);
    }
    async Task RefreshActivity()
    {
        if (activityBusy) return; activityBusy = true;
        try
        {
            var next = await sessionMonitor.Read(lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            foreach (var name in new[] { "Codex", "Claude" })
                if (next.ErrorFor(name) is null && sessions.Sessions.Any(s => s.Provider == name && s.State == "working")
                    && !next.Sessions.Any(s => s.Provider == name && s.State == "working")) finishedUntil[name] = DateTimeOffset.Now.AddSeconds(15);
            sessions = next; insights?.UpdateDetails(analysis, sessions); Render();
        }
        catch (OperationCanceledException) { }
        catch { sessions = new(new(), DateTimeOffset.Now, "Brak odczytu sesji", "Brak odczytu sesji"); Render(); }
        finally { lastActivity = DateTimeOffset.Now; activityBusy = false; }
    }
    async Task RefreshAnalytics()
    {
        if (analyticsBusy) return; analyticsBusy = true;
        try { var next = await Task.Run(() => analytics.Scan(lifetime.Token), lifetime.Token); lifetime.Token.ThrowIfCancellationRequested(); analysis = next; insights?.UpdateDetails(analysis, sessions); }
        catch (OperationCanceledException) { }
        catch { analysis = analysis with { Loading = false, Skipped = analysis.Skipped + 1 }; insights?.UpdateDetails(analysis, sessions); }
        finally { lastAnalytics = DateTimeOffset.Now; analyticsBusy = false; }
    }
    async Task RefreshHealth()
    {
        if (healthBusy) return; healthBusy = true;
        try
        {
            var a = settings.IsEnabled("Codex") ? healthMonitor.Read("Codex", lifetime.Token) : Task.FromResult(ServiceHealth.Empty("Codex"));
            var b = settings.IsEnabled("Claude") ? healthMonitor.Read("Claude", lifetime.Token) : Task.FromResult(ServiceHealth.Empty("Claude"));
            await Task.WhenAll(a, b); lifetime.Token.ThrowIfCancellationRequested(); codexHealth = await a; claudeHealth = await b; Render();
        }
        catch (OperationCanceledException) { }
        finally { healthBusy = false; lastHealth = DateTimeOffset.Now; }
    }
    void LinkHealth(FrameworkElement target, string name)
    {
        if (name is not ("Codex" or "Claude")) return;
        target.Cursor = Cursors.Hand;
        var health = name == "Codex" ? codexHealth : claudeHealth;
        SetTip(target, !health.Available ? "Status niedostępny" : health.Incident ? "Incydent usługi" : "Brak zgłoszonych awarii",
            health.Incident ? health.Detail : "");
        target.MouseLeftButtonUp += (_, e) => { OpenUrl((name == "Codex" ? codexHealth : claudeHealth).Url); e.Handled = true; };
    }
    void AddHealth(StackPanel panel, string name)
    {
        if (name is not ("Codex" or "Claude")) return;
        var health = name == "Codex" ? codexHealth : claudeHealth;
        if (health.Available && !health.Incident) return;
        var label = Text(!health.Available ? "○  Status usługi niedostępny" : health.Incident ? "●  " + (name == "Codex" ? "OpenAI" : "Claude") + " · incydent" : "●  Usługa działa", 9,
            !health.Available ? Muted : health.Incident ? Brush("#EAC06C") : Brush("#79C7AE"));
        label.Margin = new Thickness(0, 7, 0, 0); label.Cursor = Cursors.Hand;
        SetTip(label, name == "Codex" ? "Status usług OpenAI" : "Status usług Claude", health.Detail
            + (health.At == DateTimeOffset.MinValue ? "" : $"\nOdczyt: {health.At.LocalDateTime:HH:mm:ss}") + "\nKliknij, aby otworzyć stronę statusu.");
        label.MouseLeftButtonUp += (_, e) => { OpenUrl(health.Url); e.Handled = true; };
        panel.Children.Add(label);
    }
    void AddSessions(StackPanel panel, string providerName)
    {
        if (providerName is not ("Claude" or "Codex")) return;
        var active = sessions.Sessions.Where(s => s.Provider == providerName).ToList();
        if (active.Count == 0 && sessions.ErrorFor(providerName) is null) return;
        var block = new StackPanel { Margin = new Thickness(0, 7, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        string summary = active.Count == 0 ? "Sesje · brak odczytu" : string.Join(" · ", new[]
        {
            active.Count(s => s.State == "working") is int n && n > 0 ? $"▶ Pracuje: {n}" : "",
            active.Count(s => s.State == "waiting") is int w && w > 0 ? $"? Czeka: {w}" : "",
            active.Count(s => s.State == "idle") is int i && i > 0 ? $"○ Bezczynne: {i}" : ""
        }.Where(s => s.Length > 0));
        if (active.Count != 1) block.Children.Add(Text(summary, 10, active.Any(s => s.State == "waiting") ? Brush("#EAC06C") : Muted));
        // Active projects remain visible in compact mode too, without expanding the quota rows.
        foreach (var s in active.OrderByDescending(s => s.State == "waiting").ThenByDescending(s => s.State == "working").Take(settings.Compact ? 2 : 4))
        {
            var line = Text((s.State == "working" ? "▶ " : s.State == "waiting" ? "? " : "○ ") + Path.GetFileName(s.Directory.TrimEnd('\\', '/'))
                + (active.Count == 1 ? " · " + s.StateLabel.ToLowerInvariant() : ""), 10, s.State == "waiting" ? Brush("#EAC06C") : Muted);
            line.TextTrimming = TextTrimming.CharacterEllipsis; block.Children.Add(line);
        }
        if (active.Count > (settings.Compact ? 2 : 4)) block.Children.Add(Text("+ " + (active.Count - (settings.Compact ? 2 : 4)) + " więcej · szczegóły", 9, Muted));
        SetTip(block, "Otwórz listę sesji", sessions.ErrorFor(providerName) ?? "");
        block.MouseLeftButtonUp += (_, e) => { OpenProvider(providerName, true); e.Handled = true; };
        panel.Children.Add(block);
    }
    void AddMascot(StackPanel title, ProviderState provider)
    {
        if (provider.Name is not ("Codex" or "Claude")) return;
        bool finished = finishedUntil.TryGetValue(provider.Name, out var until) && until > DateTimeOffset.Now;
        var mascot = new MascotView(provider.Name, MascotView.Choose(provider, sessions, finished), settings.AnimateMascots)
        { Width = 44, Height = 35, Margin = new Thickness(0, -5, 6, -5), VerticalAlignment = VerticalAlignment.Center };
        mascot.ToolTip = null;
        title.Children.Insert(0, mascot);
    }
    void Render()
    {
        double scrollOffset = providerScroll.VerticalOffset;
        pinButton.Foreground = settings.Topmost ? new SolidColorBrush(Mint) : Muted;
        pinButton.Visibility = settings.Compact ? Visibility.Collapsed : Visibility.Visible;
        brandSubtitle.Visibility = settings.Compact ? Visibility.Collapsed : Visibility.Visible;
        modeButton.Foreground = settings.Compact ? new SolidColorBrush(Mint) : Muted;
        insights?.UpdateQuotas(codexState, claudeState);
        int index = 0;
        foreach (var (state, color) in new[] { (codexState, CodexAccent), (claudeState, ClaudeAccent),
            (devinState, DevinAccent), (airState, AirAccent), (antigravityState, AntigravityAccent) })
            if (settings.IsEnabled(state.Name)) RefreshCard(index++, state, color);
        var states = new[] { codexState, claudeState, devinState, airState, antigravityState }.Where(s => settings.IsEnabled(s.Name)).ToArray();
        var updates = states.Select(s => s.Updated).Where(d => d.HasValue).ToList();
        footer.FontSize = settings.Compact ? 9 : 10;
        footer.Text = busy ? "Odświeżanie…" : updates.Count == 0 ? "Łączenie z kontami…"
            : $"Odczyt {updates.Max()!.Value.LocalDateTime:HH:mm}" + (settings.Compact ? "" : " · automatyczne odświeżanie");
        SetTip(footer, "Aktualizacja danych", string.Join("\n", states.Select(s => $"{s.Name}: {UpdatedText(s)}")));
        tray.Text = TrayFormatter.Format(codexState, claudeState, settings);
        var selected = states.FirstOrDefault(s => s.Name == settings.TrayProvider && s.Name is "Codex" or "Claude")
            ?? states.FirstOrDefault(s => s.Name is "Codex" or "Claude");
        if (selected is null) {
            if (trayKey != "default") { var icon = tray.Icon; tray.Icon = CreateIcon(); icon?.Dispose(); trayKey = "default"; }
            lastPaint = DateTimeOffset.Now;
            if (scrollOffset > 0) Dispatcher.BeginInvoke(() => providerScroll.ScrollToVerticalOffset(scrollOffset), DispatcherPriority.Loaded);
            return;
        }
        var shared = selected.Windows.Where(w => !w.Expired && (selected.Name != "Codex" || w.Id.StartsWith("codex:"))).ToList();
        var quota = shared.OrderBy(w => w.Remaining).FirstOrDefault();
        string value = Stale(selected) || quota is null ? "?" : $"{quota.Remaining:0}";
        string key = selected.Name + ":" + value;
        if (key != trayKey)
        {
            var previous = tray.Icon;
            tray.Icon = CreatePercentIcon(value, selected.Name == "Claude" ? ClaudeAccent : CodexAccent);
            previous?.Dispose(); trayKey = key;
        }
        lastPaint = DateTimeOffset.Now;
        if (scrollOffset > 0) Dispatcher.BeginInvoke(() => providerScroll.ScrollToVerticalOffset(scrollOffset), DispatcherPriority.Loaded);
    }
    void RefreshCard(int index, ProviderState state, Color color)
    {
        string resets = string.Join("\n", state.Windows.Select(w => w.ResetText));
        bool stale = Stale(state);
        string expired = string.Concat(state.Windows.Select(w => w.Expired ? '1' : '0'));
        ServiceHealth? health = state.Name == "Codex" ? codexHealth : state.Name == "Claude" ? claudeHealth : null;
        var rows = sessions.Sessions.Where(s => s.Provider == state.Name).ToArray();
        string? error = state.Name is "Codex" or "Claude" ? sessions.ErrorFor(state.Name) : null;
        bool finished = finishedUntil.TryGetValue(state.Name, out var until) && until > DateTimeOffset.Now;
        var mascot = state.Name is "Codex" or "Claude" ? MascotView.Choose(state, sessions, finished) : MascotState.Sleeping;
        if (cards.TryGetValue(state.Name, out var old) && ReferenceEquals(old.Source, state)
            && old.Compact == settings.Compact && old.Animate == settings.AnimateMascots && old.Resets == resets
            && old.Stale == stale && old.Expired == expired && old.Health == health && old.Mascot == mascot
            && old.SessionError == error && old.Sessions.SequenceEqual(rows))
        {
            // Keep the existing animation's reset at each logical refresh, without rebuilding its card.
            foreach (var view in old.Views) view.ResetPhase();
            return;
        }
        var element = Card(state, color);
        var views = FindMascots(element).ToArray();
        cards[state.Name] = new(state, settings.Compact, settings.AnimateMascots, resets, stale, expired, health, mascot, rows, error, element, views);
        if (body.Children.Count <= index) body.Children.Add(element);
        else { body.Children.RemoveAt(index); body.Children.Insert(index, element); }
    }
    static IEnumerable<MascotView> FindMascots(DependencyObject parent)
    {
        if (parent is MascotView view) yield return view;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in FindMascots(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    FrameworkElement Card(ProviderState state, Color color)
    {
        if (settings.Compact) return CompactCard(state, color);
        var panel = new StackPanel();
        var card = new Border { CornerRadius = new CornerRadius(11), Background = Brush("#1D2632"),
            Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 7), Child = panel };
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Text(state.Name, 12, weight: FontWeights.SemiBold));
        AddMascot(title, state);
        if (!string.IsNullOrEmpty(state.Plan)) title.Children.Add(Text("  " + state.Plan.ToUpperInvariant(), 8, Muted));
        header.Children.Add(ProviderLink(title, state));
        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        bool stale = Stale(state);
        var dot = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = stale ? Brush("#E5B66A") : new SolidColorBrush(color), Margin = new Thickness(0, 0, 5, 0) };
        status.Children.Add(dot); if (stale) status.Children.Add(Text("Oczekiwanie", 9, Muted));
        LinkHealth(status, state.Name);
        Grid.SetColumn(status, 1); header.Children.Add(status); panel.Children.Add(header);
        AddSessions(panel, state.Name);
        AddHealth(panel, state.Name);
        var windows = state.Windows;
        if (windows.Count == 0)
        {
            var waiting = Text(state.Status, 10, Muted); waiting.TextWrapping = TextWrapping.Wrap; waiting.Margin = new Thickness(0, 14, 0, 3);
            panel.Children.Add(waiting);
            if (state.Name == "Claude")
            {
                var hint = Text("Dane pojawią się po odpowiedzi w Claude Code.", 9, Muted); hint.TextWrapping = TextWrapping.Wrap;
                panel.Children.Add(hint);
            }
            return card;
        }
        string? currentGroup = null;
        foreach (var window in windows)
        {
            AddGroupHeading(panel, window, ref currentGroup, color);
            var row = new StackPanel { Margin = new Thickness(0, settings.Compact ? 9 : 13, 0, 0) };
            var info = new Grid(); info.ColumnDefinitions.Add(new ColumnDefinition()); info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = Text(window.Label + (window.BalanceText is null ? "" : "\n" + window.BalanceText), settings.Compact ? 11 : 12, ForegroundBrush, FontWeights.SemiBold);
            label.TextTrimming = TextTrimming.CharacterEllipsis; label.Margin = new Thickness(0, 0, 8, 0);
            label.VerticalAlignment = VerticalAlignment.Center; info.Children.Add(label);
            var remaining = new StackPanel { Orientation = Orientation.Horizontal };
            var percent = Text(window.Expired ? "—" : $"{window.Remaining:0}%", settings.Compact ? 18 : 24,
                QuotaColor(window, stale), FontWeights.SemiBold);
            remaining.Children.Add(percent);
            var remainingLabel = Text("pozostało", settings.Compact ? 10 : 11, Muted);
            remainingLabel.VerticalAlignment = VerticalAlignment.Bottom; remainingLabel.Margin = new Thickness(5, 0, 0, 4);
            remaining.Children.Add(remainingLabel);
            Grid.SetColumn(remaining, 1); info.Children.Add(remaining); row.Children.Add(info);

            row.Children.Add(RemainingBar(window, state, GroupColor(window, color)));
            row.Children.Add(ResetBadge(window, GroupColor(window, color)));
            SetTip(row, "Źródło danych", QuotaDetail(window, state));
            panel.Children.Add(row);
        }
        if (!settings.Compact && (state.Error is not null || stale))
        {
            var source = Text(state.Error ?? "Dane z ostatniego odczytu · czekam na aktualizację", 10, Muted);
            source.Margin = new Thickness(0, 12, 0, 0); source.TextWrapping = TextWrapping.Wrap; panel.Children.Add(source);
        }
        return card;
    }
    FrameworkElement CompactCard(ProviderState state, Color color)
    {
        bool stale = Stale(state);
        var panel = new StackPanel();
        var card = new Border { Background = Brush("#1D2632"), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 7), Child = panel };
        var header = new Grid { Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        var providerName = Text(state.Name, 12, new SolidColorBrush(color), FontWeights.SemiBold);
        brand.Children.Add(providerName);
        AddMascot(brand, state);
        var statusTarget = new Border { Width = 21, Background = Brushes.Transparent };
        statusTarget.Child = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5,
            Fill = stale ? Brush("#EAC06C") : new SolidColorBrush(Mint), VerticalAlignment = VerticalAlignment.Center };
        LinkHealth(statusTarget, state.Name);
        var providerHeading = new StackPanel { Orientation = Orientation.Horizontal };
        providerHeading.Children.Add(ProviderLink(brand, state)); providerHeading.Children.Add(statusTarget);
        header.Children.Add(providerHeading);
        var remainingHeading = Text("Pozostało", 10, Muted); remainingHeading.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remainingHeading, 1); header.Children.Add(remainingHeading);
        if (state.Name is not ("Codex" or "Claude")) SetTip(header, state.Plan, state.Status);
        panel.Children.Add(header);
        AddSessions(panel, state.Name);
        AddHealth(panel, state.Name);
        if (state.Windows.Count == 0)
        {
            var waiting = Text(state.Status, 11, Muted); waiting.TextWrapping = TextWrapping.Wrap;
            waiting.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(waiting);
        }
        string? currentGroup = null;
        foreach (var window in state.Windows)
        {
            AddGroupHeading(panel, window, ref currentGroup, color);
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Background = Brushes.Transparent };
            var main = new Grid(); main.ColumnDefinitions.Add(new ColumnDefinition());
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var label = Text(window.Label, 11, ForegroundBrush); label.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(label);
            if (window.BalanceText is not null)
            {
                var balance = Text(window.BalanceText, 9, Muted); balance.Margin = new Thickness(0, 2, 0, 0);
                balance.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(balance);
            }
            main.Children.Add(labels);
            var value = Text(window.Expired ? "—" : $"{window.Remaining:0}%", 22, QuotaColor(window, stale), FontWeights.SemiBold);
            Grid.SetColumn(value, 1); main.Children.Add(value); row.Children.Add(main);
            SetTip(row, "Źródło danych", QuotaDetail(window, state));
            row.Children.Add(RemainingBar(window, state, GroupColor(window, color)));
            var reset = Reset(window);
            var resetLine = new Grid { Margin = new Thickness(0, 2, 0, 0), Background = Brushes.Transparent };
            resetLine.ColumnDefinitions.Add(new ColumnDefinition()); resetLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var countdown = Text(reset.Countdown, 10, Muted); countdown.Margin = new Thickness(0, 0, 5, 0);
            countdown.TextWrapping = TextWrapping.Wrap; resetLine.Children.Add(countdown);
            var calendar = Text(reset.ShortCalendar, 10, Muted); Grid.SetColumn(calendar, 1); resetLine.Children.Add(calendar);
            SetTip(resetLine, window.ResetText);
            row.Children.Add(resetLine); panel.Children.Add(row);
        }
        if (state.Windows.Count > 0 && stale)
        {
            var warning = Text("Dane nieaktualne", 10, Brush("#EAC06C")); warning.Margin = new Thickness(0, 7, 0, 0);
            SetTip(warning, "Czekam na aktualny odczyt", state.Error ?? state.Status);
            panel.Children.Add(warning);
        }
        else if (state.Error is not null)
        {
            var warning = Text(state.Error, 10, Muted); warning.TextWrapping = TextWrapping.Wrap;
            warning.Margin = new Thickness(0, 7, 0, 0); panel.Children.Add(warning);
        }
        return card;
    }
    static Color GroupColor(UsageWindow window, Color color) => window.Group == "Claude / GPT" ? ClaudeAccent : color;
    static void AddGroupHeading(StackPanel panel, UsageWindow window, ref string? currentGroup, Color color)
    {
        if (window.Group is null || window.Group == currentGroup) return;
        bool separator = currentGroup is not null;
        currentGroup = window.Group;
        var heading = Text(window.Group, 11, new SolidColorBrush(GroupColor(window, color)), FontWeights.SemiBold);
        SetTip(heading, window.Group, window.DetailText ?? "Wspólna pula modeli");
        panel.Children.Add(new Border { Child = heading, Margin = new Thickness(0, separator ? 12 : 10, 0, 0),
            Padding = new Thickness(0, separator ? 10 : 0, 0, 0), BorderThickness = new Thickness(0, separator ? 1 : 0, 0, 0), BorderBrush = Brush("#3A4351") });
    }
    FrameworkElement RemainingBar(UsageWindow window, ProviderState state, Color color)
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = window.Expired ? 0 : window.Remaining,
            Height = 6, Margin = new Thickness(0, 4, 0, 5), BorderThickness = new Thickness(0),
            Background = Brush("#3A4351"), Opacity = Stale(state) || window.Expired ? 0.45 : 1,
            Foreground = window.Remaining <= 20 ? QuotaColor(window, false) : new SolidColorBrush(color) };
        var template = new ControlTemplate(typeof(ProgressBar));
        var rail = new FrameworkElementFactory(typeof(Border));
        rail.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        rail.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var track = new FrameworkElementFactory(typeof(Grid), "PART_Track");
        var fill = new FrameworkElementFactory(typeof(Border), "PART_Indicator");
        fill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        fill.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        track.AppendChild(fill); rail.AppendChild(track); template.VisualTree = rail; bar.Template = template;
        AutomationProperties.SetName(bar, $"{state.Name} · {window.Label} · pozostały limit");
        return bar;
    }
    FrameworkElement ResetBadge(UsageWindow window, Color color)
    {
        var reset = Reset(window);
        var badge = new Border { Background = Brush("#1C2129"), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(settings.Compact ? 7 : 9, settings.Compact ? 5 : 8, settings.Compact ? 7 : 9, settings.Compact ? 5 : 8),
            Margin = new Thickness(0, settings.Compact ? 3 : 9, 0, 0) };
        if (settings.Compact) SetTip(badge, window.ResetText);
        if (settings.Compact)
        {
            var line = new Grid(); line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var countdown = Text(reset.Countdown, 10, ForegroundBrush);
            countdown.TextWrapping = TextWrapping.Wrap; countdown.Margin = new Thickness(0, 0, 6, 0); line.Children.Add(countdown);
            var calendar = Text(reset.ShortCalendar, 10, Muted); calendar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(calendar, 1); line.Children.Add(calendar); badge.Child = line;
        }
        else
        {
            var line = new Grid(); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Text("↻", 20, new SolidColorBrush(color)); icon.Margin = new Thickness(0, 0, 9, 0);
            icon.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(icon);
            var words = new StackPanel(); Grid.SetColumn(words, 1);
            var countdown = Text(reset.Countdown, 12, ForegroundBrush, FontWeights.SemiBold);
            countdown.TextWrapping = TextWrapping.Wrap; words.Children.Add(countdown);
            if (reset.Calendar.Length > 0)
            {
                var calendar = Text(reset.Calendar, 11, Muted); calendar.Margin = new Thickness(0, 3, 0, 0);
                calendar.TextWrapping = TextWrapping.Wrap; words.Children.Add(calendar);
            }
            line.Children.Add(words); badge.Child = line;
        }
        return badge;
    }
    static void Fish(Canvas canvas, double x, double y, Color color, double scale)
    {
        var brush = new SolidColorBrush(color);
        var tail = new System.Windows.Shapes.Polygon { Points = new PointCollection { new(x - 2 * scale, y), new(x - 12 * scale, y - 6 * scale), new(x - 12 * scale, y + 6 * scale) }, Fill = brush };
        canvas.Children.Add(tail);
        var fin = new System.Windows.Shapes.Polygon { Points = new PointCollection { new(x - 3 * scale, y - 3 * scale), new(x - 1 * scale, y - 10 * scale), new(x + 6 * scale, y - 4 * scale) }, Fill = brush };
        canvas.Children.Add(fin);
        var fish = new System.Windows.Shapes.Ellipse { Width = 19 * scale, Height = 13 * scale, Fill = brush };
        Canvas.SetLeft(fish, x - 6 * scale); Canvas.SetTop(fish, y - 6.5 * scale); canvas.Children.Add(fish);
        var eye = new System.Windows.Shapes.Ellipse { Width = 2.5 * scale, Height = 2.5 * scale, Fill = Brush("#18212B") };
        Canvas.SetLeft(eye, x + 6 * scale); Canvas.SetTop(eye, y - 2.5 * scale); canvas.Children.Add(eye);
    }
    async void Tick(object? sender, EventArgs e)
    {
        if (!activityBusy && DateTimeOffset.Now - lastActivity >= TimeSpan.FromSeconds(9)) _ = RefreshActivity();
        if (!analyticsBusy && DateTimeOffset.Now - lastAnalytics >= TimeSpan.FromSeconds(analysis.Loading ? 3 : 60)) _ = RefreshAnalytics();
        if (!healthBusy && DateTimeOffset.Now - lastHealth >= TimeSpan.FromMinutes(3)) _ = RefreshHealth();
        var bridge = settings.IsEnabled("Claude") ? claude.Bridge() : null;
        if (bridge is not null)
        {
            string fingerprint = JsonSerializer.Serialize(bridge);
            if (fingerprint != bridgeFingerprint)
            {
                bridgeFingerprint = fingerprint;
                if (!claudeState.Updated.HasValue || bridge.Updated >= claudeState.Updated)
                { claudeState = bridge; SaveCache(); Render(); Alert(bridge); }
            }
        }
        int interval = codexState.Connected ? settings.RefreshSeconds : Math.Min(60, settings.RefreshSeconds);
        if (!busy && (DateTimeOffset.Now - lastFetch).TotalSeconds >= interval) await Refresh();
        else if ((DateTimeOffset.Now - lastPaint).TotalSeconds >= 30) Render();
    }
    async Task Refresh()
    {
        if (busy || lifetime.IsCancellationRequested) return;
        busy = true; refreshButton.IsEnabled = false; Render();
        try
        {
            async Task Codex()
            {
                if (!settings.IsEnabled("Codex")) return;
                var state = await codex.Fetch(lifetime.Token);
                codexState = Merge(codexState, state); Alert(codexState); Render();
            }
            async Task Claude()
            {
                if (!settings.IsEnabled("Claude")) return;
                var state = await claude.Fetch(lifetime.Token);
                if (state is not null && (!claudeState.Updated.HasValue || !state.Updated.HasValue || state.Updated >= claudeState.Updated))
                    claudeState = Merge(claudeState, state);
                Alert(claudeState); Render();
            }
            async Task Devin()
            {
                if (!settings.IsEnabled("Devin")) return;
                devinState = Merge(devinState, await devin.Fetch(lifetime.Token)); Alert(devinState); Render();
            }
            async Task Air()
            {
                if (!settings.IsEnabled("JetBrains Air")) return;
                airState = Merge(airState, await air.Fetch(lifetime.Token)); Alert(airState); Render();
            }
            async Task Antigravity()
            {
                if (!settings.IsEnabled("Antigravity")) return;
                antigravityState = Merge(antigravityState, await antigravity.Fetch(lifetime.Token)); Alert(antigravityState); Render();
            }
            await Task.WhenAll(Codex(), Claude(), Antigravity(), Devin(), Air()); SaveCache();
        }
        catch (OperationCanceledException) { }
        finally { busy = false; lastFetch = DateTimeOffset.Now; if (!lifetime.IsCancellationRequested) { refreshButton.IsEnabled = true; Render(); } }
    }
    static ProviderState Merge(ProviderState old, ProviderState next) => !next.Connected && next.Windows.Count == 0 && old.Windows.Count > 0
        ? next with { Windows = old.Windows, Updated = old.Updated, Plan = old.Plan } : next;
    void Alert(ProviderState state)
    {
        if (Stale(state)) return;
        foreach (var window in state.Windows.Where(w => !w.Expired))
        {
            string key = state.Name + ":" + window.Id + ":" + window.ResetAt;
            if (window.Remaining > 20) notified.Remove(key);
            if (window.Remaining <= 10 && notified.Add(key))
                tray.ShowBalloonTip(5000, $"{state.Name} · niski limit", $"Pozostało {window.Remaining:0}% ({(window.Group is null ? "" : window.Group + " · ")}{window.Label}). {window.ResetText}", Forms.ToolTipIcon.Warning);
        }
    }
    string CachePath => Path.Combine(Settings.DataDirectory, "usage-cache.json");
    void LoadCache()
    {
        try
        {
            var states = JsonSerializer.Deserialize<List<ProviderState>>(File.ReadAllText(CachePath));
            foreach (var s in states ?? new())
                if (s.Name == "Codex") codexState = s with { Connected = false, Status = "Zapisany odczyt · łączenie…" };
                else if (s.Name == "Claude") claudeState = s with { Connected = false, Status = "Zapisany odczyt · łączenie…" };
                else if (s.Name == "Devin") devinState = s with { Connected = false, Status = "Zapisany odczyt · łączenie…" };
                else if (s.Name == "JetBrains Air") airState = s with { Connected = false, Status = "Zapisany odczyt · łączenie…" };
                else if (s.Name == "Antigravity") antigravityState = s with { Windows = s.Windows.Where(w => w.Group is not null).ToList(), Connected = false, Status = "Zapisany odczyt · łączenie…" };
        }
        catch { }
    }
    void SaveCache()
    {
        bool changed = (settings.IsEnabled("Codex") && history.Record(codexState, DateTimeOffset.Now))
            | (settings.IsEnabled("Claude") && history.Record(claudeState, DateTimeOffset.Now));
        if (changed) insights?.RefreshHistory();
        try
        {
            File.WriteAllText(CachePath + ".tmp", JsonSerializer.Serialize(new[] { codexState, claudeState, devinState, airState, antigravityState }.Where(s => settings.IsEnabled(s.Name))));
            File.Move(CachePath + ".tmp", CachePath, true);
        }
        catch { }
    }
    static Drawing.Icon CreateIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32); using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var dark = new Drawing.SolidBrush(Drawing.Color.FromArgb(27, 33, 44));
        using var mint = new Drawing.SolidBrush(Drawing.Color.FromArgb(105, 220, 196));
        graphics.FillEllipse(dark, 0, 0, 32, 32);
        graphics.FillPolygon(mint, new[] { new Drawing.Point(12, 16), new Drawing.Point(5, 10), new Drawing.Point(5, 22) });
        graphics.FillEllipse(mint, 10, 10, 17, 13); graphics.FillEllipse(dark, 21, 13, 3, 3);
        var handle = bitmap.GetHicon();
        try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }
    static Drawing.Icon CreatePercentIcon(string value, Color color)
    {
        using var bitmap = new Drawing.Bitmap(32, 32); using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(color.R, color.G, color.B));
        using var font = new Drawing.Font("Segoe UI", value.Length > 2 ? 12 : 15, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        using var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
        graphics.DrawString(value, font, brush, new Drawing.RectangleF(0, 0, 32, 29), format);
        graphics.FillRectangle(brush, 10, 29, 12, 2);
        var handle = bitmap.GetHicon();
        try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr handle, int command);
}
