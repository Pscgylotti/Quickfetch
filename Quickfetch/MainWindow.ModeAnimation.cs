using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quickfetch;

public partial class MainWindow
{
    private bool _modeTransitionActive;
    private double _modeTargetWidth;
    private long _modeStarted;
    private ModeRect _modeStart;
    private int _modeEndWidth, _modeEndLeft, _modeLastWidth, _modeLastLeft;
    private void StopModeTransition()
    {
        if (!_modeTransitionActive) return;
        CompositionTarget.Rendering -= RenderModeTransition;
        _modeTransitionActive = false;
        ResultsSurface.BeginAnimation(OpacityProperty, null); ResultsSurface.Opacity = 1;
        if (ResultsSurface.RenderTransform is TranslateTransform shift) { shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = 0; }
    }
    private void AnimateModeTransition()
    {
        if (!IsVisible || !SystemParameters.ClientAreaAnimation) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetModeBounds(handle, out _modeStart)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        _modeEndWidth = Math.Max(1, (int)Math.Round(_modeTargetWidth * dpi.DpiScaleX));
        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var margin = (int)Math.Round(12 * dpi.DpiScaleX);
        _modeEndLeft = Math.Clamp(_modeStart.Left + (_modeStart.Right - _modeStart.Left - _modeEndWidth) / 2,
            work.Left + margin, Math.Max(work.Left + margin, work.Right - margin - _modeEndWidth));
        _modeLastWidth = _modeStart.Right - _modeStart.Left; _modeLastLeft = _modeStart.Left;
        // The clock starts on the first composed frame: switching re-templates the results, and
        // that first layout/render pass can take longer than the whole resize. Timing from the
        // click would skip most of the curve and read as a jump.
        _modeStarted = 0; _modeTransitionActive = true;
        CompositionTarget.Rendering += RenderModeTransition;
        ResultsSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(.2, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
        if (ResultsSurface.RenderTransform is TranslateTransform shift)
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    private void RenderModeTransition(object? sender, EventArgs args)
    {
        if (!_modeTransitionActive) return;
        if (_modeStarted == 0) { _modeStarted = Stopwatch.GetTimestamp(); return; }
        var progress = Math.Clamp(Stopwatch.GetElapsedTime(_modeStarted).TotalMilliseconds / 180, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var width = (int)Math.Round((_modeStart.Right - _modeStart.Left) + (_modeEndWidth - (_modeStart.Right - _modeStart.Left)) * eased);
        var left = (int)Math.Round(_modeStart.Left + (_modeEndLeft - _modeStart.Left) * eased);
        // One atomic HWND move/resize per rendered frame, rounded once in physical
        // pixels. Separate Width/Left animations caused intermediate HWND jumps.
        if (width != _modeLastWidth || left != _modeLastLeft)
        {
            SetModeBounds(new WindowInteropHelper(this).Handle, IntPtr.Zero, left, _modeStart.Top, width, _modeStart.Bottom - _modeStart.Top, 0x214);
            _modeLastWidth = width; _modeLastLeft = left;
        }
        if (progress < 1) return;
        // WPF can keep Width from an intermediate WM_SIZE even though the HWND reached the
        // target; re-sync it (same physical size, so no visible change) for later centering.
        StopModeTransition(); Width = _modeTargetWidth; ScheduleBackdrop();
    }
    [StructLayout(LayoutKind.Sequential)] private struct ModeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool GetModeBounds(IntPtr window, out ModeRect rect);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool SetModeBounds(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
