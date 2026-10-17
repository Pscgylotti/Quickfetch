using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Quickfetch;

public static class ShellContextMenu
{
    public static (bool Invoked, string Verb) Show(IntPtr owner, string path, int x, int y)
    {
        using var menu = new Session(owner, path);
        var source = HwndSource.FromHwnd(owner);
        source?.AddHook(menu.Messages);
        try
        {
            var command = TrackPopupMenuEx(menu.Menu, 0x102, x, y, owner, IntPtr.Zero);
            if (command == 0) return (false, "");
            var verb = menu.GetVerb(command - 1);
            var invoke = new CommandInfo { Size = Marshal.SizeOf<CommandInfo>(), Mask = 0x4000 | 0x20000000,
                Owner = owner, Verb = new IntPtr(command - 1), VerbW = new IntPtr(command - 1), Show = 1, Point = new Point { X = x, Y = y } };
            Marshal.ThrowExceptionForHR(menu.Context.InvokeCommand(ref invoke));
            return (true, verb);
        }
        finally { source?.RemoveHook(menu.Messages); }
    }
    // Integration tests can build a real Shell menu without showing or invoking any command.
    public static int GetItemCount(IntPtr owner, string path) { using var menu = new Session(owner, path); return GetMenuItemCount(menu.Menu); }
    private sealed class Session : IDisposable
    {
        private IntPtr _pidl, _parent, _children;
        public IntPtr Menu { get; private set; }
        public IContextMenu Context { get; private set; } = null!;
        public Session(IntPtr owner, string path)
        {
            try
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out _pidl, 0, out _));
                var folderId = new Guid("000214E6-0000-0000-C000-000000000046");
                Marshal.ThrowExceptionForHR(SHBindToParent(_pidl, ref folderId, out _parent, out var child));
                _children = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(_children, child);
                var definition = new Definition { Owner = owner, Folder = _parent, Count = 1, Children = _children };
                var contextId = new Guid("000214E4-0000-0000-C000-000000000046");
                Marshal.ThrowExceptionForHR(SHCreateDefaultContextMenu(ref definition, ref contextId, out var context));
                Context = (IContextMenu)context;
                Menu = CreatePopupMenu();
                if (Menu == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                Marshal.ThrowExceptionForHR(Context.QueryContextMenu(Menu, 0, 1, 0x7FFF, 0));
            }
            catch { Dispose(); throw; }
        }
        public string GetVerb(uint command)
        {
            var memory = Marshal.AllocHGlobal(520);
            try { Marshal.WriteInt16(memory, 0); return Context.GetCommandString((UIntPtr)command, 4, IntPtr.Zero, memory, 260) >= 0 ? Marshal.PtrToStringUni(memory) ?? "" : ""; }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public IntPtr Messages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message is not (0x117 or 0x2B or 0x2C or 0x120)) return IntPtr.Zero;
            if (Context is IContextMenu3 three && three.HandleMenuMsg2((uint)message, wParam, lParam, out var result) >= 0)
            { handled = true; return result; }
            if (Context is IContextMenu2 two && two.HandleMenuMsg((uint)message, wParam, lParam) >= 0) handled = true;
            return IntPtr.Zero;
        }
        public void Dispose()
        {
            if (Menu != IntPtr.Zero) { DestroyMenu(Menu); Menu = IntPtr.Zero; }
            if (Context is not null) { Marshal.ReleaseComObject(Context); Context = null!; }
            if (_children != IntPtr.Zero) { Marshal.FreeHGlobal(_children); _children = IntPtr.Zero; }
            if (_parent != IntPtr.Zero) { Marshal.Release(_parent); _parent = IntPtr.Zero; }
            if (_pidl != IntPtr.Zero) { Marshal.FreeCoTaskMem(_pidl); _pidl = IntPtr.Zero; }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Definition { public IntPtr Owner, Callback, FolderPidl, Folder; public uint Count; public IntPtr Children, Associations; public uint Keys; public IntPtr KeyArray; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CommandInfo
    {
        public int Size; public uint Mask; public IntPtr Owner, Verb, Parameters, Directory; public int Show; public uint HotKey;
        public IntPtr Icon, Title, VerbW, ParametersW, DirectoryW, TitleW; public Point Point;
    }
    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInfo command);
        [PreserveSig] int GetCommandString(UIntPtr id, uint flags, IntPtr reserved, IntPtr name, uint size);
    }
    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInfo command);
        [PreserveSig] int GetCommandString(UIntPtr id, uint flags, IntPtr reserved, IntPtr name, uint size);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }
    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInfo command);
        [PreserveSig] int GetCommandString(UIntPtr id, uint flags, IntPtr reserved, IntPtr name, uint size);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, IntPtr context, out IntPtr pidl, uint attributes, out uint outputAttributes);
    [DllImport("shell32.dll")] private static extern int SHBindToParent(IntPtr pidl, ref Guid id, out IntPtr folder, out IntPtr child);
    [DllImport("shell32.dll")] private static extern int SHCreateDefaultContextMenu(ref Definition definition, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out object context);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
}
