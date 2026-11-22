using System;
using System.IO;
using System.IO.Compression;
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
            CheckNewFormats(temp, check);
            var textPath = Path.Combine(temp, "sample.txt"); File.WriteAllText(textPath, "预览测试\n" + new string('a', 100000));
            var text = FilePreviewService.LoadAsync(new("sample.txt", temp, textPath, false, 100000, null), CancellationToken.None).GetAwaiter().GetResult();
            check(text.Text.StartsWith("预览测试") && text.Text.Length < 33000 && text.Text.Contains("32 KiB"), "Text preview must be bounded and support UTF-8.");
            var warm = FilePreviewService.LoadAsync(new("sample.txt", temp, textPath, false, 100000, null), CancellationToken.None).GetAwaiter().GetResult();
            check(ReferenceEquals(text, warm), "Repeated unchanged previews must reuse the bounded memory cache.");
            File.WriteAllText(textPath, "Changed preview");
            var changed = FilePreviewService.LoadAsync(new("sample.txt", temp, textPath, false, 15, null), CancellationToken.None).GetAwaiter().GetResult();
            check(changed.Text == "Changed preview" && !ReferenceEquals(changed, warm), "Changing file size or timestamp must invalidate its cached preview.");
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
                    if (name == "video.mp4") check((preview.Details.Contains("首个可解码") || preview.Details.Contains("Windows")) && preview.Details.Contains("1280 × 720") && preview.Details.Contains("比特率"), "Video must expose its first frame, original size, duration and bitrate.");
                    if (name == "audio.mp3") check(preview.Details.Contains("Quickfetch Test Album") && preview.Details.Contains("44100") && (preview.Details.Contains("内嵌专辑") || preview.Details.Contains("Windows")), "Music must expose embedded art, album and sample rate.");
                }
            }
        }
        finally { Directory.Delete(temp, true); }
        CheckTouch(window, check);
        CheckPreviewPanel(window, check);
    }
    private static void CheckNewFormats(string directory, Action<bool, string> check)
    {
        foreach (var name in new[] { "main.rs", "Dockerfile", ".gitignore", "data.tsv" })
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "预览 sample");
            var file = new FileEntry(name, directory, path, false, null, null);
            check(FilePreviewService.LoadAsync(file, CancellationToken.None).GetAwaiter().GetResult().Text == "预览 sample",
                "Text and extensionless sources must have readable previews: " + name);
        }
        foreach (var (ext, part, xml, expected) in new[]
        {
            (".docx", "word/document.xml", "<doc><p><t>Hello </t><t>world.</t></p></doc>", "Hello world."),
            (".pptx", "ppt/slides/slide1.xml", "<slide><p><t>Summary</t></p></slide>", "Summary"),
            (".xlsx", "xl/workbook.xml", "<workbook><sheets><sheet name='Budget'/></sheets></workbook>", "Budget"),
            (".odt", "content.xml", "<doc><p>Mixed <span>styles</span></p></doc>", "Mixed styles")
        })
        {
            var path = Path.Combine(directory, "example" + ext);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry(part).Open())) writer.Write(xml);
            check(PackagePreview.Read(path, ext, CancellationToken.None).Contains(expected),
                "Packages without a system thumbnail must fall back to readable contents: " + ext);
        }
        var zipPath = Path.Combine(directory, "files.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            for (var i = 0; i <= 40; i++) archive.CreateEntry($"folder/file{i}.txt");
        var listing = PackagePreview.Read(zipPath, ".zip", CancellationToken.None);
        check(listing.Contains("folder/file39.txt") && !listing.Contains("file40.txt") && listing.EndsWith("…"),
            "Archive previews must list at most 40 entry names.");
        var result = FilePreviewService.LoadAsync(new("files.zip", directory, zipPath, false, null, null), CancellationToken.None).GetAwaiter().GetResult();
        check(result.Text == listing || result.Image is System.Windows.Media.Imaging.BitmapSource && result.Details.Contains("Windows"),
            "The file preview pane must prefer a Windows thumbnail or show the archive contents.");
        File.WriteAllText(zipPath, "invalid zip");
        check(PackagePreview.Read(zipPath, ".zip", CancellationToken.None) == "", "Damaged packages must fall back to metadata.");
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
        FileUiChecks.PumpAnimation(window);
        var results = (ListBox)window.FindName("Results");
        results.ItemsSource = Enumerable.Range(0, 200).Select(i => new FileItem(new("test" + i, "C:\\", "C:\\test" + i, false, i, null))).ToArray();
        window.UpdateLayout();
        ((FrameworkElement)window.FindName("EmptyMessage")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)window.FindName("InstallEverythingButton")).Visibility = Visibility.Collapsed;
        window.SystemKeyboardRequest = _ => true;
        window.SetKeyboardPreference(false, showKeyboard: true);
        results.SelectedIndex = 0;
        window.UpdateLayout();
        var barPanel = (Border)window.FindName("FileSelectionBar");
        ListBoxItem Row(int i) => (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(i);
        CheckBox Box(int i) => Descendant<CheckBox>(Row(i))!;
        Point BoxPoint(int i) => Box(i).TranslatePoint(new Point(Box(i).ActualWidth / 2, Box(i).ActualHeight / 2), window);
        var tap = new TestTouch(window, 9000);
        void Tap(Point point) { tap.Down(point); tap.Up(point); window.UpdateLayout(); }
        check(!barPanel.IsVisible && Box(0).IsChecked == false, "Automatic highlight must leave the checkboxes empty and collapse the action bar.");
        Tap(BoxPoint(1));
        check(Box(1).IsChecked == true && Box(0).IsChecked == false && results.SelectedIndex == 0 &&
            barPanel.IsVisible && Descendants<Button>(barPanel).Count(b => b.IsEnabled && b.ActualHeight >= 40) == 3,
            "Only the tapped checkbox should change, and the first check must reveal the three actions.");
        Tap(Row(2).TranslatePoint(new Point(85, Row(2).ActualHeight / 2), window));
        check(results.SelectedIndex == 2 && Box(2).IsChecked == false && Box(1).IsChecked == true && barPanel.IsVisible,
            "Browsing another row must move the highlight while retaining checked files.");
        Tap(BoxPoint(0));
        check(window.FileClipboardTargets(true).Select(item => item.Name).SequenceEqual(new[] { "test0", "test1" }) &&
            window.FileClipboardTargets(false).Length == 2 && window.CheckedSummary.Text.Contains("1 B"),
            "Clipboard actions and the size summary must use only the explicit checks when browsing another row.");
        var scroll = Descendant<ScrollViewer>(results)!;
        scroll.ScrollToBottom(); window.UpdateLayout(); scroll.ScrollToTop(); window.UpdateLayout();
        check(Box(0).IsChecked == true && Box(1).IsChecked == true && Box(2).IsChecked == false,
            "Recycling virtual rows must preserve the checks with the correct files.");
        var point = BoxPoint(1);
        tap.Down(point); tap.Move(new Point(point.X, point.Y - 24)); tap.Up(new Point(point.X, point.Y - 24)); window.UpdateLayout();
        check(window.CheckedFilePaths.Count == 2 && results.SelectedIndex == 2, "Panning from a checkbox must preserve checks and highlight.");
        scroll.ScrollToTop(); window.UpdateLayout();
        Tap(BoxPoint(1));
        check(!Box(1).IsChecked.GetValueOrDefault() && Box(0).IsChecked == true && barPanel.IsVisible,
            "Clearing one of two checked rows must keep the remaining item and actions.");
        Tap(BoxPoint(0));
        check(!barPanel.IsVisible && results.SelectedIndex == 2 && window.FileClipboardTargets(true).Length == 0 &&
            window.FileClipboardTargets(false).Single().Name == "test2", "Clearing the final checkbox must collapse the bar and restore keyboard clipboard targeting.");
        window.SetKeyboardPreference(true, showKeyboard: false);
        scroll.ScrollToTop(); window.UpdateLayout();
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
