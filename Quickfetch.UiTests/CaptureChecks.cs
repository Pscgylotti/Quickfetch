using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quickfetch;

internal static class CaptureChecks
{
    internal static void Run(Action<bool, string> check, bool integration)
    {
        const string qrUrl = "https://example.org/quickfetch?q=qr";
        var writer = new ZXing.BarcodeWriterPixelData { Format = ZXing.BarcodeFormat.QR_CODE, Options = new ZXing.Common.EncodingOptions { Width = 240, Height = 240 } };
        var qr = writer.Write(qrUrl);
        var qrImage = BitmapSource.Create(qr.Width, qr.Height, 96, 96, PixelFormats.Bgra32, null, qr.Pixels, qr.Width * 4);
        check(ScreenshotQr.Decode(qrImage) == qrUrl, "QR recognition must decode screenshot pixels without OCR.");
        check(ScreenshotQr.WebLink(qrUrl)?.AbsoluteUri == qrUrl && ScreenshotQr.WebLink("file:///C:/example.exe") is null && ScreenshotQr.WebLink("plain text") is null, "Only HTTP(S) QR results may be opened.");
        var previousContext = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var root = Path.Combine(Path.GetTempPath(), "Quickfetch-capture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var image = Fixture(); var persisted = 0; var settings = new Settings { CaptureFolder = root }; var storage = new CaptureStorage(settings, _ => persisted++);
            var screen = new ScreenCapture(image, new(-1920, -200, image.PixelWidth, image.PixelHeight), [new(new(20, 20, 600, 150), "Front"), new(new(0, 0, 1000, 360), "Back")]);
            check(MainWindow.IsCaptureShortcut(Key.S, ModifierKeys.Alt) && !MainWindow.IsCaptureShortcut(Key.S, ModifierKeys.Control) && !MainWindow.IsCaptureShortcut(Key.S, ModifierKeys.Alt | ModifierKeys.Shift), "Screenshot shortcut must be Alt+S without extra modifiers.");
            var main = new MainWindow(new Settings(), _ => { });
            try { var menu = ((Button)main.FindName("ExtensionsButton")).ContextMenu; check(menu.Items.Count == 2 && menu.Items[1] is MenuItem { InputGestureText: "Alt + S" }, "Screenshot must be the second extension after Character Map."); }
            finally { main.DisposeLauncher(); main.Close(); }
            check(CaptureOverlay.Region(new(301.2, 150.2), new(-10, -20), 1000, 360) == new Int32Rect(0, 0, 302, 151), "Reverse drags must clamp to the physical desktop and include fractional boundary pixels.");
            check(CaptureOverlay.Region(new(20, 20), new(20, 20), 1000, 360).IsEmpty, "A stationary tap must not create an invalid region.");
            check(screen.WindowAt(new(50, 50))?.Title == "Front" && screen.WindowAt(new(900, 300))?.Title == "Back", "Window selection must respect desktop Z order and use screenshot-local coordinates on negative-position monitors.");
            BitmapSource? copied = null;
            var overlay = new CaptureOverlay(screen, storage, bitmap => copied = bitmap); overlay.Select(new(15, 25, 200, 80)); overlay.Copy();
            check(copied is { PixelWidth: 200, PixelHeight: 80 }, "Copy must use exactly the selected image pixels without toolbar or dimming.");
            var reselect = new CaptureOverlay(screen, storage); reselect.Select(new(0, 0, 1000, 360)); reselect.Reselect(true); check(reselect.Selection.IsEmpty, "Reselect must clear a full-screen selection and return to window selection."); reselect.Close();
            var path = storage.Save(image, saveAs: false); var path2 = storage.Save(image, saveAs: false);
            check(path != path2 && File.Exists(path) && File.Exists(path2) && persisted == 2 && settings.CaptureFolder == root, "Save to last directory must retain the directory and never overwrite a preceding screenshot.");
            using (var saved = File.OpenRead(path!)) { var png = BitmapDecoder.Create(saved, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); check(png.Frames[0].PixelWidth == image.PixelWidth && png.Frames[0].PixelHeight == image.PixelHeight, "PNG saves must preserve full screenshot resolution."); }
            string text = "";
            var review = new CaptureReviewWindow(image, storage, value => text = value, bitmap => copied = bitmap);
            try
            {
                review.Recognizer = (_, _) => Task.FromResult(new CaptureText([new("Quickfetch", 45, 35, 250, 60, 0), new("Screen", 310, 35, 190, 60, 0), new("123", 45, 120, 100, 60, 1)], "en-US"));
                Wait(review.Recognize()); check(review.WordCount == 3 && review.SelectedText.Length == 0, "OCR must overlay recognized segments without automatically selecting them.");
                review.SelectAll(true); review.CopySelection(); check(text == "Quickfetch Screen" + Environment.NewLine + "123", "Selected OCR segments must copy in reading order with line boundaries preserved.");
                review.SelectAll(false); check(review.SelectedText.Length == 0, "Clearing text selection must retain the screenshot and remove copied segments.");
                review.ShowText(new([new("invalid", double.NaN, 0, 20, 20, 0), new("outside", 3000, 0, 10, 10, 0), new("valid", 0, 0, 60, 20, 0)], "en-US"));
                check(review.WordCount == 1, "Out-of-range OCR rectangles must never corrupt overlay layout.");
                review.Recognizer = (_, _) => throw new IOException("Simulated unavailable OCR language"); Wait(review.Recognize()); check(review.WordCount == 1, "An OCR failure must preserve the screenshot and previous recognition.");
                review.ShowText(new([new("Quickfetch", 45, 35, 250, 60, 0), new("Screen", 310, 35, 190, 60, 0), new("123", 520, 35, 100, 60, 0)], "en-US"));
                review.Show(); review.UpdateLayout(); SavePreview(review, "artifacts/capture-review-preview.png");
            }
            finally { review.Close(); }
            bool cancelled = false;
            var cancelReview = new CaptureReviewWindow(image, storage); cancelReview.Recognizer = async (_, token) => { using var registration = token.Register(() => cancelled = true); await Task.Delay(Timeout.Infinite, token); return new([], ""); };
            var pending = cancelReview.Recognize(); cancelReview.Close(); Wait(pending); check(cancelled, "Closing screenshot tools must cancel an in-flight OCR request.");
            if (integration)
            {
                Wait(WindowsOcr.Register(CancellationToken.None));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = Wait(WindowsOcr.Recognize(image, CancellationToken.None));
                var fixtureEncoder = new PngBitmapEncoder(); fixtureEncoder.Frames.Add(BitmapFrame.Create(image)); using (var fixtureFile = File.Create("artifacts/capture-ai-fixture.png")) fixtureEncoder.Save(fixtureFile);
                Console.WriteLine($"Windows OCR: {result.Language}; {result.Words.Length} words; cold {watch.ElapsedMilliseconds} ms.");
                check(result.Words.Any(w => w.Text.Contains("Quickfetch", StringComparison.OrdinalIgnoreCase)) && result.Words.All(w => w.X >= 0 && w.Y >= 0 && w.X + w.Width <= image.PixelWidth + 1 && w.Y + w.Height <= image.PixelHeight + 1), "Actual Windows OCR must recognize the printed fixture and return source-pixel coordinates.");
                check(string.Concat(result.Words.Select(w => w.Text)).Contains("中文"), "The Windows AI backend must recognize Chinese without selecting an English-only legacy language engine.");
                using (var session = new WindowsAiOcrSession())
                {
                    Wait(session.Recognize(image, CancellationToken.None)); var processId = session.ProcessId;
                    watch.Restart(); var repeated = Wait(session.Recognize(image, CancellationToken.None));
                    Console.WriteLine($"Windows AI OCR: warm {watch.ElapsedMilliseconds} ms; reused companion={session.ProcessId == processId}.");
                    check(processId is not null && session.ProcessId == processId && repeated.Words.Length == result.Words.Length, "Repeated OCR must reuse the active native recognizer instead of relaunching PowerShell or loading a new model.");
                    var large = new TransformedBitmap(image, new ScaleTransform(4, 4)); large.Freeze();
                    var scaled = Wait(session.Recognize(large, CancellationToken.None));
                    check(string.Concat(scaled.Words.Select(w => w.Text)).Contains("中文") && scaled.Words.All(w => w.X >= 0 && w.Y >= 0 && w.X + w.Width <= large.PixelWidth + 1 && w.Y + w.Height <= large.PixelHeight + 1), "Large captures must stay under the native pixel limit after WPF rounding and map recognized rectangles back to full source resolution.");
                    var blank = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgr32, null, new byte[20 * 20 * 4], 20 * 4); blank.Freeze();
                    check(Wait(session.Recognize(blank, CancellationToken.None)).Words.Length == 0, "A blank screenshot must return an empty result without a null-lines crash.");
                }
                var actualReview = new CaptureReviewWindow(image, storage) { Recognizer = (_, _) => Task.FromResult(result) };
                try { Wait(actualReview.Recognize()); actualReview.SelectAll(true); actualReview.Show(); actualReview.UpdateLayout(); SavePreview(actualReview, "artifacts/capture-review-preview.png"); }
                finally { actualReview.Close(); }
                var captured = Wait(Task.Run(ScreenCapture.Take));
                check(captured.Image.PixelWidth == captured.Desktop.Width && captured.Image.PixelHeight == captured.Desktop.Height && captured.Windows.All(w => w.Bounds.X >= 0 && w.Bounds.Y >= 0 && w.Bounds.X + w.Bounds.Width <= captured.Image.PixelWidth && w.Bounds.Y + w.Bounds.Height <= captured.Image.PixelHeight), "The native desktop capture and window enumeration must agree on physical pixel bounds.");
                using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool stopped = false;
                try { Wait(WindowsOcr.Recognize(image, cancel.Token)); } catch (OperationCanceledException) { stopped = true; }
                check(stopped, "Already cancelled OCR requests must not stay alive.");
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); foreach (var file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
    private static BitmapSource Fixture()
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1000, 360)); dc.DrawText(new FormattedText("Quickfetch Screen 123", System.Globalization.CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 52, Brushes.Black, 1), new Point(45, 35)); dc.DrawText(new FormattedText("中文识别测试，快速截图。", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 40, Brushes.Black, 1), new Point(45, 145)); }
        var result = new RenderTargetBitmap(1000, 360, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) }; var until = DateTime.UtcNow.AddSeconds(70);
            timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= until) frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
            if (!task.IsCompleted) throw new Exception("Screenshot test timed out.");
        }
        task.GetAwaiter().GetResult();
    }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    private static void SavePreview(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
