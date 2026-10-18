using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Quickfetch;
using Quickfetch.Core;

internal static class FileUiChecks
{
    internal static void Run(MainWindow window, Action<bool, string> check, bool nativeIntegration)
    {
        var query = (TextBox)window.FindName("Query");
        var mode = (Button)window.FindName("ModeButton");
        var settings = (FrameworkElement)window.FindName("SettingsPanel");
        check(((Button)window.FindName("SettingsButton")).Content is TextBlock && settings.IsAncestorOf((Button)window.FindName("FolderButton")), "Settings must remain icon-only, and folder opening must be available only inside its panel.");
        query.Text = "quick";
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(query.Text == "quick" && Math.Abs(window.Width - 784) < .1, "Application-to-file switching must preserve the query and use a 20% narrower file window.");
        query.Text = "other";
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(query.Text == "" && Math.Abs(window.Width - 660) < .1, "Returning to apps must clear the query and restore the app width.");
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var rows = (ListBox)window.FindName("Results");
        rows.ItemsSource = new[] { new FileItem(new FileEntry("Design notes.pdf", "C:\\Example", "C:\\Example\\Design notes.pdf", false, 81920, DateTimeOffset.Now)), new FileItem(new FileEntry("Preview.png", "C:\\Example", "C:\\Example\\Preview.png", false, 245760, DateTimeOffset.Now)) };
        ((UIElement)window.FindName("EmptyMessage")).Visibility = Visibility.Collapsed;
        var choosing = typeof(MainWindow).GetField("_choosingFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        choosing.SetValue(window, true);
        window.Show(); window.UpdateLayout();
        var header = (Grid)window.FindName("FileSortHeader");
        var before = header.ColumnDefinitions.Select(c => c.ActualWidth).ToArray();
        window.ResizeFileColumns(0, 30); window.UpdateLayout();
        check(Math.Abs(header.ColumnDefinitions[0].ActualWidth - before[0] - 30) < 1 && Math.Abs(header.ColumnDefinitions[1].ActualWidth - before[1] + 30) < 1, "Resizing must transfer space only between adjacent columns.");
        window.ResizeFileColumns(2, 10000); window.UpdateLayout();
        check(header.ColumnDefinitions[3].ActualWidth >= 107, "A wide drag must preserve a usable date column.");
        var grips = header.Children.OfType<System.Windows.Controls.Primitives.Thumb>().ToArray();
        check(grips.Length == 3 && grips.All(g => g.Width >= 20 && g.ActualHeight >= 44), "All column separators must provide generous touch hit targets.");
        check(rows.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem, "File rows must remain realized after width changes.");
        var original = Path.GetFullPath("Quickfetch/Quickfetch.csproj");
        var data = MainWindow.CreateFileDragData(original);
        check(data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths && paths.Single() == original, "Cross-application drags must carry standard original-file paths.");
        var root = (FrameworkElement)window.Content;
        Save(root, "file-ui-preview.png", (int)window.Width, (int)window.Height);
        var flyout = new FileFlyout("Design notes.pdf");
        var flyoutRoot = (FrameworkElement)flyout.Content;
        flyoutRoot.Measure(new Size(294, double.PositiveInfinity));
        var flyoutHeight = (int)Math.Ceiling(flyoutRoot.DesiredSize.Height);
        flyoutRoot.Arrange(new Rect(0, 0, 294, flyoutHeight)); flyoutRoot.UpdateLayout();
        Save(flyoutRoot, "file-flyout-preview.png", 294, flyoutHeight);
        check(flyoutHeight >= 280 && flyoutHeight <= 400, "The Fluent file menu must fit its touch-friendly actions without clipping.");
        flyout.Owner = window;
        flyout.Loaded += (_, _) => flyout.Dispatcher.BeginInvoke(() =>
        {
            check(HwndSource.FromHwnd(new WindowInteropHelper(flyout).Handle)?.CompositionTarget.BackgroundColor.A == 0, "The actual file flyout HWND must preserve the Acrylic background.");
            flyout.Close();
        });
        flyout.ShowDialog();
        var testPoint = window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2));
        if (nativeIntegration) using (var dragVisuals = new FileDragVisuals(window, testPoint))
        {
            dragVisuals.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var hit = WindowFromPoint(new NativePoint { X = (int)testPoint.X, Y = (int)testPoint.Y });
            check(GetAncestor(hit, 2) == new WindowInteropHelper(window).Handle, "The actual blurred drag ghost must pass native hit testing through to the underlying window.");
            GetWindowRect(dragVisuals.CancelHandle, out var rect);
            hit = WindowFromPoint(new NativePoint { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 });
            check(GetAncestor(hit, 2) == dragVisuals.CancelHandle, "The top cancel strip must remain a native hit target while the ghost passes through.");
        }
        settings.Visibility = Visibility.Visible; window.Hide(); window.ShowLauncher();
        check(settings.Visibility == Visibility.Collapsed, "Every reopen after hiding must collapse settings.");
        window.Hide(); mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); query.Text = "";
        choosing.SetValue(window, false);
    }
    private static void Save(FrameworkElement root, string name, int width, int height)
    {
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory("artifacts"); using var stream = File.Create(Path.Combine("artifacts", name)); encoder.Save(stream);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
}
