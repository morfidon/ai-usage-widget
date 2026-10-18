using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsageWidget;

public static class VisualVerification
{
    public static void Capture(Window window, string name)
    {
        foreach (var view in Descendants(window).OfType<MascotView>())
        {
            ((System.Windows.Threading.DispatcherTimer)typeof(MascotView).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!).Stop();
            typeof(MascotView).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, 1.75);
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
        var main = new MainWindow(new Settings { Compact = true, Topmost = false, Left = 20, Top = 20 }, true);
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
                projects.Width = 620; projects.Height = 440; await Task.Delay(80); Capture(projects, "visual-projects-narrow");
                main.InsightsForTest(0); await Task.Delay(80); Capture(projects, "visual-history-narrow");
                var scratch = new UsageHistory(Path.Combine(Settings.DataDirectory, "visual-empty-" + Guid.NewGuid() + ".json"));
                var edge = new InsightsWindow(scratch); edge.Show(); await Task.Delay(80); Capture(edge, "visual-history-empty");
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
                File.WriteAllText(Path.Combine(Settings.DataDirectory, "visual-check.json"), JsonSerializer.Serialize(new { Passed = true, Captures = 11, CompactSessions = true, HistoryButton = true, HideAndRestore = true, ScrollPreserved = true, StateChanges = true, ProjectExpansion = true, NarrowWindow = true, EmptyAndSparseHistory = true, main.ActualWidth, main.ActualHeight, main.IsVisible }));
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
}
