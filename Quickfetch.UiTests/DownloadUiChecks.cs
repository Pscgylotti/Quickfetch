using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quickfetch;
using Quickfetch.Core;

internal static class DownloadUiChecks
{
    internal static void RunRecordRemoval(Action<bool, string> check)
    {
        ((App)Application.Current).SetTheme();
        var root = Path.Combine(Path.GetTempPath(), "Quickfetch-record-removal-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var jobs = Enumerable.Range(0, 3).Select(i => new DownloadJob { Url = "https://example.com/record-" + i + ".txt", Name = "record-" + i + ".txt", Folder = root, Status = i == 2 ? DownloadStatus.Paused : DownloadStatus.Completed, CompletedPath = Path.Combine(root, "record-" + i + ".txt") }).ToArray();
            foreach (var job in jobs) File.WriteAllText(job.CompletedPath, "Keep this file.");
            var state = Path.Combine(root, "queue.json"); File.WriteAllText(state, JsonSerializer.Serialize(jobs));
            var settings = new Settings { Downloads = new DownloadOptions { Folder = root, ProxyMode = "none" } };
            using var manager = new DownloadManager(state, () => settings.Downloads);
            var window = new DownloadsWindow(manager, settings, _ => { });
            try
            {
                window.Show(); window.UpdateLayout();
                var list = Descendants<ListBox>(window).Single();
                VirtualizingPanel.SetIsVirtualizing(list, false); window.UpdateLayout();
                var remove = Descendants<Button>(window).Where(b => b.Tag as string == "remove").ToArray();
                check(remove.Count(b => b.Visibility == Visibility.Visible) == 2 && remove.Single(b => b.DataContext is DownloadView v && v.Job.Id == jobs[2].Id).Visibility == Visibility.Collapsed, "Only completed history items may expose record removal.");
                remove.Single(b => b.DataContext is DownloadView v && v.Job.Id == jobs[0].Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(manager.Snapshot().Length == 2 && File.Exists(jobs[0].CompletedPath), "The history button must remove only its record and retain the downloaded file.");
                var toast = new DownloadToast(jobs[1], manager);
                try
                {
                    toast.Show(); toast.UpdateLayout();
                    var button = Descendants<Button>(toast).Single(b => b.Content as string == "移除记录");
                    check(button.IsVisible, "Completed notifications must expose their own record-removal action.");
                    Save(toast, "artifacts/download-completed-toast-preview.png");
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(!toast.IsVisible && manager.Snapshot().Single().Id == jobs[2].Id && File.Exists(jobs[1].CompletedPath), "Removing a toast record must dismiss the toast, retain the file and preserve unfinished tasks.");
                    check(JsonSerializer.Deserialize<DownloadJob[]>(File.ReadAllText(state))!.Single().Id == jobs[2].Id, "Removed records must stay absent in the persisted queue.");
                }
                finally { toast.Close(); }
            }
            finally { window.Close(); }
        }
        finally { foreach (var file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
    internal static void Run(MainWindow main, Action<bool, string> check)
    {
        RunRecordRemoval(check);
        ((App)Application.Current).SetTheme();
        check(main.FindName("DownloadsButton") is Button, "The launcher must provide a download button without a permanent progress pane.");
        var root = Path.Combine(Path.GetTempPath(), "Quickfetch-ui-download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var job = new DownloadJob { Url = "https://example.com/file.zip", Name = "Example download.zip", Folder = root, Status = DownloadStatus.Paused, Size = 16 * 1024 * 1024, Ranged = true, Pointers = Enumerable.Range(0, 8).Select(i => (long)i * 2 * 1024 * 1024 + 800000).ToArray() };
        for (int i = 0; i < 8; i++) job.Ranges.Add(new() { Start = i * 2 * 1024 * 1024, End = (i + 1) * 2 * 1024 * 1024 - 1, Next = job.Pointers[i] });
        var file = Path.Combine(root, "queue.json"); File.WriteAllText(file, JsonSerializer.Serialize(new[] { job }));
        var settings = new Settings { Downloads = new DownloadOptions { Folder = root, ProxyMode = "none" } };
        try
        {
            using var manager = new DownloadManager(file, () => settings.Downloads);
            var window = new DownloadsWindow(manager, settings, _ => { }); window.Show(); window.UpdateLayout();
            check(window.IsVisible && manager.Snapshot().Single().Status == DownloadStatus.Paused, "Opening download history must not resume paused tasks.");
            Save(window, "artifacts/download-queue-preview.png"); window.Close();
            var toast = new DownloadToast(job, manager); toast.Show(); toast.UpdateLayout(); Save(toast, "artifacts/download-toast-preview.png");
            check(toast.Content is Border surface && ReferenceEquals(surface.Background, toast.FindResource("Surface")), "Download notifications must share the launcher's translucent surface rather than an opaque tooltip background.");
            check(System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(toast).Handle)?.CompositionTarget?.BackgroundColor.A == 0, "The toast composition target must stay transparent for native Acrylic.");
            var buttons = Descendants<Button>(toast).ToArray();
            check(buttons.Any(b => b.ToolTip as string == "关闭提示，下载继续") && buttons.Any(b => b.ToolTip as string == "收起 / 展开"), "Progress toasts must expose independent collapse and close controls.");
            buttons.Single(b => b.ToolTip as string == "收起 / 展开").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(toast.Height == 48 && manager.Snapshot().Single().Status == DownloadStatus.Paused, "Collapsing the toast must not change task state.");
            buttons.Single(b => b.ToolTip as string == "收起 / 展开").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(toast.Height == 194, "The compact progress map must be expandable again."); toast.Close();
            check(manager.Snapshot().Length == 1, "Closing a toast must preserve its queue item.");
            using var stream = new MemoryStream();
            DownloadBridge.Write(stream, new DownloadRequest("https://example.com/test.zip"), CancellationToken.None).GetAwaiter().GetResult(); stream.Position = 0;
            var roundtrip = DownloadBridge.Read<DownloadRequest>(stream, CancellationToken.None).GetAwaiter().GetResult();
            check(roundtrip?.Url == "https://example.com/test.zip", "Native messaging must use the length-framed UTF-8 JSON protocol.");
            using var invalid = new MemoryStream(BitConverter.GetBytes(1000000));
            try { DownloadBridge.Read<DownloadRequest>(invalid, CancellationToken.None).GetAwaiter().GetResult(); check(false, "Oversized bridge input must be rejected."); } catch (IOException) { check(true, "Bridge input size is bounded."); }
            check(DownloadBridge.IsNativeArgs(["chrome-extension://" + DownloadBridge.ExtensionId + "/"]) && DownloadBridge.IsNativeArgs(["manifest.json", DownloadBridge.FirefoxId]), "Chromium and Firefox launches must enter bridge mode without showing the launcher.");
            using var stop = new CancellationTokenSource();
            var serving = DownloadBridge.Serve(manager, stop.Token);
            try
            {
                foreach (var arguments in new[] { "chrome-extension://" + DownloadBridge.ExtensionId + "/", "\"" + Path.Combine(Settings.Storage, "native-download-host-firefox.json") + "\" " + DownloadBridge.FirefoxId })
                {
                    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.ChangeExtension(typeof(App).Assembly.Location, ".exe"), arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
                    using var timeout = new CancellationTokenSource(10000);
                    DownloadBridge.Write(process.StandardInput.BaseStream, new DownloadRequest("file:///not-a-download"), timeout.Token).GetAwaiter().GetResult();
                    var reply = DownloadBridge.Read<BridgeReply>(process.StandardOutput.BaseStream, timeout.Token).GetAwaiter().GetResult();
                    process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                    check(process.ExitCode == 0 && reply is { Ok: false }, "The real WinExe native host must reject unsafe URLs through Chromium and Firefox framing without opening the launcher.");
                }
            }
            finally { stop.Cancel(); try { serving.Wait(1000); } catch (AggregateException) { } }
        }
        finally { foreach (var child in Directory.GetFiles(root)) File.Delete(child); Directory.Delete(root); }
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static void Save(FrameworkElement view, string path)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory("artifacts"); using var output = File.Create(path); encoder.Save(output);
    }
}
