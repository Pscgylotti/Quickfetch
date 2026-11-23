using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Quickfetch.Core;
using Forms = System.Windows.Forms;
using PixelPoint = System.Drawing.Point;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly DoubleTapTracker _gripTaps = new();
    private TouchDevice? _gripTouch;
    private Point _gripStart;
    private Vector _gripOffset;
    private long _gripDownAt;
    private bool _gripMoved, _gripCancelled;
    private const int GripHoldMs = 350;

    private void InitializeWindowGrip()
    {
        WindowGrip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch || _gripTouch != null) return;
            if (e.ClickCount == 2) { ResetWindowPlacement(); return; }
            if (WindowGrip.CaptureMouse()) BeginGrip(e.GetPosition(this));
        };
        WindowGrip.PreviewMouseMove += (_, e) =>
        {
            if (WindowGrip.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) MoveGrip(e.GetPosition(this));
        };
        WindowGrip.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!WindowGrip.IsMouseCaptured) return;
            e.Handled = true;
            MoveGrip(e.GetPosition(this)); WindowGrip.ReleaseMouseCapture();
        };
        WindowGrip.LostMouseCapture += (_, _) => FinishGrip();
        WindowGrip.PreviewTouchDown += (_, e) =>
        {
            e.Handled = true;
            if (_gripTouch != null || WindowGrip.IsMouseCaptured || !e.TouchDevice.Capture(WindowGrip)) return;
            _gripTouch = e.TouchDevice;
            BeginGrip(e.GetTouchPoint(this).Position);
        };
        WindowGrip.PreviewTouchMove += (_, e) =>
        {
            if (e.TouchDevice != _gripTouch) return;
            e.Handled = true; MoveGrip(e.GetTouchPoint(this).Position);
        };
        WindowGrip.PreviewTouchUp += (_, e) =>
        {
            if (e.TouchDevice != _gripTouch) return;
            e.Handled = true;
            var local = e.GetTouchPoint(this).Position;
            MoveGrip(local);
            var now = Environment.TickCount64;
            if (!_gripMoved && !_gripCancelled && now - _gripDownAt < GripHoldMs)
            {
                var screen = PointToScreen(local);
                var dpi = VisualTreeHelper.GetDpi(this);
                if (_gripTaps.Tap("grip", now, screen.X / dpi.DpiScaleX, screen.Y / dpi.DpiScaleY, Forms.SystemInformation.DoubleClickTime))
                    ResetWindowPlacement();
            }
            else _gripTaps.Reset();
            e.TouchDevice.Capture(null);
        };
        WindowGrip.LostTouchCapture += (_, e) =>
        {
            if (e.TouchDevice != _gripTouch) return;
            _gripTouch = null; FinishGrip();
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) _gripTaps.Reset(); };
    }

    private void BeginGrip(Point local)
    {
        _gripMoved = _gripCancelled = false; _gripDownAt = Environment.TickCount64;
        _gripStart = PointToScreen(local);
        _gripOffset = local - new Point();
    }

    private void MoveGrip(Point local)
    {
        if (_gripCancelled) return;
        var screen = PointToScreen(local);
        var dpi = VisualTreeHelper.GetDpi(this);
        if (!_gripMoved && (screen - _gripStart).Length < 4 * dpi.DpiScaleX) return;
        if (_gripTouch != null && Environment.TickCount64 - _gripDownAt < GripHoldMs)
        {
            if ((screen - _gripStart).Length > 9 * dpi.DpiScaleX) _gripCancelled = true;
            return;
        }
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetModeBounds(handle, out var r)) return;
        if (_modeTransitionActive) { StopModeTransition(); Width = _modeTargetWidth; }
        var monitor = Forms.Screen.FromPoint(new PixelPoint((int)screen.X, (int)screen.Y));
        var work = monitor.WorkingArea;
        var scale = ScreenScale(monitor);
        var x = Math.Clamp((int)Math.Round(screen.X - _gripOffset.X * scale), work.Left, Math.Max(work.Left, work.Right - (r.Right - r.Left)));
        var y = Math.Clamp((int)Math.Round(screen.Y - _gripOffset.Y * scale), work.Top, Math.Max(work.Top, work.Bottom - (r.Bottom - r.Top)));
        if (!SetModeBounds(handle, IntPtr.Zero, x, y, 0, 0, 0x15)) return;
        _gripMoved = true; GripMark.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Accent");
    }

    private void FinishGrip()
    {
        if (_gripMoved) RememberWindowPlacement();
        _gripMoved = false;
        GripMark.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Muted");
    }

    private void RememberWindowPlacement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetModeBounds(handle, out var r)) return;
        var monitor = Forms.Screen.FromHandle(handle);
        var work = monitor.WorkingArea;
        static double Fraction(int offset, int travel) => travel <= 0 ? 0.5 : Math.Clamp((double)offset / travel, 0, 1);
        SavePlacement(new(monitor.DeviceName, Fraction(r.Left - work.Left, work.Width - (r.Right - r.Left)),
            Fraction(r.Top - work.Top, work.Height - (r.Bottom - r.Top))));
    }

    private void SavePlacement(LauncherPlacement placement)
    {
        _settings.WindowPlacement = placement;
        try { _saveSettings(_settings); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { SetMessage(ex.Message); }
    }

    private void ResetWindowPlacement()
    {
        var monitor = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        SavePlacement(new(monitor.DeviceName, .5, .5));
        RestoreWindowPlacement();
    }

    private Forms.Screen? SavedMonitor() => _settings.WindowPlacement is { } p
        ? Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName.Equals(p.Display, StringComparison.OrdinalIgnoreCase)) : null;

    private void RestoreWindowPlacement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var saved = SavedMonitor();
        var work = (saved ?? Forms.Screen.PrimaryScreen!).WorkingArea;
        var position = saved == null ? null : _settings.WindowPlacement;
        // Moving to a different monitor can synchronously change the window's DPI and size.
        for (int i = 0; i < 2 && GetModeBounds(handle, out var r); i++)
        {
            var x = work.Left + (int)Math.Round(Math.Max(0, work.Width - (r.Right - r.Left)) * Math.Clamp(position?.Horizontal ?? .5, 0, 1));
            var y = work.Top + (int)Math.Round(Math.Max(0, work.Height - (r.Bottom - r.Top)) * Math.Clamp(position?.Vertical ?? .5, 0, 1));
            if (x == r.Left && y == r.Top) break;
            SetModeBounds(handle, IntPtr.Zero, x, y, 0, 0, 0x15);
        }
    }

    private Rect LauncherWorkArea()
    {
        var monitor = IsVisible ? Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle) : SavedMonitor() ?? Forms.Screen.PrimaryScreen!;
        var r = monitor.WorkingArea;
        var scale = ScreenScale(monitor);
        return new Rect(r.Left / scale, r.Top / scale, r.Width / scale, r.Height / scale);
    }

    private static double ScreenScale(Forms.Screen screen)
    {
        var monitor = MonitorFromPoint(screen.Bounds.Location, 2);
        return GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96d : 1;
    }

    [DllImport("user32")] private static extern IntPtr MonitorFromPoint(PixelPoint point, uint flags);
    [DllImport("shcore")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
