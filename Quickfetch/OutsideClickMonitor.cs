using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Quickfetch;

// Exists only while the launcher is visible. Forward every input unchanged;
// the callback only queues coordinates and never performs UI or file work.
internal sealed class OutsideClickMonitor : IDisposable
{
    private readonly Action<int, int> _click;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly HookProc _callback;
    private IntPtr _hook;
    private uint _threadId;
    private Exception? _error;
    private int _disposed;
    internal OutsideClickMonitor(Action<int, int> click)
    {
        _click = click; _callback = MouseMessage;
        _thread = new Thread(Run) { IsBackground = true, Name = "Quickfetch outside click" }; _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(3))) { Dispose(); throw new TimeoutException(Loc.T("窗口外点击监听启动超时。")); }
        if (_error is not null) { Dispose(); throw _error; }
    }
    private void Run()
    {
        _threadId = GetCurrentThreadId(); PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _hook = SetWindowsHookEx(14, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) _error = new Win32Exception(Marshal.GetLastWin32Error());
        _ready.Set();
        if (_error is not null) return;
        try
        {
            while (Volatile.Read(ref _disposed) == 0 && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            { TranslateMessage(ref message); DispatchMessage(ref message); }
        }
        finally { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
    private IntPtr MouseMessage(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && Volatile.Read(ref _disposed) == 0 && (int)wParam is 0x201 or 0x204 or 0x207)
        {
            try { var point = Marshal.PtrToStructure<MouseData>(lParam); _click(point.X, point.Y); }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { /* Optional dismissal must not interrupt native input. */ }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_threadId != 0) PostThreadMessage(_threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        // A timed-out startup can still reach Set(); dispose only after its thread exits.
        if (!_thread.IsAlive) _ready.Dispose();
    }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Hwnd; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
