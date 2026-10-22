using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Quickfetch;

internal sealed class FileDragVisuals : IDisposable
{
    private readonly Window _ghost, _cancel;
    private readonly System.Drawing.Rectangle _cancelBounds;
    internal IntPtr GhostHandle => new WindowInteropHelper(_ghost).Handle;
    internal IntPtr CancelHandle => new WindowInteropHelper(_cancel).Handle;
    internal FileDragVisuals(MainWindow source, Point screenPoint)
    {
        var dpi = VisualTreeHelper.GetDpi(source);
        var content = (FrameworkElement)source.Content;
        var snapshot = new RenderTargetBitmap(Math.Max(1, (int)(content.ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)(content.ActualHeight * dpi.DpiScaleY)), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        snapshot.Render(content); snapshot.Freeze();
        _ghost = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true, Focusable = false, Left = source.Left, Top = source.Top, Width = source.ActualWidth, Height = source.ActualHeight,
            IsHitTestVisible = false, Content = new Image { Source = snapshot, Opacity = .18, Effect = new BlurEffect { Radius = 10 } } };
        _ghost.SourceInitialized += (_, _) => SetStyle(new WindowInteropHelper(_ghost).Handle, 0x080000A0);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPoint.X, (int)screenPoint.Y));
        var bounds = screen.Bounds;
        _cancelBounds = new System.Drawing.Rectangle(bounds.Left, bounds.Top, bounds.Width, (int)Math.Round(60 * dpi.DpiScaleY));
        var label = new TextBlock { Text = Loc.T("拖到这里松手取消   ·   Esc 取消"), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var border = new Border { CornerRadius = new CornerRadius(0, 0, 12, 12), BorderThickness = new Thickness(1), Child = label, Padding = new Thickness(16, 8, 16, 8) };
        border.SetResourceReference(Border.BackgroundProperty, "KeyboardSurface"); border.SetResourceReference(Border.BorderBrushProperty, "Accent");
        _cancel = new Window { Title = Loc.T("取消拖放"), Width = bounds.Width / dpi.DpiScaleX, Height = 60,
            Left = bounds.Left / dpi.DpiScaleX, Top = bounds.Top / dpi.DpiScaleY,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, AllowDrop = true, Content = border };
        _cancel.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(_cancel).Handle; WindowBackdrop.Apply(h); SetStyle(h, 0x08000080);
            var square = 1; DwmSetWindowAttribute(h, 33, ref square, sizeof(int));
        };
        _cancel.PreviewDragOver += (_, e) => { border.SetResourceReference(Border.BackgroundProperty, "Selection"); e.Effects = DragDropEffects.None; e.Handled = true; };
        _cancel.DragLeave += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "KeyboardSurface");
        _cancel.PreviewDrop += (_, e) => { e.Effects = DragDropEffects.None; e.Handled = true; };
    }
    internal void Show()
    {
        _ghost.Show(); _cancel.Show();
        // Use monitor pixels so scaling, top taskbars and rounded native corners
        // cannot leave a non-cancelling gap at the physical screen edge.
        SetWindowPos(CancelHandle, IntPtr.Zero, _cancelBounds.Left, _cancelBounds.Top, _cancelBounds.Width, _cancelBounds.Height, 0x214);
    }
    internal bool PointerOverCancel()
    {
        if (!TryPointer(out var point) || !GetWindowRect(new WindowInteropHelper(_cancel).Handle, out var r)) return false;
        return point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom;
    }
    internal static bool TryPointer(out Point point) { var ok = GetCursorPos(out var p); point = new Point(p.X, p.Y); return ok; }
    private static void SetStyle(IntPtr window, long flags) => SetWindowLongPtr(window, -20, new IntPtr(GetWindowLongPtr(window, -20).ToInt64() | flags));
    public void Dispose() { _ghost.Close(); _cancel.Close(); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
