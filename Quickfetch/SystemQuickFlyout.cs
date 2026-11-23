using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Quickfetch;

// Shell owns the device pages. Move only the foreground Quick Settings
// window opened for this gesture, then return it to its original taskbar position.
internal sealed class SystemQuickFlyout
{
    private IntPtr _window;
    private Point? _original, _placed;
    internal bool Opening { get; private set; } = true;
    internal bool Contains(IntPtr target) => _window != IntPtr.Zero && GetAncestor(target, 2) == _window;

    internal async Task Show(string page, Point anchor, Point trigger, double dpiScale, Action<string> launch, CancellationToken token)
    {
        var previous = GetForegroundWindow();
        var work = Screen.FromPoint(anchor).WorkingArea;
        try
        {
            using (var controlCenter = Registry.ClassesRoot.OpenSubKey("ms-controlcenter"))
                launch(controlCenter != null ? "ms-controlcenter:" + page :
                    page == "wifi" ? "ms-availablenetworks:" : "ms-actioncenter:controlcenter/" + page);
            var startup = Stopwatch.StartNew();
            while (startup.ElapsedMilliseconds < 2000)
            {
                var window = GetForegroundWindow();
                if (window != previous && window != IntPtr.Zero && IsQuickSettingsWindow(window, out var canMove))
                {
                    _window = window;
                    Opening = false;
                    using var cancellation = token.Register(Restore);
                    var entrance = Stopwatch.StartNew();
                    var animate = canMove && Motion.Enabled;
                    while (!token.IsCancellationRequested && Contains(GetForegroundWindow()) && IsWindowVisible(window))
                    {
                        var progress = animate ? Math.Min(1, entrance.Elapsed.TotalMilliseconds / 150) : 1;
                        if (canMove) Place(work, anchor, trigger, 18 * dpiScale, progress);
                        await Task.Delay(progress < 1 ? 16 : 150, token);
                    }
                    return;
                }
                await Task.Delay(startup.ElapsedMilliseconds < 350 ? 16 : 50, token);
            }
        }
        finally { Opening = false; Restore(); _window = IntPtr.Zero; }
    }

    private static bool IsQuickSettingsWindow(IntPtr window, out bool canMove)
    {
        var name = new StringBuilder(96);
        GetClassName(window, name, name.Capacity);
        canMove = name.ToString() == "ControlCenterWindow";
        if (!canMove && name.ToString() != "Windows.UI.Core.CoreWindow") return false;
        GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var owner = Process.GetProcessById((int)pid);
            return string.Equals(owner.ProcessName, canMove ? "ShellHost" : "ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    internal static Point OriginFor(Rectangle work, Point anchor, Rectangle region)
    {
        var x = Math.Clamp(anchor.X - region.Width, work.Left, Math.Max(work.Left, work.Right - region.Width));
        var y = Math.Clamp(anchor.Y, work.Top, Math.Max(work.Top, work.Bottom - region.Height));
        return new Point(x - region.Left, y - region.Top);
    }

    internal static Point EntranceOrigin(Rectangle work, Point anchor, Rectangle region, Point trigger, double distance, double progress)
    {
        var end = OriginFor(work, anchor, region);
        if (progress >= 1) return end;
        var x = end.X + region.Left;
        var y = end.Y + region.Top;
        var dx = trigger.X - (x + region.Width / 2.0);
        var dy = trigger.Y - (y + region.Height / 2.0);
        var scale = Math.Min(1, distance / Math.Max(1, Math.Sqrt(dx * dx + dy * dy))) * Math.Pow(1 - progress, 2);
        return new Point(Math.Clamp(x + (int)Math.Round(dx * scale), work.Left, Math.Max(work.Left, work.Right - region.Width)) - region.Left,
            Math.Clamp(y + (int)Math.Round(dy * scale), work.Top, Math.Max(work.Top, work.Bottom - region.Height)) - region.Top);
    }

    private void Place(Rectangle work, Point anchor, Point trigger, double distance, double progress)
    {
        if (!GetWindowRect(_window, out var bounds)) return;
        // The shell host can span the entire monitor height. Its shaped region describes
        // the visible flyout, including when a network row expands to accept a password.
        var handle = CreateRectRgn(0, 0, 0, 0);
        if (handle == IntPtr.Zero) return;
        try
        {
            if (GetWindowRgn(_window, handle) < 2 || GetRgnBox(handle, out var r) < 2) return;
            var visible = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (visible.Width <= 0 || visible.Height <= 0) return;
            var origin = EntranceOrigin(work, anchor, visible, trigger, distance, progress);
            if (bounds.Left == origin.X && bounds.Top == origin.Y) return;
            if (!Move(_window, origin)) return;
            _original ??= new Point(bounds.Left, bounds.Top);
            _placed = origin;
        }
        finally { DeleteObject(handle); }
    }

    private void Restore()
    {
        if (_original is { } original && _placed is { } placed && GetWindowRect(_window, out var r) &&
            r.Left == placed.X && r.Top == placed.Y && IsQuickSettingsWindow(_window, out _))
            Move(_window, original);
        _original = _placed = null;
    }

    private static bool Move(IntPtr window, Point origin) =>
        SetWindowPos(window, IntPtr.Zero, origin.X, origin.Y, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("user32")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("gdi32")] private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32")] private static extern int GetRgnBox(IntPtr region, out Rect rect);
    [DllImport("gdi32")] private static extern bool DeleteObject(IntPtr region);
}
