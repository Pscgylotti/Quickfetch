using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Quickfetch;
using Quickfetch.Core;

internal static class RegisteredPreviewChecks
{
    internal static void Run(MainWindow window, Action<bool, string> check, bool integration)
    {
        var options = JsonSerializer.Deserialize<Settings>("{}")!;
        check(options.IncludeRegisteredApps && !options.EnableFilePreview, "Registered apps default on; preview is opt-in.");
        options.IncludeRegisteredApps = false; options.EnableFilePreview = true;
        var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(options))!;
        check(!restored.IncludeRegisteredApps && restored.EnableFilePreview, "Both preferences must persist.");
        var registered = RegisteredAppCatalog.Entry("聊天工具", "Example_123!App");
        check(registered.IsRegisteredApp && registered.AppUserModelId == "Example_123!App" && registered.Directory == "Windows 注册应用", "AUMID identities must round-trip without pretending to be files.");
        check(SearchMatcher.Search([registered], "liaotian").Count == 1, "Registered apps must share pinyin matching.");
        var shortcut = new ShortcutEntry("My shortcut", "C:\\Example.lnk");
        var merged = RegisteredAppCatalog.Merge([shortcut], [registered, RegisteredAppCatalog.Entry("My shortcut", "Other_123!App")], new Dictionary<string, string> { [shortcut.Path] = "Example_123!App" });
        check(merged.Length == 2 && merged.Contains(shortcut) && merged.Any(e => e.AppUserModelId == "Other_123!App"), "Identity deduplication must preserve shortcuts and equally named different apps.");
        check(TouchScrollBar.Offset(0, 50, 1000, 100) == 500 && TouchScrollBar.Offset(500, -1000, 1000, 100) == 0, "Thumb movement must expose intermediate offsets and clamp bounds.");
        check(((CheckBox)window.FindName("RegisteredAppsCheck")).IsChecked == true && ((CheckBox)window.FindName("FilePreviewCheck")).IsChecked == false, "The settings panel must expose both preferences.");
        var temp = Path.Combine(Path.GetTempPath(), "Quickfetch-preview-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var textPath = Path.Combine(temp, "sample.txt"); File.WriteAllText(textPath, "预览测试\n" + new string('a', 100000));
            var text = FilePreviewService.LoadAsync(new("sample.txt", temp, textPath, false, 100000, null), CancellationToken.None).GetAwaiter().GetResult();
            check(text.Text.StartsWith("预览测试") && text.Text.Length < 33000 && text.Text.Contains("32 KiB"), "Text preview must be bounded and support UTF-8.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { FilePreviewService.LoadAsync(new("sample.txt", temp, textPath, false, 100000, null), cancelled.Token).GetAwaiter().GetResult(); check(false, "Cancelled previews must stop."); }
            catch (OperationCanceledException) { check(true, "Cancelled previews stop."); }
            var folder = FilePreviewService.LoadAsync(new("Folder", temp, temp, true, null, null), CancellationToken.None).GetAwaiter().GetResult();
            check(folder.Text.Contains("不递归") && folder.Image!.IsFrozen, "Folder previews must avoid recursive I/O.");
            var executable = typeof(MainWindow).Assembly.Location;
            var version = FilePreviewService.LoadAsync(new("Quickfetch.dll", temp, executable, false, null, null), CancellationToken.None).GetAwaiter().GetResult();
            check(version.Details.Contains("文件版本"), "Executable previews must expose version metadata without running files.");
            var exePath = Path.ChangeExtension(executable, ".exe");
            var exe = FilePreviewService.LoadAsync(new("Quickfetch.exe", temp, exePath, false, null, null), CancellationToken.None).GetAwaiter().GetResult();
            check(exe.Image is System.Windows.Media.Imaging.BitmapSource { IsFrozen: true, PixelWidth: <= 128, PixelHeight: <= 128 }, "EXE previews must extract the executable's own icon without running it.");
            if (integration)
            {
                var catalog = new RegisteredAppCatalog(Path.Combine(temp, "apps.json"));
                var snapshot = catalog.LoadAsync([], false).GetAwaiter().GetResult();
                check(snapshot.Entries.Length > 0 && snapshot.Entries.All(e => e.IsRegisteredApp), "Live AppsFolder enumeration must return packaged app identities.");
                var chat = snapshot.Entries.FirstOrDefault(e => e.AppUserModelId == "OpenAI.Codex_2p2nqsd0c76g0!App");
                if (chat is not null) check(snapshot.Icons[chat.Path] is { IsFrozen: true }, "An installed ChatGPT must have a cached, frozen icon.");
                var stamp = File.GetLastWriteTimeUtc(Path.Combine(temp, "apps.json"));
                var again = catalog.LoadAsync([], false).GetAwaiter().GetResult();
                check(ReferenceEquals(snapshot.Entries, again.Entries) && File.GetLastWriteTimeUtc(Path.Combine(temp, "apps.json")) == stamp, "Warm refreshes must reuse app icons and avoid cache writes.");
                foreach (var name in new[] { "image.png", "video.mp4", "audio.mp3" })
                {
                    var path = Path.GetFullPath(Path.Combine("artifacts", "preview-fixtures", name));
                    if (!File.Exists(path)) continue;
                    var preview = FilePreviewService.LoadAsync(new(name, Path.GetDirectoryName(path)!, path, false, new FileInfo(path).Length, null), CancellationToken.None).GetAwaiter().GetResult();
                    check(preview.Image is System.Windows.Media.Imaging.BitmapSource { PixelWidth: <= 640, PixelHeight: <= 480, IsFrozen: true }, "Media previews must remain bounded and frozen: " + name);
                    if (name == "video.mp4") check(preview.Details.Contains("首个可解码") && preview.Details.Contains("1280 × 720") && preview.Details.Contains("比特率"), "Video must expose its first frame, original size, duration and bitrate.");
                    if (name == "audio.mp3") check(preview.Details.Contains("Quickfetch Test Album") && preview.Details.Contains("44100") && preview.Details.Contains("内嵌专辑"), "Music must expose embedded art, album and sample rate.");
                }
            }
        }
        finally { Directory.Delete(temp, true); }
        CheckTouch(window, check);
        CheckPreviewPanel(window, check);
    }
    private static void CheckPreviewPanel(MainWindow window, Action<bool, string> check)
    {
        window.ShowLauncher();
        ((Button)window.FindName("ModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var preference = (CheckBox)window.FindName("FilePreviewCheck"); preference.IsChecked = true; preference.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(((FrameworkElement)window.FindName("FilePreviewPanel")).Visibility == Visibility.Visible && window.Width > 784, "Enabling preview must open an additional panel to the right.");
        var results = (ListBox)window.FindName("Results"); var path = Path.GetFullPath("README.md");
        var item = new FileItem(new("README.md", Path.GetDirectoryName(path)!, path, false, new FileInfo(path).Length, null));
        results.ItemsSource = new[] { item }; results.SelectedItem = item;
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (((TextBlock)window.FindName("PreviewText")).Text.Length == 0 && DateTime.UtcNow < deadline)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
        check(((TextBlock)window.FindName("PreviewText")).Text.StartsWith("# Quickfetch"), "Selecting a file must update the preview asynchronously.");
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts"); using (var stream = File.Create("artifacts/file-preview-panel.png")) encoder.Save(stream);
        preference.IsChecked = false; preference.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(((FrameworkElement)window.FindName("FilePreviewPanel")).Visibility == Visibility.Collapsed && ((Image)window.FindName("PreviewImage")).Source is null, "Disabling previews must clear retained images and collapse the panel.");
        ((Button)window.FindName("ModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Hide();
    }
    private static void CheckTouch(MainWindow window, Action<bool, string> check)
    {
        window.ShowLauncher();
        ((Button)window.FindName("ModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var results = (ListBox)window.FindName("Results");
        results.ItemsSource = Enumerable.Range(0, 200).Select(i => new FileItem(new("test" + i, "C:\\", "C:\\test" + i, false, i, null))).ToArray();
        window.UpdateLayout();
        var scroll = Descendant<ScrollViewer>(results)!;
        var bar = Descendants<ScrollBar>(scroll).First(b => b.Orientation == Orientation.Vertical);
        bar.ApplyTemplate(); var track = (Track)bar.Template.FindName("PART_Track", bar); window.UpdateLayout();
        check(bar.ActualWidth >= 32 && TouchScrollBar.GetEnabled(bar), "Scrollbar must provide a wide transparent touch target.");
        var start = track.Thumb.TranslatePoint(new Point(track.Thumb.ActualWidth / 2, track.Thumb.ActualHeight / 2), window);
        var device = new TestTouch(window, 9001);
        device.Down(start); device.Move(new Point(start.X, start.Y + 60)); window.UpdateLayout();
        check(scroll.VerticalOffset > 0 && scroll.VerticalOffset < scroll.ScrollableHeight, "A held touch drag must scroll the list before release.");
        var offset = scroll.VerticalOffset;
        device.Up(new Point(start.X, start.Y + 60)); window.UpdateLayout();
        check(Math.Abs(scroll.VerticalOffset - offset) < 1 && device.Captured is null, "Touch release must preserve the live preview position and release capture.");
        ((Button)window.FindName("ModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Hide();
    }
    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject => Descendants<T>(root).FirstOrDefault();
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class TestTouch(Window window, int id) : TouchDevice(id)
    {
        private Point _point; private TouchAction _action;
        public void Down(Point point) { _point = point; _action = TouchAction.Down; SetActiveSource(PresentationSource.FromVisual(window)); Activate(); ReportDown(); }
        public void Move(Point point) { _point = point; _action = TouchAction.Move; ReportMove(); }
        public void Up(Point point) { _point = point; _action = TouchAction.Up; ReportUp(); Deactivate(); }
        public override TouchPoint GetTouchPoint(IInputElement relativeTo)
        {
            var point = relativeTo is Visual visual ? window.TransformToVisual(visual).Transform(_point) : _point;
            return new(this, point, new Rect(point, new Size(2, 2)), _action);
        }
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo) => new() { GetTouchPoint(relativeTo) };
    }
}
