using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Quickfetch;

// A dedicated message-loop thread keeps the keyboard callback independent of UI or file work.
// Only Win+S is intercepted; other keystrokes are passed through and never stored.
public sealed class GlobalShortcut : IDisposable
{
    private readonly Action _show;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private HookProc? _callback;
    private IntPtr _hook;
    private uint _threadId;
    private bool _leftWin, _rightWin, _ctrl, _alt, _shift, _capturedS;
    private Exception? _error;
    private int _disposed;
    public GlobalShortcut(Action show)
    {
        _show = show;
        _thread = new Thread(Run) { Name = "Quickfetch shortcut", IsBackground = true };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(Loc.T("快捷键线程未能及时启动。"));
        if (_error is not null) throw _error;
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _callback = HandleKey;
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) _error = new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("无法接管 Win + S。"));
        _ready.Set();
        if (_error is not null) return;
        try { while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); } }
        finally { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    private IntPtr HandleKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
        var data = Marshal.PtrToStructure<KeyboardData>(lParam);
        if ((data.Flags & 0x10) != 0) return CallNextHookEx(_hook, code, wParam, lParam);
        var msg = (int)wParam;
        var down = msg is 0x100 or 0x104;
        switch (data.VirtualKey)
        {
            case 0x5B: _leftWin = down; break;
            case 0x5C: _rightWin = down; break;
            case 0xA2 or 0xA3 or 0x11: _ctrl = down; break;
            case 0xA4 or 0xA5 or 0x12: _alt = down; break;
            case 0xA0 or 0xA1 or 0x10: _shift = down; break;
            case 0x53:
                if (down && (_leftWin || _rightWin) && !_ctrl && !_alt && !_shift)
                {
                    if (!_capturedS)
                    {
                        _capturedS = true;
                        // An unused key masks the otherwise bare Windows-key release, avoiding Start opening.
                        var mask = new[] { KeyInput(0x07, 0), KeyInput(0x07, 2) };
                        SendInput((uint)mask.Length, mask, Marshal.SizeOf<Input>());
                        _show();
                    }
                    return new IntPtr(1);
                }
                if (_capturedS)
                {
                    if (!down) _capturedS = false;
                    return new IntPtr(1);
                }
                break;
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static Input KeyInput(ushort key, uint flags) => new() { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = key, Flags = flags } } };
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_threadId != 0) PostThreadMessage(_threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _ready.Dispose();
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint VirtualKey, ScanCode, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Hwnd; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X,Y; public uint MouseData,Flags,Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
