using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsageWidget;

public static class VisualVerification
{
    public static void Capture(FrameworkElement window, string name)
    {
        foreach (var view in Descendants(window).OfType<MascotView>())
        {
            ((System.Windows.Threading.DispatcherTimer)typeof(MascotView).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!).Stop();
            typeof(MascotView).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, 1.75);
            view.InvalidateVisual();
            view.InvalidateVisual();
        }
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 1.5), (int)Math.Ceiling(window.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Settings.DataDirectory, name + ".png")); encoder.Save(output);
    }
    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var main = new MainWindow(new Settings { Language = "pl", Compact = true, Topmost = false, Left = 20, Top = 20 }, true);
        int result = 0;
        main.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(350);
                var texts = Descendants(main).OfType<TextBlock>().Select(t => t.Text).ToList();
                if (!texts.Any(t => t.Contains("Pracuje: 2")) || !texts.Any(t => t.Contains("Czeka: 1"))) throw new Exception("Compact sessions are missing");
                Capture(main, "visual-compact");
                var historyButton = Descendants(main).OfType<Button>().First(b => System.Windows.Automation.AutomationProperties.GetHelpText(b) == "Historia limitów i szczegóły");
                historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Clicking a provider must scope every tab, including sessions and token analysis.
                foreach (var providerName in new[] { "Claude", "Codex" })
                {
                    var entry = Descendants(main).OfType<Button>().First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Otwórz szczegóły " + providerName);
                    entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
                    var scoped = Application.Current.Windows.OfType<InsightsWindow>().Single();
                    var tabControl = Descendants(scoped).OfType<TabControl>().Single();
                    if (scoped.SelectedProvider != providerName || tabControl.SelectedIndex != 0) throw new Exception("Provider click did not open its overview");
                    Capture(scoped, "visual-overview-" + providerName.ToLowerInvariant());
                    scoped.SelectSessions(); await Task.Delay(80);
                    var sessionTexts = Descendants(scoped).OfType<TextBlock>().Select(t => t.Text).ToList();
                    if (sessionTexts.Contains(providerName == "Claude" ? "Weryfikacja tłumaczeń" : "Przegląd interfejsu")) throw new Exception("Sessions leaked across providers");
                    scoped.SelectTab(2); await Task.Delay(80);
                    var modelTexts = Descendants(scoped).OfType<TextBlock>().Select(t => t.Text).ToList();
                    if (!modelTexts.Contains(providerName.ToLowerInvariant()) || modelTexts.Contains(providerName == "Claude" ? "codex" : "claude")) throw new Exception("Models leaked across providers");
                    if (providerName == "Claude") Capture(scoped, "visual-models-claude");
                }
                var scopedWindow = Application.Current.Windows.OfType<InsightsWindow>().Single();
                var sampleAnalysis = (AnalyticsSnapshot)typeof(MainWindow).GetField("analysis", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
                var sampleSessions = (SessionSnapshot)typeof(MainWindow).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
                var oldEntry = sampleAnalysis.Entries.First() with { At = DateTimeOffset.Now.AddDays(-2), Model = "older-model" };
                scopedWindow.UpdateDetails(sampleAnalysis with { Entries = sampleAnalysis.Entries.Append(oldEntry).ToList() }, sampleSessions);
                var periodChoice = Descendants(scopedWindow).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Okres statystyk");
                periodChoice.SelectedIndex = 1; await Task.Delay(60);
                if (!Descendants(scopedWindow).OfType<TextBlock>().Any(t => t.Text == "older-model")) throw new Exception("Seven-day period did not include older model");
                periodChoice.SelectedIndex = 0; await Task.Delay(60);
                if (Descendants(scopedWindow).OfType<TextBlock>().Any(t => t.Text == "older-model")) throw new Exception("24-hour period leaked older model");
                scopedWindow.UpdateDetails(sampleAnalysis, sampleSessions);
                main.SetCompactForTest(false); await Task.Delay(200); Capture(main, "visual-full");
                var scroll = Descendants(main).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
                if (scroll.ScrollableHeight > 120)
                {
                    scroll.ScrollToVerticalOffset(120); await Task.Delay(80);
                    main.SetCompactForTest(false); await Task.Delay(80);
                    if (Math.Abs(scroll.VerticalOffset - 120) > 1) throw new Exception("Session repaint resets scroll position");
                }
                var names = new[] { "history", "projects", "diagnostics", "sessions" };
                for (int i = 0; i < names.Length; i++)
                {
                    var details = main.InsightsForTest(i); await Task.Delay(180); Capture(details, "visual-" + names[i]);
                }
                var projects = main.InsightsForTest(1);
                await Task.Delay(80);
                var expander = Descendants(projects).OfType<Expander>().First();
                expander.ApplyTemplate();
                var toggle = (ToggleButton)expander.Template.FindName("HeaderToggle", expander);
                toggle.IsChecked = true; await Task.Delay(60);
                if (!expander.IsExpanded) throw new Exception("Project details toggle failed");
                var analysisField = typeof(MainWindow).GetField("analysis", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var currentAnalysis = (AnalyticsSnapshot)analysisField.GetValue(main)!;
                projects.UpdateDetails(currentAnalysis with { At = DateTimeOffset.Now }, SessionSnapshot.Empty);
                projects.UpdateLayout();
                if (!Descendants(projects).OfType<Expander>().First().IsExpanded) throw new Exception("Refresh collapsed project details");
                Capture(projects, "visual-project-composition");
                projects.Width = 620; projects.Height = 440; await Task.Delay(80); Capture(projects, "visual-projects-narrow");
                var drillRows = Enumerable.Range(1, 20).Select(i => new TokenEntry("Codex", "O:/projects/deep-project", "test-" + i,
                    i % 2 == 0 ? "model-A" : "model-B", DateTimeOffset.Now, i * 100, i * 1000, 0, i * 10, false)).ToList();
                drillRows.Add(new("Codex", "O:/projects/deep-project", "test-20", "model-C", DateTimeOffset.Now, 100, 0, 0, 0, false));
                drillRows.Add(new("Claude", "O:/projects/deep-project", "test-20", "foreign-model", DateTimeOffset.Now, 9000000, 0, 0, 0, false));
                projects.UpdateDetails(currentAnalysis with { Entries = drillRows }, SessionSnapshot.Empty);
                projects.Width = 820; projects.Height = 760; await Task.Delay(80); Capture(projects, "visual-projects-ranking-entry");
                var projectLink = Descendants(projects).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Otwórz projekt O:/projects/deep-project");
                projectLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
                var rankTexts = Descendants(projects).OfType<TextBlock>().Select(t => t.Text).ToList();
                if (!rankTexts.Contains("Sesja test-20 ↗") || rankTexts.Contains("Sesja test-1 ↗") || rankTexts.Contains("foreign-model")) throw new Exception("Project session ranking or pagination is wrong");
                var firstSession = Descendants(projects).OfType<Expander>().First(); firstSession.IsExpanded = true;
                await Task.Delay(60);
                if (!Descendants(projects).OfType<TextBlock>().Any(t => t.Text == "model-C")) throw new Exception("Mixed-model session breakdown missing");
                Capture(projects, "visual-project-sessions");
                var nextPage = Descendants(projects).OfType<Button>().First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Następna strona sesji");
                nextPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(60);
                if (!Descendants(projects).OfType<TextBlock>().Any(t => t.Text == "Sesja test-1 ↗")) throw new Exception("Next session page is inaccessible");
                var modelRanking = Descendants(projects).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Ranking modeli projektu");
                modelRanking.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(60);
                rankTexts = Descendants(projects).OfType<TextBlock>().Select(t => t.Text).ToList();
                if (!rankTexts.Contains("model-A") || rankTexts.IndexOf("model-A") > rankTexts.IndexOf("model-B") || rankTexts.Contains("foreign-model")) throw new Exception("Project model ranking is not scoped or sorted");
                Capture(projects, "visual-project-models");
                projects.Width = 620; projects.Height = 440; await Task.Delay(60); Capture(projects, "visual-project-models-narrow");
                var goBack = Descendants(projects).OfType<Button>().Single(b => b.Content as string == "← Projekty");
                goBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); projects.UpdateDetails(currentAnalysis, sampleSessions);
                main.InsightsForTest(0); await Task.Delay(80); Capture(projects, "visual-history-narrow");
                var scratch = new UsageHistory(Path.Combine(Settings.DataDirectory, "visual-empty-" + Guid.NewGuid() + ".json"));
                string? openedThread = null, openedClaudeThread = null; bool failOpen = false;
                const string targetThread = "019ef3c1-1234-7123-8123-0123456789ab";
                var navigation = new InsightsWindow(scratch, false,
                    id => { openedThread = id; return failOpen ? "Nie udało się otworzyć Codex. Spróbuj ponownie." : null; },
                    id => { openedClaudeThread = id; return failOpen ? "Nie udało się otworzyć Claude Desktop. Spróbuj ponownie." : null; });
                var navigationRows = new[] { "Codex", "Claude" }.Select(name => new TokenEntry(name, "test-project", targetThread, "model", DateTimeOffset.Now, 20, 100, 0, 10, false)).ToList();
                navigation.UpdateDetails(currentAnalysis with { Entries = navigationRows }, SessionSnapshot.Empty);
                navigation.SelectTab(1); navigation.Show(); await Task.Delay(80);
                Button FindProjectLink() => Descendants(navigation).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Otwórz projekt test-project");
                FindProjectLink().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(60);
                var sessionLink = Descendants(navigation).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Otwórz w Codex:"));
                sessionLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int attempt = 0; attempt < 40 && !sessionLink.IsEnabled; attempt++) await Task.Delay(25);
                if (openedThread != targetThread || !sessionLink.IsEnabled) throw new Exception("Session click did not open the exact Codex thread");
                Capture(navigation, "visual-project-open-session");
                failOpen = true; sessionLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int attempt = 0; attempt < 40 && !sessionLink.IsEnabled; attempt++) await Task.Delay(25);
                if (!Descendants(navigation).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.StartsWith("Nie udało się otworzyć Codex"))) throw new Exception("Navigation failure is invisible");
                navigation.SelectProvider("Claude", false); await Task.Delay(60);
                FindProjectLink().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(60);
                if (Descendants(navigation).OfType<Button>().Any(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Otwórz w Codex:"))) throw new Exception("Claude sessions must not open in Codex");
                openedThread = null; failOpen = false;
                var claudeLink = Descendants(navigation).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Otwórz w Claude:"));
                claudeLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int attempt = 0; attempt < 40 && !claudeLink.IsEnabled; attempt++) await Task.Delay(25);
                if (openedClaudeThread != targetThread || openedThread is not null || !claudeLink.IsEnabled) throw new Exception("Claude click did not target the exact Claude conversation");
                Capture(navigation, "visual-project-open-claude-session");
                failOpen = true; claudeLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int attempt = 0; attempt < 40 && !claudeLink.IsEnabled; attempt++) await Task.Delay(25);
                if (!Descendants(navigation).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.StartsWith("Nie udało się otworzyć Claude Desktop"))) throw new Exception("Claude navigation failure is invisible");
                navigation.Close();
                var edge = new InsightsWindow(scratch); edge.SelectTab(0); edge.Show(); await Task.Delay(80); Capture(edge, "visual-history-empty");
                if (!Descendants(edge).OfType<TextBlock>().Any(t => t.Text == "Jeszcze bez odczytów")) throw new Exception("History empty state missing");
                var now = DateTimeOffset.Now;
                scratch.Samples.Add(new("Codex", "single", "Model z długą nazwą · pojedynczy odczyt", now.AddMinutes(-15), 42, null));
                scratch.Samples.Add(new("Codex", "gaps", "Przerwy i reset", now.AddHours(-2), 25, 1));
                scratch.Samples.Add(new("Codex", "gaps", "Przerwy i reset", now.AddHours(-2).AddMinutes(5), 30, 1));
                scratch.Samples.Add(new("Codex", "gaps", "Przerwy i reset", now.AddMinutes(-40), 70, 1));
                scratch.Samples.Add(new("Codex", "gaps", "Przerwy i reset", now.AddMinutes(-35), 0, 2));
                edge.RefreshHistory(); await Task.Delay(80); Capture(edge, "visual-history-sparse"); edge.Close();
                var gallery = new Window { Title = "Stany postaci · dane testowe", Width = 780, Height = 400,
                    Background = new SolidColorBrush(Color.FromRgb(24, 28, 35)), WindowStartupLocation = WindowStartupLocation.CenterScreen };
                var grid = new UniformGrid { Rows = 2, Columns = 8, Margin = new Thickness(16) };
                gallery.Content = grid;
                foreach (var name in new[] { "Claude", "Codex" })
                    foreach (MascotState state in Enum.GetValues<MascotState>())
                    {
                        var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3) };
                        p.Children.Add(InsightsUi.Text(name, 11)); p.Children.Add(new MascotView(name, state, true));
                        p.Children.Add(InsightsUi.Text(MascotView.Label(state), 10, InsightsUi.Muted)); grid.Children.Add(p);
                    }
                gallery.Show(); await Task.Delay(200); Capture(gallery, "visual-mascots"); gallery.Close();
                VerifyMascotMotion();
                var tipTarget = Descendants(main).OfType<FrameworkElement>()
                    .First(e => System.Windows.Automation.AutomationProperties.GetHelpText(e).StartsWith("Otwórz listę sesji"));
                if (((StackPanel)((ToolTip)tipTarget.ToolTip).Content).Children.OfType<TextBlock>().Any(t => t.Text.Contains("snake-game")))
                    throw new Exception("Session tooltip repeats visible projects");
                if (ToolTipService.GetInitialShowDelay(tipTarget) != 120 || ToolTipService.GetBetweenShowDelay(tipTarget) != 1000)
                    throw new Exception("Tooltip timing is inconsistent");
                var samples = new[] {
                    ("sessions", "Otwórz listę sesji", ""),
                    ("quota", "Źródło danych", "Odczyt: 18.10.2026 09:35"),
                    ("long", new string('T', 400), string.Join("\n", Enumerable.Repeat(new string('x', 1000), 40))) };
                foreach (var (file, title, description) in samples)
                {
                    WidgetTips.Set(tipTarget, title, description);
                    var tip = (ToolTip)tipTarget.ToolTip; tip.PlacementTarget = tipTarget; tip.IsOpen = true;
                    await Task.Delay(140); tip.UpdateLayout();
                    if (tip.ActualWidth > 321 || tip.ActualHeight > 239 || tip.ActualWidth < 40)
                        throw new Exception("Tooltip exceeds its display bounds");
                    Capture(tip, "visual-tooltip-" + file); tip.IsOpen = false;
                }
                // A reused card must still react to a fresh quota and to a session changing state.
                var quotaField = typeof(MainWindow).GetField("claudeState", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var quota = (ProviderState)quotaField.GetValue(main)!;
                quotaField.SetValue(main, quota with { Windows = quota.Windows.Select(w => w with { Used = 35 }).ToList() });
                var sessionField = typeof(MainWindow).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var snapshot = (SessionSnapshot)sessionField.GetValue(main)!;
                sessionField.SetValue(main, snapshot with { Sessions = snapshot.Sessions.Select(s => s.Provider == "Claude"
                    ? s with { State = "waiting", Directory = "C:/pending-project", Reason = "Odpowiedź" } : s).ToList() });
                main.SetCompactForTest(true); await Task.Delay(80);
                texts = Descendants(main).OfType<TextBlock>().Select(t => t.Text).ToList();
                if (!texts.Contains("65%") || !texts.Any(t => t.Contains("pending-project"))) throw new Exception("Quota or session state did not invalidate its card");
                main.Hide(); if (main.IsVisible) throw new Exception("Hide failed");
                main.Restore(); if (!main.IsVisible) throw new Exception("Restore failed");
                var selected = new MainWindow(new Settings { Language = "pl", Compact = true, Topmost = false, EnabledProviders = new[] { "Codex" } }, true);
                selected.Show(); await Task.Delay(100);
                var selectedText = Descendants(selected).OfType<TextBlock>().Select(t => t.Text).ToArray();
                if (!selectedText.Contains("Codex") || selectedText.Any(t => t is "Claude" or "Devin" or "JetBrains Air" or "Antigravity"))
                    throw new Exception("Unselected provider cards remain visible");
                Capture(selected, "visual-selected-codex");
                Descendants(selected).OfType<Button>().First(b => AutomationProperties.GetHelpText(b) == "Historia limitów i szczegóły")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var selectedInsights = Application.Current.Windows.OfType<InsightsWindow>().Single(w => ReferenceEquals(w.Owner, selected));
                var selectedFilter = Descendants(selectedInsights).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Dostawca");
                if (selectedFilter.Items.Count != 1 || selectedFilter.Items[0].ToString() != "Codex") throw new Exception("Insights exposes a hidden provider");
                selectedInsights.Close();
                void Choose(string name, bool enabled) {
                    var providerMenu = selected.ContextMenu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Dostawcy");
                    var item = providerMenu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == name);
                    item.IsChecked = enabled; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                }
                Choose("Codex", false); await Task.Delay(80);
                selectedText = Descendants(selected).OfType<TextBlock>().Select(t => t.Text).ToArray();
                if (!selectedText.Contains("Wybierz dostawców") || selectedText.Contains("Codex")) throw new Exception("Empty provider choice has no onboarding or leaves an old card");
                Capture(selected, "visual-provider-empty");
                Choose("Claude", true); Choose("Codex", true); await Task.Delay(80);
                selectedText = Descendants(selected).OfType<TextBlock>().Select(t => t.Text).Where(t => SetupDiagnostics.Providers.Contains(t)).ToArray();
                if (!selectedText.SequenceEqual(new[] { "Codex", "Claude" })) throw new Exception("Live provider toggles lose stable order or reused cards");
                var selectedCodex = (ProviderState)typeof(MainWindow).GetField("codexState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(selected)!;
                typeof(MainWindow).GetField("codexState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(selected, selectedCodex with { Connected = false, Status = "Brak połączenia" });
                selected.SetCompactForTest(true);
                selected.UpdateLayout(); await Task.Delay(80);
                if (!Descendants(selected).OfType<TextBlock>().Any(t => t.Text == "Codex")) throw new Exception("Temporary failure hides a selected provider");
                Capture(selected, "visual-provider-multiple");
                var selectedTray = (System.Windows.Forms.NotifyIcon)typeof(MainWindow).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(selected)!;
                var trayChoices = selectedTray.ContextMenuStrip!.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i => i.Text == "Dostawcy");
                var trayClaude = trayChoices.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i => i.Text == "Claude");
                trayClaude.Checked = true; trayClaude.PerformClick(); await Task.Delay(80);
                if (Descendants(selected).OfType<TextBlock>().Any(t => t.Text == "Claude")) throw new Exception("Tray provider toggle does not hide its card");
                // Exercise the actual language options, including an already-open
                // details window and both directions of cached quota translation.
                MenuItem LanguageChoice(string label) => selected.ContextMenu.Items.OfType<MenuItem>()
                    .Single(i => i.Header?.ToString()?.Contains("Language") == true).Items.OfType<MenuItem>()
                    .Single(i => i.Header?.ToString() == label);
                LanguageChoice("English").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(80);
                if (L.Language != "en" || !Descendants(selected).OfType<TextBlock>().Any(t => t.Text == "Remaining")
                    || !selected.ContextMenu.Items.OfType<MenuItem>().Any(i => i.Header?.ToString() == "Refresh limits"))
                    throw new Exception("English language option did not translate the current view");
                if (!selectedTray.ContextMenuStrip!.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Any(i => i.Text == "Providers"))
                    throw new Exception("Tray menu was not translated");
                Capture(selected, "visual-english-compact");
                selected.SetCompactForTest(false); await Task.Delay(80); Capture(selected, "visual-english-full");
                var cachedError = new SessionSnapshot(new(), DateTimeOffset.Now, CodexError: "Monitor sesji Codex niedostępny · sprawdź wersję aplikacji");
                typeof(MainWindow).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(selected, cachedError);
                var englishDetails = selected.InsightsForTest(0); await Task.Delay(80);
                var englishTabs = Descendants(englishDetails).OfType<TabControl>().Single();
                if (!englishTabs.Items.OfType<TabItem>().Select(t => t.Header?.ToString())
                    .SequenceEqual(new[] { "Overview", "Limits", "Projects", "Models and tokens", "Sessions" }))
                    throw new Exception("English details tabs are incomplete");
                for (int tab = 0; tab < 5; tab++) {
                    englishTabs.SelectedIndex = tab; await Task.Delay(80);
                    if (tab is 0 or 4 && !Descendants(englishDetails).OfType<TextBlock>().Any(t => t.Text == "Codex session monitor unavailable · check the app version"))
                        throw new Exception("Cached session error did not follow the language selection");
                    Capture(englishDetails, "visual-english-details-" + tab);
                }
                LanguageChoice("Polski").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(80);
                if (L.Language != "pl" || !Descendants(selected).OfType<TextBlock>().Any(t => t.Text == "pozostało")
                    || !selected.ContextMenu.Items.OfType<MenuItem>().Any(i => i.Header?.ToString() == "Dostawcy"))
                    throw new Exception("Switching back to Polish left English views");
                if (!Application.Current!.Windows.OfType<InsightsWindow>().Any(w => ReferenceEquals(w.Owner, selected)))
                    throw new Exception("Open details window was lost during language change");
                var polishTrayLanguage = selectedTray.ContextMenuStrip!.Items.OfType<System.Windows.Forms.ToolStripMenuItem>()
                    .Single(i => i.Text?.Contains("Language") == true);
                polishTrayLanguage.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i => i.Text == "English").PerformClick();
                await Task.Delay(80);
                if (L.Language != "en") throw new Exception("Tray language option did not work");
                selected.Close();
                File.WriteAllText(Path.Combine(Settings.DataDirectory, "visual-check.json"), JsonSerializer.Serialize(new { Passed = true, Captures = 34, LiveLanguageSelection = true, EnglishAndPolish = true, SelectedProviders = true, LiveProviderSelection = true, EmptyProviderSelection = true, TrayProviderSelection = true, CompactSessions = true, HistoryButton = true, HideAndRestore = true, ScrollPreserved = true, StateChanges = true, ProjectExpansion = true, ProjectRankings = true, SessionPagination = true, CodexSessionNavigation = true, ClaudeSessionNavigation = true, NarrowWindow = true, EmptyAndSparseHistory = true, BoundedTooltips = true, ProviderNavigation = true, ScopedModelsAndSessions = true, SharedPeriod = true, main.ActualWidth, main.ActualHeight, main.IsVisible }));
            }
            catch (Exception e) { result = 1; File.WriteAllText(Path.Combine(Settings.DataDirectory, "visual-check.json"), JsonSerializer.Serialize(new { Passed = false, Error = e.ToString() })); }
            finally { main.Close(); app.Shutdown(); }
        };
        main.Show(); app.Run(); return result;
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    static void VerifyMascotMotion()
    {
        byte[] Frame(MascotView view, double phase) {
            view.Measure(new Size(80, 64)); view.Arrange(new Rect(0, 0, 80, 64));
            typeof(MascotView).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, phase);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                typeof(MascotView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, new object[] { dc });
            var bitmap = new RenderTargetBitmap(80, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var pixels = new byte[80 * 64 * 4]; bitmap.CopyPixels(pixels, 80 * 4, 0); return pixels;
        }
        foreach (string provider in new[] { "Codex", "Claude" })
            foreach (MascotState state in Enum.GetValues<MascotState>()) {
                var still = new MascotView(provider, state, false);
                if (!Frame(still, 0).SequenceEqual(Frame(still, .65))) throw new Exception("Disabled mascot animation is not static: " + provider + state);
                var animated = new MascotView(provider, state, true);
                bool changed = !Frame(animated, 0).SequenceEqual(Frame(animated, .65));
                bool shouldMove = SystemParameters.ClientAreaAnimation && state is not (MascotState.Offline or MascotState.Exhausted);
                if (changed != shouldMove) throw new Exception("Mascot motion does not match activity/reduced-motion state: " + provider + state);
            }
    }
}
