using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Quickfetch;
using Quickfetch.Core;

internal static class LauncherInputChecks
{
    internal static void Run(Action<bool, string> check, bool startMenu)
    {
        var main = new MainWindow(new Settings { UseBuiltInKeyboard = true }, _ => { });
        try
        {
            main.ShowLauncher(); Pump(160);
            var own = new WindowInteropHelper(main).Handle;
            Console.WriteLine($"Initial activation: visible={main.IsVisible}; active={main.IsActive}; foreground={LauncherActivation.GetForegroundWindow()}; own={own}; start={LauncherActivation.IsStartMenu(LauncherActivation.GetForegroundWindow())}.");
            check(main.IsVisible && main.Topmost && LauncherActivation.GetForegroundWindow() == own, "Showing the launcher must raise the actual native window and acquire the foreground.");
            var query = (TextBox)main.FindName("Query");
            var keyboard = (TouchKeyboard)main.FindName("QuickKeyboard");
            var rows = (ListBox)main.FindName("Results");
            var surface = (FrameworkElement)main.FindName("LauncherSurface");
            var keys = ((Grid)keyboard.Child).Children.OfType<Button>().ToArray();
            var mode = (Button)main.FindName("ModeButton");
            main.EverythingAvailable = () => false;
            foreach (var fileMode in new[] { false, true })
            {
                if (fileMode) mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                main.SetKeyboardPreference(true, showKeyboard: true);
                var items = Enumerable.Range(0, 3).Select(i => new FileItem(new FileEntry("Missing test item " + i, "C:\\Quickfetch-test-missing", "C:\\Quickfetch-test-missing\\item-" + i, false, 0, DateTimeOffset.Now))).ToArray();
                rows.ItemsSource = items; rows.SelectedIndex = 0; ((UIElement)main.FindName("EmptyMessage")).Visibility = Visibility.Collapsed;
                main.UpdateLayout();
                var row = (ListBoxItem)rows.ItemContainerGenerator.ContainerFromIndex(1);
                check(row is not null, "Test rows must be realized in app and file layouts.");
                var point = row!.TranslatePoint(new Point(25, row.ActualHeight / 2), main);
                var touch = new TestTouch(main, fileMode ? 502 : 501);
                touch.Down(point); touch.Up(point);
                check(ReferenceEquals(rows.SelectedItem, items[1]), "One finger tap must select exactly the tapped result in both modes.");
                check(!rows.TouchesCaptured.Any() && Mouse.Captured is null, "Completing a row tap must release both touch and promoted mouse capture before another control is pressed.");
                check(ReferenceEquals(Keyboard.FocusedElement, surface) && !query.IsKeyboardFocused, "Built-in touch input must restore non-edit focus after selecting a result.");
                // Missing paths intentionally exercise the real launch attempt without opening an external application.
                var status = (TextBlock)main.FindName("Status"); status.Text = "Awaiting launch";
                var scroll = FindScroll(rows); var offset = scroll?.VerticalOffset;
                keys.Single(key => key.Tag as string == "enter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(rows.SelectedIndex == 1 && status.Text.StartsWith("无法启动：") && scroll?.VerticalOffset == offset, "The on-screen Enter key must attempt to launch the selected row, without moving selection or scrolling the result list.");
                row.CaptureMouse(); main.SelectTouchedResult(items[2]);
                check(Mouse.Captured is null && rows.SelectedIndex == 2, "Selecting a touch result must also clear an existing legacy ListBox mouse capture.");
            }
            var extensions = (Button)main.FindName("ExtensionsButton");
            extensions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(30);
            check(extensions.ContextMenu.IsOpen, "The extension button must open its panel on the first click.");
            var buttonPoint = extensions.TranslatePoint(new Point(extensions.ActualWidth / 2, extensions.ActualHeight / 2), main);
            var dismissTouch = new TestTouch(main, 503); dismissTouch.Down(buttonPoint); dismissTouch.Up(buttonPoint); Pump(30);
            check(!extensions.ContextMenu.IsOpen && main.IsVisible, "Tapping the extension button with its popup open must dismiss it without reopening or hiding the launcher.");
            extensions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(30);
            check(extensions.ContextMenu.IsOpen, "A new intentional click must reopen extensions immediately after touch dismissal.");
            extensions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!extensions.ContextMenu.IsOpen, "Repeated button clicks must toggle the extension panel closed.");
            main.Hide(); main.ShowLauncher(); Pump(160);
            check(main.IsVisible && LauncherActivation.GetForegroundWindow() == own, "The same native window must regain the foreground when reopened.");
            if (startMenu)
            {
                main.Hide();
                var input = new[] { new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = 0x5B } } }, new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = 0x5B, Flags = 2 } } } };
                check(SendInput(2, input, Marshal.SizeOf<Input>()) == 2, "The explicit Start-menu integration test must be able to open Start.");
                var until = DateTime.UtcNow.AddSeconds(4);
                while (!LauncherActivation.IsStartMenu(LauncherActivation.GetForegroundWindow()) && DateTime.UtcNow < until) Pump(30);
                check(LauncherActivation.IsStartMenu(LauncherActivation.GetForegroundWindow()), "The real Windows Start menu must be foreground before testing launcher activation.");
                main.SetKeyboardPreference(false, showKeyboard: false);
                main.ShowLauncher(); Pump(250);
                Console.WriteLine($"Start activation: visible={main.IsVisible}; active={main.IsActive}; foreground={LauncherActivation.GetForegroundWindow()}; own={own}; start={LauncherActivation.IsStartMenu(LauncherActivation.GetForegroundWindow())}; queryFocus={query.IsKeyboardFocused}; focused={Keyboard.FocusedElement?.GetType().Name}.");
                check(main.IsVisible && LauncherActivation.GetForegroundWindow() == own && query.IsKeyboardFocused, "Launching over the real Start menu must restore native foreground and editable query focus.");
                Console.WriteLine("Real Start menu: Quickfetch regained foreground and query focus.");
            }
        }
        finally { Mouse.Capture(null); main.DisposeLauncher(); main.Close(); }
    }
    private static ScrollViewer? FindScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) if (FindScroll(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private sealed class TestTouch(Window root, int id) : TouchDevice(id)
    {
        private Point _position;
        private TouchAction _action;
        public override TouchPoint GetTouchPoint(IInputElement? relativeTo)
        {
            var point = relativeTo is Visual visual ? root.TransformToVisual(visual).Transform(_position) : _position;
            return new(this, point, new Rect(point, new Size(1, 1)), _action);
        }
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo) => new() { GetTouchPoint(relativeTo) };
        internal void Down(Point point) { _position = point; _action = TouchAction.Down; SetActiveSource(PresentationSource.FromVisual(root)); Activate(); ReportDown(); }
        internal void Up(Point point) { _position = point; _action = TouchAction.Up; ReportUp(); Deactivate(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
}
