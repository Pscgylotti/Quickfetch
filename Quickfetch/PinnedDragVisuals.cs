using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quickfetch;

// Separate non-activating, click-through windows keep the drag preview visible outside the launcher.
internal sealed class PinnedDragVisuals : IDisposable
{
    private readonly Window _ghost, _remove;
    private readonly Border _removeSurface;
    private readonly System.Drawing.Rectangle _removeBounds;
    private readonly double _scaleX, _scaleY;
    internal bool OverRemove { get; private set; }
    internal PinnedDragVisuals(MainWindow owner, ShortcutItem item, Point point)
    {
        var dpi = VisualTreeHelper.GetDpi(owner); _scaleX = dpi.DpiScaleX; _scaleY = dpi.DpiScaleY;
        var icon = item.Icon is not null ? (UIElement)new Image { Source = item.Icon, Width = 32, Height = 32 }
            : new TextBlock { Text = "\uE71D", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var surface = new Border { Child = icon, Padding = new Thickness(10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), RenderTransformOrigin = new Point(.5, .5) };
        surface.SetResourceReference(Border.BackgroundProperty, "TooltipSurface"); surface.SetResourceReference(Border.BorderBrushProperty, "Accent"); surface.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        _ghost = Overlay(surface, 56, 56); _ghost.Owner = owner;
        var monitor = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)).Bounds;
        _removeBounds = new(monitor.Left, monitor.Top, monitor.Width, (int)Math.Ceiling(64 * _scaleY));
        var label = new TextBlock { Text = Loc.T("拖到这里松手取消固定   ·   Esc 取消拖动"), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        _removeSurface = new Border { Child = label, Padding = new Thickness(16, 8, 16, 8), CornerRadius = new CornerRadius(0, 0, 12, 12), BorderThickness = new Thickness(1) };
        _removeSurface.SetResourceReference(Border.BackgroundProperty, "TooltipSurface"); _removeSurface.SetResourceReference(Border.BorderBrushProperty, "Line");
        _remove = Overlay(_removeSurface, monitor.Width / _scaleX, 64); _remove.Owner = owner;
        _remove.Show(); _ghost.Show();
        MoveWindow(_remove, _removeBounds.Left, _removeBounds.Top, _removeBounds.Width, _removeBounds.Height);
        Move(point);
        var scale = new ScaleTransform(1, 1); surface.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(140)));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(140)));
    }
    private static Window Overlay(UIElement content, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Content = content, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Focusable = false, IsHitTestVisible = false };
        window.SourceInitialized += (_, _) => { var h = new WindowInteropHelper(window).Handle; SetWindowLongPtr(h, -20, new IntPtr(GetWindowLongPtr(h, -20).ToInt64() | 0x080000A0)); };
        return window;
    }
    internal void Move(Point point)
    {
        MoveWindow(_ghost, (int)Math.Round(point.X - 28 * _scaleX), (int)Math.Round(point.Y - 28 * _scaleY), (int)Math.Ceiling(56 * _scaleX), (int)Math.Ceiling(56 * _scaleY));
        var over = _removeBounds.Contains((int)point.X, (int)point.Y);
        if (over == OverRemove) return;
        OverRemove = over; _removeSurface.SetResourceReference(Border.BackgroundProperty, over ? "Selection" : "TooltipSurface");
        _removeSurface.SetResourceReference(Border.BorderBrushProperty, over ? "Accent" : "Line"); _ghost.Opacity = over ? .55 : 1;
    }
    private static void MoveWindow(Window window, int x, int y, int width, int height) => SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), x, y, width, height, 0x210);
    public void Dispose() { _ghost.Close(); _remove.Close(); }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
