using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;
using Quickfetch.Core;
using Quickfetch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    private static void Run(string[] args)
    {
        var passed = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
        var app = new App { SuppressLauncherStartup = true };
        app.InitializeComponent();
        Check(AppContext.TryGetSwitch("Switch.System.Windows.Input.Stylus.DisableImplicitTouchKeyboardInvocation", out var disabled) && disabled, "WPF implicit system-keyboard invocation must be disabled in this application.");
        var window = new MainWindow();
        Check(GestureActionRunner.ParseShortcut("Ctrl + Alt + T").SequenceEqual(new ushort[] { 0x11, 0x12, 0x54 }), "Shortcut parser must preserve modifier/key order.");
        Check(GestureActionRunner.ParseShortcut("Win + S").SequenceEqual(new ushort[] { 0x5B, 0x53 }), "Win+S must be available as a launcher gesture.");
        Check(GestureActionRunner.ParseShortcut("F12").Single() == 0x7B, "Function keys must be supported without a modifier.");
        foreach (var invalidShortcut in new[] { "", "A", "Ctrl", "Ctrl + Ctrl + A", "Ctrl + A + B", "NoSuchKey", "A + Ctrl" })
        {
            bool rejected = false;
            try { GestureActionRunner.ParseShortcut(invalidShortcut); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid gesture shortcuts must be rejected: " + invalidShortcut);
        }
        var gestureDialog = new GestureSettingsWindow(new Quickfetch.Core.GestureSettings());
        Check(((CheckBox)gestureDialog.FindName("EnabledCheck")).IsChecked == true && ((CheckBox)gestureDialog.FindName("RightClickCheck")).IsChecked == false, "Gesture dialog must reflect global-on/right-click-opt-in defaults.");
        Check(((StackPanel)gestureDialog.FindName("Editors")).Children.Count == 4, "All four global edge gestures must have independent editors.");
        var invalidAction = new Quickfetch.Core.GestureAction { Kind = Quickfetch.Core.GestureActionKind.Program, Program = "missing.exe" };
        bool invalidProgram = false;
        try { GestureActionRunner.Validate(invalidAction); } catch (ArgumentException) { invalidProgram = true; }
        Check(invalidProgram, "Program actions must reject nonexistent or relative paths before saving.");
        Console.WriteLine(GlobalTouchGestures.ProbeDevices());
        Console.WriteLine($"Real touchscreen HID descriptors verified with in-memory contact packets: {GlobalTouchGestures.CheckNativeParser()}");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var ready = new ManualResetEventSlim();
            string nativeStatus = "";
            using (var listener = new GlobalTouchGestures(new Quickfetch.Core.GestureSettings(), (_, _, _) => { }, status => { nativeStatus = status; ready.Set(); }))
                Check(ready.Wait(TimeSpan.FromSeconds(5)) && nativeStatus.StartsWith("全局监听已开启", StringComparison.Ordinal), "Global touch registration must start successfully, including after disposal/restart: " + nativeStatus);
        }
        var gestureContent = (FrameworkElement)gestureDialog.Content;
        gestureContent.Measure(new Size(700, 700)); gestureContent.Arrange(new Rect(0, 0, 700, 700)); gestureContent.UpdateLayout();
        var gesturePreview = new RenderTargetBitmap(700, 700, 96, 96, PixelFormats.Pbgra32); gesturePreview.Render(gestureContent);
        var gestureEncoder = new PngBitmapEncoder(); gestureEncoder.Frames.Add(BitmapFrame.Create(gesturePreview));
        using (var imageFile = File.Create(Path.Combine(Path.GetTempPath(), "quickfetch-gesture-settings.png"))) gestureEncoder.Save(imageFile);
        gestureDialog.Close();
        var themeNames = new[] { "Ink", "Muted", "Surface", "Panel", "Accent", "Selection", "Line" };
        var originalTheme = themeNames.ToDictionary(name => name, name => app.Resources[name]);
        try
        {
            foreach (var (name, color) in new[] { ("Ink", "#EDF2FF"), ("Muted", "#ADB9CD"), ("Surface", "#10171C27"), ("Panel", "#28FFFFFF"), ("Accent", "#9CC7FF"), ("Selection", "#66567FAF"), ("Line", "#38FFFFFF") })
                app.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            var darkDialog = new GestureSettingsWindow(new GestureSettings());
            var display = (ComboBox)darkDialog.FindName("DisplayPicker");
            display.ApplyTemplate();
            Check(((SolidColorBrush)display.Background).Color.R < 80 && ((SolidColorBrush)display.Foreground).Color.R > 200, "Dark combo fields must use a dark background with light text.");
            var popup = (System.Windows.Controls.Primitives.Popup)display.Template.FindName("PART_Popup", display);
            var popupSurface = (Border)popup.Child;
            Check(((SolidColorBrush)popupSurface.Background).Color.R < 80, "Dark dropdown popups must not inherit a white system background.");
            Check(darkDialog.TryFindResource(typeof(System.Windows.Controls.Primitives.ScrollBar)) is Style, "Gesture settings and popups must share the modern scrollbar style.");
            var darkContent = (FrameworkElement)darkDialog.Content;
            darkContent.Measure(new Size(700, 700)); darkContent.Arrange(new Rect(0, 0, 700, 700)); darkContent.UpdateLayout();
            var darkPreview = new RenderTargetBitmap(700, 700, 96, 96, PixelFormats.Pbgra32);
            var baseLayer = new DrawingVisual(); using (var dc = baseLayer.RenderOpen()) dc.DrawRectangle(darkDialog.Background, null, new Rect(0, 0, 700, 700));
            darkPreview.Render(baseLayer); darkPreview.Render(darkContent);
            var darkEncoder = new PngBitmapEncoder(); darkEncoder.Frames.Add(BitmapFrame.Create(darkPreview));
            using (var imageFile = File.Create(Path.Combine(Path.GetTempPath(), "quickfetch-gesture-settings-dark.png"))) darkEncoder.Save(imageFile);
            popupSurface.Measure(new Size(650, 300)); popupSurface.Arrange(new Rect(0, 0, 650, 150)); popupSurface.UpdateLayout();
            var popupPreview = new RenderTargetBitmap(650, 150, 96, 96, PixelFormats.Pbgra32); popupPreview.Render(popupSurface);
            var popupEncoder = new PngBitmapEncoder(); popupEncoder.Frames.Add(BitmapFrame.Create(popupPreview));
            using (var imageFile = File.Create(Path.Combine(Path.GetTempPath(), "quickfetch-gesture-popup-dark.png"))) popupEncoder.Save(imageFile);
            darkDialog.Close();
        }
        finally { foreach (var (name, resource) in originalTheme) app.Resources[name] = resource; }
        var keyboard = (TouchKeyboard)window.FindName("QuickKeyboard");
        var query = (TextBox)window.FindName("Query");
        var toggle = (Button)window.FindName("KeyboardToggle");
        var grid = (Grid)keyboard.Child;
        var keys = grid.Children.OfType<Button>().ToArray();
        void Key(string value) => keys.Single(key => (string)key.Tag == value).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(keys.Length == 45 && keys.All(key => !key.Focusable), "All touch keys must preserve non-edit focus.");
        string Row(int row) => string.Concat(keys.Where(key => Grid.GetRow(key) == row && ((string)key.Tag).Length == 1).Select(key => (string)key.Tag));
        Check(Row(0) == "1234567890" && Row(1) == "qwertyuiop" && Row(2) == "asdfghjkl" && Row(3) == "zxcvbnm", "Keyboard rows must use the standard digit/QWERTY layout.");
        Check(Grid.GetColumn(keys.Single(key => (string)key.Tag == "a")) == 1 && Grid.GetColumn(keys.Single(key => (string)key.Tag == "z")) == 2, "Home and bottom rows must be staggered by half/full keys.");
        var enter = keys.Single(key => (string)key.Tag == "enter");
        Check(Grid.GetColumn(enter) == 20 && Grid.GetRow(enter) == 0 && Grid.GetRowSpan(enter) == 5, "Enter must occupy the entire independent right side.");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(keyboard.Visibility == Visibility.Visible && query.IsReadOnly, "Touch mode must show the keyboard and use a read-only edit surface.");
        Check(!query.Focusable && !query.IsTabStop, "The touch query must not accept edit focus or request the system keyboard.");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(window), window.FindName("LauncherSurface")), "Touch focus must remain on the non-edit launcher surface.");
        Key("v"); Key("s"); Key("c");
        Check(query.Text == "vsc" && query.CaretIndex == 3, "Touch keys must update the query and caret.");
        query.Select(1, 2); Key("2");
        Check(query.Text == "v2", "Touch input must replace selected text.");
        Key("backspace");
        Check(query.Text == "v", "Touch backspace must update the query.");
        Key("clear");
        Check(query.Text.Length == 0, "Clear must empty the search box.");
        Key("hide");
        Check(keyboard.Visibility == Visibility.Collapsed, "Hiding the compact keyboard must collapse it without forcing edit focus.");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var nativeHandle = new WindowInteropHelper(window).EnsureHandle();
        Console.WriteLine("HWND background after creation: " + HwndSource.FromHwnd(nativeHandle)?.CompositionTarget.BackgroundColor);
        Check(window.BackdropMode is "Acrylic" or "SystemAcrylic", "The backdrop must initialize or select its supported fallback.");
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(nativeHandle), 0, System.Windows.Input.Key.A) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(input);
        Check(keyboard.Visibility == Visibility.Collapsed && !query.IsReadOnly, "Physical keyboard input must restore editing and collapse the compact keyboard.");
        Check(query.Focusable && query.IsTabStop, "Physical keyboard mode must restore normal edit focus.");
        Check(HwndSource.FromHwnd(nativeHandle)?.CompositionTarget.BackgroundColor.A == 0, "The HWND must retain an alpha-zero background in non-touch mode.");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        // Render only the controls offscreen to inspect layout; Acrylic itself needs the desktop compositor.
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(660, 750));
        content.Arrange(new Rect(0, 0, 660, 750));
        content.UpdateLayout();
        Check(keys.Where(key => key.Visibility == Visibility.Visible).All(key => key.ActualWidth >= 40 && key.ActualHeight >= 40), "Every visible key must retain a usable touch target.");
        Check(new[] { "FolderButton", "KeyboardToggle", "SettingsButton", "HideButton" }.Select(name => (Button)window.FindName(name)).All(button => button.ActualWidth >= 48 && button.ActualHeight >= 48), "Header actions must have at least 48-DIP touch targets.");
        var preview = new RenderTargetBitmap(660, 750, 96, 96, PixelFormats.Pbgra32);
        preview.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(preview));
        var output = Path.Combine(Path.GetTempPath(), "quickfetch-touch-layout.png");
        using (var file = File.Create(output)) encoder.Save(file);
        Console.WriteLine("Layout preview: " + output);
        var mode = (Button)window.FindName("ModeButton");
        window.EverythingAvailable = () => false;
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((Button)window.FindName("InstallEverythingButton")).Visibility == Visibility.Visible && ((TextBlock)window.FindName("Status")).Text == "文件搜索不可用", "Missing Everything must immediately show unavailable status and the install action.");
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.EverythingAvailable = () => EverythingClient.IsAvailable;
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((FrameworkElement)window.FindName("FileSortHeader")).Visibility == Visibility.Visible && window.Width > 660, "File mode must show sortable metadata columns and expand the window.");
        Check(keys.Single(key => (string)key.Tag == "directory").Visibility == Visibility.Visible && Grid.GetRowSpan(enter) == 6, "File mode must expose the directory key and retain a full-height Enter key.");
        query.Text = "文件名 .txt";
        Check(query.Text == "文件名 .txt", "File mode must retain Unicode, spaces and punctuation.");
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((FrameworkElement)window.FindName("FileSortHeader")).Visibility == Visibility.Collapsed, "Switching back must restore app mode.");
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(query.Text == "文件名 .txt", "Each mode must retain its own query.");
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        // Show this test window briefly to verify unfocused text geometry and custom caret.
        var choosing = typeof(MainWindow).GetField("_choosingFolder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        choosing.SetValue(window, true);
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var caret = (Canvas)window.FindName("TouchCaret");
        var caretLine = (System.Windows.Shapes.Line)window.FindName("TouchCaretLine");
        Check(caret.Visibility == Visibility.Visible && !query.IsKeyboardFocused, "The touch caret must be visible without focusing an edit control.");
        query.Text = "abcdef"; query.Select(2, 0);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var middle = Canvas.GetLeft(caretLine);
        query.Select(6, 0);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(Canvas.GetLeft(caretLine) > middle, "The custom caret must follow the actual insertion position.");
        window.Hide(); choosing.SetValue(window, false);
        query.Text = "";
        if (args.Contains("--integration"))
        {
            using var client = new EverythingClient(nativeHandle);
            IntPtr Reply(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            { if (client.HandleMessage(message, lParam)) { handled = true; return new IntPtr(1); } return IntPtr.Zero; }
            var source = HwndSource.FromHwnd(nativeHandle);
            source.AddHook(Reply);
            T Await<T>(Task<T> task)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
                if (!task.IsCompleted) System.Windows.Threading.Dispatcher.PushFrame(frame);
                return task.GetAwaiter().GetResult();
            }
            try
            {
                var page = Await(client.SearchAsync("\"Quickfetch.csproj\"", 1, 100, CancellationToken.None));
                Check(page.Entries.Any(file => file.Path.Equals(Path.GetFullPath(Path.Combine("Quickfetch", "Quickfetch.csproj")), StringComparison.OrdinalIgnoreCase)), "Live Everything IPC must find the project's real file.");
                Console.WriteLine(string.Join(Environment.NewLine, page.Entries.Take(5).Select(file => $"Metadata: {file.Name}; folder={file.IsFolder}; size={file.Size}; modified={file.Modified}")));
                Check(page.Entries.Any(file => !file.IsFolder && file.Size > 0 && file.Modified is not null), "Live Everything results must include size and modified date.");
                var bySize = Await(client.SearchAsync("\"Quickfetch\"", 6, 30, CancellationToken.None));
                Check(bySize.Sort == 6, "Everything must apply server-side size sorting.");
                Console.WriteLine($"Everything IPC: connected to {client.Instance}; metadata and server-side sorting verified.");
            }
            finally { source.RemoveHook(Reply); }
            var results = (ListBox)window.FindName("Results");
            void WaitForFileRows()
            {
                var ready = new TaskCompletionSource<bool>();
                var started = System.Diagnostics.Stopwatch.StartNew();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += (_, _) =>
                {
                    if (results.Items.OfType<FileItem>().Any()) { timer.Stop(); ready.TrySetResult(true); }
                    else if (started.Elapsed > TimeSpan.FromSeconds(18)) { timer.Stop(); ready.TrySetException(new TimeoutException("File-mode UI query did not produce results.")); }
                };
                timer.Start(); Await(ready.Task);
            }
            mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            query.Text = "\"Quickfetch.csproj\"";
            WaitForFileRows();
            Check(results.Items.OfType<FileItem>().Any(item => item.Path.EndsWith("Quickfetch.csproj", StringComparison.OrdinalIgnoreCase)), "The actual file-mode UI pipeline must display live Everything results.");
            ((Button)window.FindName("SizeSort")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForFileRows();
            Check(((Button)window.FindName("SizeSort")).Content.ToString() == "大小 ↓", "Clicking the Size header must apply descending sorting in the UI.");
            var fileContent = (FrameworkElement)window.Content;
            fileContent.Measure(new Size(980, 750)); fileContent.Arrange(new Rect(0, 0, 980, 750)); fileContent.UpdateLayout();
            var filePreview = new RenderTargetBitmap(980, 750, 96, 96, PixelFormats.Pbgra32); filePreview.Render(fileContent);
            var fileEncoder = new PngBitmapEncoder(); fileEncoder.Frames.Add(BitmapFrame.Create(filePreview));
            using (var imageFile = File.Create(Path.Combine(Path.GetTempPath(), "quickfetch-file-layout.png"))) fileEncoder.Save(imageFile);
            mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var fixture = Path.Combine(Path.GetTempPath(), "QuickfetchIntegration-" + Guid.NewGuid());
            Directory.CreateDirectory(fixture);
            try
            {
                var file = Path.Combine(fixture, "sample.txt"); File.WriteAllText(file, "Quickfetch native menu fixture");
                Check(ShellContextMenu.GetItemCount(nativeHandle, file) > 0, "A real Shell file menu must be created without invoking commands.");
                Check(ShellContextMenu.GetItemCount(nativeHandle, fixture) > 0, "A real Shell folder menu must be created.");
                var historyFile = Path.Combine(fixture, "usage.json");
                new UsageStore(historyFile).Record("sample.lnk");
                Check(new UsageStore(historyFile).History["sample.lnk"].Count == 1, "Launch history must survive a reload.");
            }
            finally { Directory.Delete(fixture, true); }
        }
        if (args.Contains("--live"))
        {
            Key("hide");
            window.ShowInTaskbar = true;
            typeof(MainWindow).GetField("_choosingFolder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
            window.Title = "Quickfetch material preview";
            window.Show();
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        window.DisposeLauncher();
        window.Close();
        app.Shutdown();
        Console.WriteLine($"PASS: {passed} UI keyboard checks (no desktop input injected)");
    }
}
