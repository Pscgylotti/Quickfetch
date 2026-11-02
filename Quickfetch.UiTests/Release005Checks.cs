using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quickfetch;
using Quickfetch.Core;

internal static class Release005Checks
{
    internal static void Run(Action<bool, string> check)
    {
        ((App)Application.Current).SetTheme();
        var root = Path.Combine(Path.GetTempPath(), "Quickfetch-release-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var store = new UrlHistory(Path.Combine(root, "urls.json")); var now = DateTimeOffset.UtcNow;
        store.Record("https://github.com/Pscgylotti/Quickfetch", false, "Quickfetch", now); store.Record("https://github.com/other", false, "Other", now.AddHours(-1));
        var main = new MainWindow(new Settings(), _ => { }, store);
        try
        {
            main.EverythingAvailable = () => throw new Exception("URL mode must never call Everything.");
            main.Show(); var query = (TextBox)main.FindName("Query"); query.Text = "github"; main.EnterUrlMode(); main.UpdateLayout();
            var results = (ListBox)main.FindName("Results");
            check(MainWindow.IsUrlShortcut(Key.W, ModifierKeys.Alt) && !MainWindow.IsUrlShortcut(Key.W, ModifierKeys.Control), "URL mode must use Alt+W only inside the launcher.");
            check(results.Items.Count == 2 && results.Items[0] is UrlHistoryItem { Name: "Quickfetch" } && ((Grid)main.FindName("FileSortHeader")).Visibility == Visibility.Collapsed, "URL mode must display history without querying Everything or showing file sorting columns.");
            main.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(new WindowInteropHelper(main).Handle), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            check(results.SelectedIndex == 1, "Down must move the URL history selection.");
            main.CompleteUrl();
            check(query.Text == "https://github.com/other", "Selected URL history entries must support completion.");
            main.EverythingAvailable = () => false;
            main.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(new WindowInteropHelper(main).Handle), 0, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            check(query.Text == "https://github.com/other" && ((TextBlock)main.FindName("ModeLabel")).Text.Contains("EVERYTHING") && ((Grid)main.FindName("FileSortHeader")).Visibility == Visibility.Visible, "Tab in URL mode must preserve the query and return to file search even for a complete URL.");
            main.EnterUrlMode();
            ((Button)main.FindName("ModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(query.Text == "https://github.com/other" && ((TextBlock)main.FindName("ModeLabel")).Text.Contains("EVERYTHING"), "The mode button must also return URL mode to file search without clearing the query.");
            main.EnterUrlMode(); main.EverythingAvailable = () => throw new Exception("URL mode must never call Everything.");
            query.Text = "not-in-history"; main.CompleteUrl(); check(results.Items.Count == 0, "Unmatched URL-mode queries must not fall through to file search.");
            const string forcedUrl = "https://example.invalid/archive.zip";
            store.Record(forcedUrl, true, "archive.zip", now);
            var opened = new System.Collections.Generic.List<string>(); main.OpenWebpage = opened.Add;
            query.Text = forcedUrl; main.LaunchSelected(true);
            check(opened.SequenceEqual(new[] { forcedUrl }) && !main.IsVisible, "Shift+Enter must directly open a previously classified download in the browser without probing or enqueuing it.");
            var persisted = new UrlHistory(Path.Combine(root, "urls.json"));
            check(persisted.Search("archive.zip", now).Single() is { IsFile: false, Usage.Count: 2 }, "Forced webpage navigation must overwrite and persist the old file classification after opening succeeds.");
            check(persisted.ResolveAsync(forcedUrl, _ => throw new Exception("Forced webpage cache must bypass network probes.")).GetAwaiter().GetResult().IsFile == false, "Reloaded forced webpage classifications must bypass later probes.");
            main.Show(); query.Text = forcedUrl; main.EnterUrlMode(); main.LaunchSelected();
            check(opened.Count == 2, "Ordinary Enter after forced webpage navigation must reuse the cached browser route.");
            main.Show(); query.Text = "https://example.invalid/failure.zip"; main.EnterUrlMode();
            main.OpenWebpage = _ => throw new IOException("Simulated browser launch failure"); main.LaunchSelected(true);
            check(main.IsVisible && store.Search("failure.zip", now).Count == 0, "A failed browser launch must leave the window open and must not cache a webpage classification.");
            main.OpenWebpage = opened.Add; query.Text = "github"; main.EnterUrlMode();
            main.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(new WindowInteropHelper(main).Handle), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            main.LaunchSelected(true);
            check(opened.Last() == "https://github.com/other", "Forced webpage navigation must honor the selected history entry for partial queries.");
            main.Show(); main.EnterUrlMode();
            var picker = (ComboBox)main.FindName("LanguageControl"); picker.SelectedValue = "en-US"; main.UpdateLayout();
            check(TextLocalization.IsEnglish && main.ContextMenu is null && main.FindName("ExtensionsButton") is Button b && b.ToolTip as string == "Extensions", "The saved language choice must switch launcher controls to English.");
            check(Loc.T("移除记录") == "Remove record" && Loc.F($"{3} 条网址历史 · 常用 / 最近使用") == "3 URLs · Frequency / recency", "English must cover static labels and formatted runtime text.");
            foreach (var pair in TextLocalization.All)
            {
                check(!Regex.IsMatch(pair.Value, "[\u4e00-\u9fff]"), "Every English translation must be English: " + pair.Key);
                check(Regex.Matches(pair.Key, @"\{\d+(?:[^}]*)?\}").Select(m => m.Value).SequenceEqual(Regex.Matches(pair.Value, @"\{\d+(?:[^}]*)?\}").Select(m => m.Value)), "Translation placeholders must be preserved: " + pair.Key);
            }
            query.Text = "github"; main.UpdateLayout(); Save(main, "artifacts/url-history-english-preview.png");
            var character = new CharacterMapWindow(query: "a");
            try { character.Show(); Pump(() => character.CharacterResults.Count > 0); character.UpdateLayout(); check(character.Title == "Quickfetch · Character Map" && ((Button)character.FindName("CopyButton")).Content as string == "Copy", "Character Map must use English resources."); check(Descendants<Button>((ItemsControl)character.FindName("Characters")).Count() < 100 && character.CharacterResults.Count > 100, "Character cards must only realize visible rows instead of all search results."); Save(character, "artifacts/character-map-english-preview.png"); }
            finally { character.Close(); }
            picker.SelectedValue = "zh-CN"; main.UpdateLayout(); check(!TextLocalization.IsEnglish && Loc.T("设置") == "设置", "The language selector must switch back to Chinese without corrupting names or data.");
        }
        finally { main.DisposeLauncher(); main.Close(); Loc.SetLanguage("zh-CN"); foreach (var file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
    private static void Pump(Func<bool> ready)
    {
        var frame = new DispatcherFrame(); var end = DateTime.UtcNow.AddSeconds(15); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (ready() || DateTime.UtcNow > end) frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); if (!ready()) throw new Exception("Release UI check timed out.");
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found; foreach (var item in Descendants<T>(child)) yield return item; }
    }
    private static void Save(FrameworkElement view, string path)
    {
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) { var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight); context.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 25, 30)), null, bounds); context.DrawRectangle(new VisualBrush(view), null, bounds); }
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); png.Save(file);
    }
}
