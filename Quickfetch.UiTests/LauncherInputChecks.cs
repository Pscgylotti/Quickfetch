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
    internal static void CheckWindowGrip(Action<bool, string> check, bool nativeMouse = false)
    {
        var settings = new Settings();
        int saves = 0;
        var main = new MainWindow(settings, _ => saves++);
        try
        {
            main.ShowLauncher(); Pump(320);
            Point Grip() => main.WindowGrip.TranslatePoint(new Point(main.WindowGrip.ActualWidth / 2, main.WindowGrip.ActualHeight / 2), main);
            Point Origin() => main.PointToScreen(new Point());
            var centered = Origin();
            var finger = new TestTouch(main, 3101);
            finger.Down(Grip()); finger.Move(Grip() + new Vector(30, 0)); finger.Up(Grip() + new Vector(30, 0));
            check((Origin() - centered).Length <= 1 && saves == 0, "An unheld swipe on the grip must not move or save the window.");
            finger.Down(Grip()); Pump(390);
            var destination = main.PointToScreen(Grip() + new Vector(-70, 40));
            finger.Move(main.PointFromScreen(destination)); Pump(40);
            finger.Up(main.PointFromScreen(destination)); Pump(50);
            var moved = Origin();
            check((moved - centered).Length > 25 && saves == 1 && settings.WindowPlacement != null,
                "Long-press dragging must move the launcher and persist only on release.");
            main.Hide(); main.ShowLauncher(); Pump(320);
            check((Origin() - moved).Length <= 2, "Reopening the launcher must restore the dragged position.");
            var restored = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            var second = new MainWindow(restored, _ => { });
            main.Hide();
            try
            {
                second.ShowLauncher(); Pump(320);
                check((second.PointToScreen(new Point()) - moved).Length <= 2, "A new launcher instance must read the saved position.");
            }
            finally { second.DisposeLauncher(); second.Close(); }
            main.ShowLauncher(); Pump(320);
            finger.Down(Grip()); finger.Up(Grip()); Pump(55); finger.Down(Grip()); finger.Up(Grip()); Pump(40);
            check((Origin() - centered).Length <= 2 && settings.WindowPlacement is { Horizontal: .5, Vertical: .5 },
                "Double-tapping the grip must reset the saved position to the monitor center.");
            if (nativeMouse)
            {
                var originalCursor = System.Windows.Forms.Cursor.Position;
                var down = false;
                void Press(bool pressed)
                {
                    var input = new Input { Data = new InputData { Mouse = new MouseInput { Flags = pressed ? 2u : 4u } } };
                    if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    down = pressed; Pump(25);
                }
                try
                {
                    var grip = main.PointToScreen(Grip());
                    var cursor = new System.Drawing.Point((int)grip.X, (int)grip.Y);
                    var own = new WindowInteropHelper(main).Handle;
                    check(LauncherActivation.Activate(main) && WindowFromPoint(new NativePoint { X = cursor.X, Y = cursor.Y }) == own,
                        "Native mouse input must target the test launcher.");
                    System.Windows.Forms.Cursor.Position = cursor; Pump(35); Press(true);
                    cursor.Offset(-90, 60); System.Windows.Forms.Cursor.Position = cursor; Pump(70); Press(false);
                    check((Origin() - centered).Length > 35, "Dragging the grip with the mouse must move the window.");
                    Pump(System.Windows.Forms.SystemInformation.DoubleClickTime + 40);
                    check(WindowFromPoint(new NativePoint { X = cursor.X, Y = cursor.Y }) == own, "The grip must still be under the cursor.");
                    Press(true); Press(false); Press(true); Press(false);
                    check((Origin() - centered).Length <= 2, "Native mouse double-click must recenter the launcher.");
                }
                finally { if (down) Press(false); System.Windows.Forms.Cursor.Position = originalCursor; }
            }
        }
        finally { main.DisposeLauncher(); main.Close(); }
    }
    internal static void Run(Action<bool, string> check, bool startMenu, bool nativeTouch = false)
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
                ((UIElement)main.FindName("InstallEverythingButton")).Visibility = Visibility.Collapsed;
                main.UpdateLayout();
                var row = (ListBoxItem)rows.ItemContainerGenerator.ContainerFromIndex(1);
                check(row is not null, "Test rows must be realized in app and file layouts.");
                var point = row!.TranslatePoint(new Point(84, row.ActualHeight / 2), main);
                var touch = new TestTouch(main, fileMode ? 502 : 501);
                touch.Down(point); touch.Up(point);
                check(ReferenceEquals(rows.SelectedItem, items[1]), $"One finger tap must select the row body (file mode: {fileMode}, selected: {rows.SelectedIndex}).");
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
            var panel = (Border)main.FindName("ExtensionsPanel");
            var buttonPoint = extensions.TranslatePoint(new Point(extensions.ActualWidth / 2, extensions.ActualHeight / 2), main);
            if (nativeTouch)
            {
                if (!InitializeTouchInjection(1, 3)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Touch initialization failed.");
                var changes = 0;
                panel.IsVisibleChanged += CountPanelChange;
                void CountPanelChange(object sender, DependencyPropertyChangedEventArgs e) => changes++;
                try
                {
                    for (int i = 1; i <= 4; i++)
                    {
                        var screen = main.PointToScreen(buttonPoint);
                        var point = new NativePoint { X = (int)screen.X, Y = (int)screen.Y };
                        if (WindowFromPoint(point) != own || LauncherActivation.GetForegroundWindow() != own)
                            throw new InvalidOperationException("The test window must own the touch target.");
                        var contact = new NativeTouch
                        {
                            Pointer = new() { Type = 2, Id = 0, Pixel = point, Flags = 0x10006 },
                            Mask = 7, Orientation = 90, Pressure = 512,
                            Contact = new() { Left = point.X - 2, Top = point.Y - 2, Right = point.X + 2, Bottom = point.Y + 2 }
                        };
                        if (!InjectTouchInput(1, ref contact)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Touch frame {contact.Pointer.Flags:X} failed at {point.X},{point.Y} ({Marshal.SizeOf<NativeTouch>()} bytes).");
                        try
                        {
                            Pump(30);
                            contact.Pointer.Flags = 0x20006;
                            if (!InjectTouchInput(1, ref contact)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Touch update failed.");
                            Pump(20);
                        }
                        finally
                        {
                            contact.Pointer.Flags = 0x40000;
                            if (!InjectTouchInput(1, ref contact)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Touch frame {contact.Pointer.Flags:X} failed at {point.X},{point.Y} ({Marshal.SizeOf<NativeTouch>()} bytes).");
                        }
                        Pump(200);
                        check(changes == i && panel.IsVisible == (i % 2 == 1),
                            $"System touch {i} must produce exactly one visibility change (observed {changes}).");
                    }
                }
                finally { panel.IsVisibleChanged -= CountPanelChange; }
            }
            var tap = new TestTouch(main, 503);
            for (var i = 0; i < 4; i++)
            {
                bool previouslyOpen = panel.IsVisible;
                tap.Down(buttonPoint); tap.Up(buttonPoint); Pump(15);
                check(panel.IsVisible != previouslyOpen && main.IsVisible, "Each touch release must toggle the extensions menu exactly once.");
            }
            extensions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(15);
            var outside = query.TranslatePoint(new Point(6, 5), main);
            tap.Down(outside); tap.Up(outside); Pump(15);
            check(!panel.IsVisible && main.IsVisible, "Tapping outside the panel should dismiss it.");
            extensions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(15);
            main.Hide(); main.ShowLauncher(); Pump(160);
            check(main.IsVisible && LauncherActivation.GetForegroundWindow() == own, "The same native window must regain the foreground when reopened.");
            check(!panel.IsVisible, "Reopening the launcher must start with the extensions panel closed.");
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
    internal sealed class TestTouch(Window root, int id) : TouchDevice(id)
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
        internal void Move(Point point) { _position = point; _action = TouchAction.Move; ReportMove(); }
        internal void Up(Point point) { _position = point; _action = TouchAction.Up; ReportUp(); Deactivate(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointerInfo
    {
        public uint Type, Id, Frame, Flags;
        public IntPtr Source, Target;
        public NativePoint Pixel, Himetric, RawPixel, RawHimetric;
        public uint Time, History;
        public int Data;
        public uint Keys;
        public ulong PerformanceCount;
        public uint ButtonChange;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeTouch
    {
        public PointerInfo Pointer;
        public uint Flags, Mask;
        public NativeRect Contact, RawContact;
        public uint Orientation, Pressure;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool InitializeTouchInjection(uint contacts, uint feedback);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool InjectTouchInput(uint count, ref NativeTouch contact);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
}
