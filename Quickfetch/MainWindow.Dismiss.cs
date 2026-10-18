using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace Quickfetch;

public partial class MainWindow
{
    private OutsideClickMonitor? _outsideClicks;
    private int _outsideGeneration;
    private void ConfigureOutsideClicks()
    {
        var generation = ++_outsideGeneration;
        _outsideClicks?.Dispose(); _outsideClicks = null;
        if (_disposed || !IsVisible || _tray is null || _draggingFile) return;
        try
        {
            _outsideClicks = new OutsideClickMonitor((x, y) =>
            {
                if (Volatile.Read(ref _choosingFolder) || Volatile.Read(ref _draggingFile) || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(() => { if (generation == _outsideGeneration) DismissForOutsideClick(x, y); });
            });
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { AppDiagnostics.Current.Fault("outside-click-listener", ex); }
    }
    internal void DismissForOutsideClick(int x, int y)
    {
        if (_disposed || !IsVisible || _choosingFolder || _draggingFile) return;
        if (!GetDismissBounds(new WindowInteropHelper(this).Handle, out var bounds)) return;
        if (x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom) return;
        var target = DismissWindowFromPoint(new DismissPoint { X = x, Y = y });
        if (IsTouchKeyboardWindow(target)) return;
        Hide();
    }
    private static bool IsTouchKeyboardWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName is "TextInputHost" or "TabTip" or "InputApp" or "osk";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DismissPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct DismissBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint="GetWindowRect")] private static extern bool GetDismissBounds(IntPtr window, out DismissBounds bounds);
    [DllImport("user32.dll", EntryPoint="WindowFromPoint")] private static extern IntPtr DismissWindowFromPoint(DismissPoint point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
