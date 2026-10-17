using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Quickfetch;

internal static class TrayIconFactory
{
    // A compact search ring and lightning stroke stay legible at tray sizes.
    public static Icon Create(bool dark)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new Pen(dark ? Color.FromArgb(245, 248, 255) : Color.FromArgb(35, 48, 68), 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(ring, 3.5f, 3.5f, 20, 20, 40, 295);
            graphics.DrawLine(ring, 21, 22, 28, 29);
            using var bolt = new SolidBrush(dark ? Color.FromArgb(117, 186, 255) : Color.FromArgb(48, 112, 229));
            graphics.FillPolygon(bolt, new[] { new PointF(16, 5), new PointF(9, 15), new PointF(14, 15), new PointF(11, 23), new PointF(21, 11), new PointF(16, 11) });
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
