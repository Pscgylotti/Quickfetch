using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Quickfetch;

internal sealed class CaptureOverlay : Window
{
    private readonly ScreenCapture _screen;
    private readonly CaptureStorage _storage;
    private readonly CaptureSurface _surface;
    private readonly Canvas _tools = new() { IsHitTestVisible = true };
    private readonly Border _actions;
    private readonly TextBlock _status = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    private readonly Action<BitmapSource> _copy;
    private Point? _start;
    private bool _windowMode;
    internal Int32Rect Selection { get; private set; }
    internal BitmapSource? ReviewImage { get; private set; }
    internal CaptureOverlay(ScreenCapture screen, CaptureStorage storage, Action<BitmapSource>? copy = null)
    {
        _screen = screen; _storage = storage; _copy = copy ?? Clipboard.SetImage;
        Title = Loc.T("Quickfetch · 截图"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual; Background = Brushes.Black;
        CaptureUi.Theme(this); Cursor = Cursors.Cross; Focusable = true;
        var root = new Grid(); _surface = new CaptureSurface(screen.Image); root.Children.Add(_surface); root.Children.Add(_tools); Content = root;
        var modes = new WrapPanel();
        modes.Children.Add(CaptureUi.Button("框选", () => Reselect(false)));
        modes.Children.Add(CaptureUi.Button("选择窗口", () => Reselect(true)));
        modes.Children.Add(CaptureUi.Button("全屏", () => Select(new(0, 0, screen.Image.PixelWidth, screen.Image.PixelHeight))));
        modes.Children.Add(CaptureUi.Button("取消", Close)); modes.Children.Add(_status);
        var modeBar = CaptureUi.Panel(modes); _tools.Children.Add(modeBar);
        var actions = new WrapPanel();
        actions.Children.Add(CaptureUi.Button("取消选中", () => Reselect(_windowMode)));
        actions.Children.Add(CaptureUi.Button("复制截图", Copy));
        actions.Children.Add(CaptureUi.Button("另存为", () => Save(true)));
        actions.Children.Add(CaptureUi.Button("保存到上次目录", () => Save(false)));
        actions.Children.Add(CaptureUi.Button("更多操作 · OCR", () => { if (!Selection.IsEmpty) { ReviewImage = _screen.Crop(Selection); Close(); } }));
        _actions = CaptureUi.Panel(actions); _actions.Visibility = Visibility.Collapsed; _tools.Children.Add(_actions);
        _surface.MouseLeftButtonDown += (_, e) => { if (e.StylusDevice is null) { Begin(e.GetPosition(_surface)); _surface.CaptureMouse(); e.Handled = true; } };
        _surface.MouseMove += (_, e) => { if (e.StylusDevice is null) Move(e.GetPosition(_surface)); };
        _surface.MouseLeftButtonUp += (_, e) => { if (e.StylusDevice is null) { End(e.GetPosition(_surface)); _surface.ReleaseMouseCapture(); e.Handled = true; } };
        _surface.TouchDown += (_, e) => { if (_start is null) { Begin(e.GetTouchPoint(_surface).Position); _surface.CaptureTouch(e.TouchDevice); } e.Handled = true; };
        _surface.TouchMove += (_, e) => { if (e.TouchDevice.Captured == _surface) Move(e.GetTouchPoint(_surface).Position); e.Handled = true; };
        _surface.TouchUp += (_, e) => { if (e.TouchDevice.Captured == _surface) { End(e.GetTouchPoint(_surface).Position); _surface.ReleaseTouchCapture(e.TouchDevice); } e.Handled = true; };
        _surface.LostMouseCapture += (_, _) => { if (_start is not null) Reselect(false); };
        _surface.LostTouchCapture += (_, _) => { if (_start is not null) Reselect(false); };
        SizeChanged += (_, _) => { PaintSelection(); ArrangeTools(modeBar); };
        Loaded += (_, _) => { _screen.Position(this); Focus(); ArrangeTools(modeBar); _status.Text = Loc.T("拖动框选；Esc 取消"); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (!Selection.IsEmpty) Reselect(_windowMode); else Close(); e.Handled = true; }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { Copy(); e.Handled = true; }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save(true); e.Handled = true; }
        };
    }
    private Point ToPixel(Point point) => new(Math.Clamp(point.X / Math.Max(1, _surface.ActualWidth) * _screen.Image.PixelWidth, 0, _screen.Image.PixelWidth), Math.Clamp(point.Y / Math.Max(1, _surface.ActualHeight) * _screen.Image.PixelHeight, 0, _screen.Image.PixelHeight));
    private void Begin(Point point)
    {
        if (!Selection.IsEmpty && _actions.Visibility == Visibility.Visible) return;
        if (_windowMode) { var window = _screen.WindowAt(ToPixel(point)); if (window is not null) Select(window.Bounds); return; }
        _start = ToPixel(point);
    }
    private void Move(Point point)
    {
        if (_windowMode && _actions.Visibility != Visibility.Visible)
        {
            Selection = _screen.WindowAt(ToPixel(point))?.Bounds ?? Int32Rect.Empty; PaintSelection(); return;
        }
        if (_start is not Point start) return;
        Selection = Region(start, ToPixel(point), _screen.Image.PixelWidth, _screen.Image.PixelHeight); PaintSelection();
    }
    private void End(Point point)
    {
        if (_start is null) return;
        Move(point); _start = null;
        if (Selection.Width >= 2 && Selection.Height >= 2) Select(Selection); else Reselect(false);
    }
    internal static Int32Rect Region(Point a, Point b, int width, int height)
    {
        int left = (int)Math.Clamp(Math.Floor(Math.Min(a.X, b.X)), 0, width), top = (int)Math.Clamp(Math.Floor(Math.Min(a.Y, b.Y)), 0, height);
        int right = (int)Math.Clamp(Math.Ceiling(Math.Max(a.X, b.X)), left, width), bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(a.Y, b.Y)), top, height);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : Int32Rect.Empty;
    }
    internal void Select(Int32Rect selection)
    {
        Selection = selection; _actions.Visibility = Visibility.Visible; PaintSelection();
        _status.Text = $"{selection.Width} × {selection.Height}";
    }
    internal void Reselect(bool windowMode)
    {
        _start = null; _windowMode = windowMode; Selection = Int32Rect.Empty; _actions.Visibility = Visibility.Collapsed;
        _status.Text = Loc.T(windowMode ? "点击需要截图的窗口" : "拖动框选；Esc 取消"); PaintSelection();
    }
    private void PaintSelection()
    {
        if (_surface.ActualWidth <= 0) return;
        var sx = _surface.ActualWidth / _screen.Image.PixelWidth; var sy = _surface.ActualHeight / _screen.Image.PixelHeight;
        _surface.Selected = Selection.IsEmpty ? Rect.Empty : new Rect(Selection.X * sx, Selection.Y * sy, Selection.Width * sx, Selection.Height * sy);
        _surface.InvalidateVisual();
        _actions.MaxWidth = Math.Max(180, ActualWidth - 24); _actions.Measure(new Size(_actions.MaxWidth, double.PositiveInfinity));
        Canvas.SetLeft(_actions, Math.Clamp(Selection.X * sx, 12, Math.Max(12, ActualWidth - _actions.DesiredSize.Width - 12)));
        var below = (Selection.Y + Selection.Height) * sy + 10;
        Canvas.SetTop(_actions, Math.Clamp(below + _actions.DesiredSize.Height < ActualHeight ? below : Selection.Y * sy - _actions.DesiredSize.Height - 10, 76, Math.Max(76, ActualHeight - _actions.DesiredSize.Height - 12)));
    }
    private void ArrangeTools(Border modes)
    {
        modes.MaxWidth = Math.Max(180, ActualWidth - 24); modes.Measure(new Size(modes.MaxWidth, double.PositiveInfinity));
        Canvas.SetLeft(modes, Math.Max(12, (ActualWidth - modes.DesiredSize.Width) / 2)); Canvas.SetTop(modes, 12);
    }
    internal void Copy()
    {
        if (Selection.IsEmpty) return;
        Act(() => { _copy(_screen.Crop(Selection)); Close(); });
    }
    private void Save(bool saveAs)
    {
        if (Selection.IsEmpty) return;
        // The native save dialog must appear above the topmost selector.
        Topmost = false;
        try { Act(() => { if (_storage.Save(_screen.Crop(Selection), saveAs) is not null) Close(); }); }
        finally { if (IsVisible) Topmost = true; }
    }
    private void Act(Action action)
    {
        try { action(); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _status.Text = Loc.T("截图操作失败：") + ex.Message; }
    }
    private sealed class CaptureSurface(BitmapSource image) : FrameworkElement
    {
        internal Rect Selected = Rect.Empty;
        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(RenderSize); dc.DrawImage(image, bounds);
            var shade = new GeometryGroup { FillRule = FillRule.EvenOdd }; shade.Children.Add(new RectangleGeometry(bounds));
            if (!Selected.IsEmpty) shade.Children.Add(new RectangleGeometry(Selected));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(130, 0, 0, 0)), null, shade);
            if (!Selected.IsEmpty) dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(133, 191, 255)), 2), Selected);
        }
    }
}

internal static class CaptureUi
{
    internal static Button Button(string label, Action action)
    {
        var button = new Button { Content = Loc.T(label), FontSize = 14, MinHeight = 44, MinWidth = 64, Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(3), Foreground = Brushes.White, Focusable = false };
        System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(label)); button.Click += (_, _) => action(); return button;
    }
    internal static Border Panel(UIElement child) => new() { Child = child, Background = new SolidColorBrush(Color.FromArgb(245, 23, 28, 36)), BorderBrush = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(6) };
    internal static void Theme(Window window)
    {
        window.FontFamily = new FontFamily("Microsoft YaHei UI"); window.UseLayoutRounding = true; window.Foreground = Brushes.White;
        foreach (var (name, color) in new[] { ("Ink", "#F0F5FF"), ("Muted", "#B2BFD1"), ("Panel", "#342F3948"), ("Surface", "#7020252F"), ("Hover", "#30FFFFFF"), ("Pressed", "#45FFFFFF"), ("Line", "#45FFFFFF"), ("Selection", "#704C7EB7"), ("Accent", "#9CC7FF"), ("OnAccent", "#101D30") })
            window.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        window.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Quickfetch;component/Assets/Quickfetch.ico"));
    }
}
