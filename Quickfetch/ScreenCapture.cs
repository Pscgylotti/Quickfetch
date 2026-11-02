using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Quickfetch;

internal sealed record CaptureWindow(Int32Rect Bounds, string Title);
internal sealed record ScreenCapture(BitmapSource Image, Int32Rect Desktop, CaptureWindow[] Windows)
{
    internal static ScreenCapture Take()
    {
        var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (desktop.Width <= 0 || desktop.Height <= 0 || (long)desktop.Width * desktop.Height > 80_000_000)
            throw new InvalidOperationException(Loc.T("屏幕尺寸超出截图限制。"));
        var windows = new List<CaptureWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int));
            if (cloaked != 0) return true;
            if (DwmGetWindowAttribute(hwnd, 9, out NativeRect bounds, Marshal.SizeOf<NativeRect>()) != 0 && !GetWindowRect(hwnd, out bounds)) return true;
            var clipped = System.Drawing.Rectangle.Intersect(desktop, System.Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
            if (clipped.Width < 2 || clipped.Height < 2) return true;
            var title = new System.Text.StringBuilder(512); GetWindowText(hwnd, title, title.Capacity);
            if (title.Length > 0) windows.Add(new(new(clipped.Left - desktop.Left, clipped.Top - desktop.Top, clipped.Width, clipped.Height), title.ToString()));
            return true;
        }, IntPtr.Zero);
        DwmFlush();
        using var bitmap = new System.Drawing.Bitmap(desktop.Width, desktop.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(desktop.Left, desktop.Top, 0, 0, desktop.Size, System.Drawing.CopyPixelOperation.SourceCopy);
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze();
            return new(source, new(desktop.Left, desktop.Top, desktop.Width, desktop.Height), windows.ToArray());
        }
        finally { DeleteObject(handle); }
    }
    internal BitmapSource Crop(Int32Rect rectangle)
    {
        if (rectangle.Width < 1 || rectangle.Height < 1 || rectangle.X < 0 || rectangle.Y < 0 || (long)rectangle.X + rectangle.Width > Image.PixelWidth || (long)rectangle.Y + rectangle.Height > Image.PixelHeight)
            throw new ArgumentOutOfRangeException(nameof(rectangle));
        // Detach a small crop from the full desktop so a preview cannot retain every monitor's bitmap.
        var source = new FormatConvertedBitmap(Image, PixelFormats.Bgra32, null, 0);
        var stride = checked(rectangle.Width * 4); var pixels = new byte[checked(stride * rectangle.Height)];
        source.CopyPixels(rectangle, pixels, stride, 0);
        var result = BitmapSource.Create(rectangle.Width, rectangle.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride); result.Freeze(); return result;
    }
    internal CaptureWindow? WindowAt(Point pixel)
    {
        foreach (var window in Windows)
            if (pixel.X >= window.Bounds.X && pixel.Y >= window.Bounds.Y && pixel.X < window.Bounds.X + window.Bounds.Width && pixel.Y < window.Bounds.Y + window.Bounds.Height) return window;
        return null;
    }
    internal void Position(Window window)
    {
        if (!SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), Desktop.X, Desktop.Y, Desktop.Width, Desktop.Height, 0x40)) throw new Win32Exception();
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
