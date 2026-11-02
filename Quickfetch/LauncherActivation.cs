using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Quickfetch;

internal static class LauncherActivation
{
    internal static bool IsStartMenu(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        GetWindowThreadProcessId(handle, out var id);
        try { using var process = Process.GetProcessById((int)id); return process.ProcessName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    internal static bool Activate(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        // Topmost windows still have a Z order. Reinsert above Start rather than relying on the existing flag.
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x43);
        SetForegroundWindow(handle);
        if (GetForegroundWindow() == handle) return true;
        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId(); var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(handle); SetForegroundWindow(handle); SetActiveWindow(handle); SetFocus(handle);
            return GetForegroundWindow() == handle;
        }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
    }
    internal static bool DismissStartMenu(IntPtr expected)
    {
        // Wait for the invoking shortcut to be released; Win+Esc has a different system meaning.
        foreach (var key in new[] { 0x5B, 0x5C, 0x10, 0x11, 0x12 }) if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
        if (GetForegroundWindow() != expected || !IsStartMenu(expected)) return false;
        var keys = new[] { new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = 0x1B } } }, new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = 0x1B, Flags = 2 } } } };
        return SendInput(2, keys, Marshal.SizeOf<Input>()) == 2;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr handle);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
