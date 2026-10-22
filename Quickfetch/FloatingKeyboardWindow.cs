using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Quickfetch;

internal sealed class FloatingKeyboardWindow : Window
{
    internal TouchKeyboard Keyboard { get; } = new();
    private readonly TextBlock _target = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private Point? _dragStart;
    private double _startLeft, _startTop;
    internal FloatingKeyboardWindow(Action<string> keyPressed)
    {
        Title = Loc.T("Quickfetch · 悬浮键盘"); Width = Math.Min(780, SystemParameters.WorkArea.Width - 24); Height = 376;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Focusable = false; Background = Brushes.Transparent; UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "Ink");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(10) });
        var grid = new Grid { Margin = new Thickness(6) }; grid.RowDefinitions.Add(new() { Height = new GridLength(36) }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var surface = new Border { Child = grid, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface"); surface.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = surface;
        var header = new Grid { Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = new GridLength(40) });
        var handle = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Padding = new Thickness(10, 0, 0, 0), Child = _target, ToolTip = Loc.T("拖动此处移动键盘") }; header.Children.Add(handle);
        var hide = new Button { Content = "−", Focusable = false, IsTabStop = false, Padding = new Thickness(0), ToolTip = Loc.T("收起悬浮键盘") }; Grid.SetColumn(hide, 1); header.Children.Add(hide); hide.Click += (_, _) => keyPressed("hide");
        handle.MouseLeftButtonDown += (_, e) => { if (e.StylusDevice is not null) return; BeginDrag(PointToScreen(e.GetPosition(this))); handle.CaptureMouse(); e.Handled = true; };
        handle.MouseMove += (_, e) => { if (handle.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) DragTo(PointToScreen(e.GetPosition(this))); };
        handle.MouseLeftButtonUp += (_, e) => { _dragStart = null; handle.ReleaseMouseCapture(); e.Handled = true; };
        handle.TouchDown += (_, e) => { BeginDrag(PointToScreen(e.GetTouchPoint(this).Position)); handle.CaptureTouch(e.TouchDevice); e.Handled = true; };
        handle.TouchMove += (_, e) => { if (e.TouchDevice.Captured == handle) DragTo(PointToScreen(e.GetTouchPoint(this).Position)); e.Handled = true; };
        handle.TouchUp += (_, e) => { _dragStart = null; handle.ReleaseTouchCapture(e.TouchDevice); e.Handled = true; };
        handle.LostMouseCapture += (_, _) => _dragStart = null; handle.LostTouchCapture += (_, _) => _dragStart = null;
        grid.Children.Add(header); Grid.SetRow(Keyboard, 1); grid.Children.Add(Keyboard); Keyboard.KeyPressed += keyPressed;
        Loaded += (_, _) => WindowBackdrop.Apply(new WindowInteropHelper(this).Handle);
    }
    internal void SetTarget(bool composer) { _target.Text = composer ? Loc.T("⠿  悬浮键盘 · 编辑下方文本") : Loc.T("⠿  悬浮键盘 · 搜索字符 / 转换文字"); Keyboard.SetCharacterMode(composer); }
    internal void BeginDrag(Point screen) { _dragStart = screen; _startLeft = Left; _startTop = Top; }
    internal void DragTo(Point screen)
    {
        if (_dragStart is not Point start) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = Math.Clamp(_startLeft + (screen.X - start.X) / dpi.DpiScaleX, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Clamp(_startTop + (screen.Y - start.Y) / dpi.DpiScaleY, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); var handle = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080));
        WindowBackdrop.Apply(handle);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
