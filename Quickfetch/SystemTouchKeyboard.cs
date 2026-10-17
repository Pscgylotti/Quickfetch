using System;
using System.Runtime.InteropServices;

namespace Quickfetch;

// Documented desktop InputPane interop. Explicit show/hide avoids Toggle races and duplicate keyboards.
internal static class SystemTouchKeyboard
{
    private static readonly Guid InteropId = new("75CF2C57-9195-4931-8332-F0B409E916AF");
    private static readonly Guid PaneId = new("8A6B3F26-7090-4793-944C-C3F2CDE26276");
    internal static bool Request(IntPtr hwnd, bool show, out string error) => WithPane(hwnd, pane =>
    {
        // Both interfaces inherit IInspectable; their first own method occupies vtable slot 6.
        var call = Marshal.GetDelegateForFunctionPointer<PaneRequest>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pane), (show ? 6 : 7) * IntPtr.Size));
        Marshal.ThrowExceptionForHR(call(pane, out var accepted));
        return accepted != 0;
    }, out error);
    internal static bool Probe(IntPtr hwnd, out string error) => WithPane(hwnd, _ => true, out error);
    private static bool WithPane(IntPtr hwnd, Func<IntPtr, bool> operation, out string error)
    {
        error = ""; IntPtr text = IntPtr.Zero, factory = IntPtr.Zero, pane = IntPtr.Zero;
        var init = RoInitialize(0);
        try
        {
            if (hwnd == IntPtr.Zero) return false;
            if (init < 0 && init != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(init);
            const string name = "Windows.UI.ViewManagement.InputPane";
            Marshal.ThrowExceptionForHR(WindowsCreateString(name, (uint)name.Length, out text));
            var factoryId = InteropId;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(text, ref factoryId, out factory));
            var get = Marshal.GetDelegateForFunctionPointer<GetForWindow>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 6 * IntPtr.Size));
            var paneId = PaneId;
            Marshal.ThrowExceptionForHR(get(factory, hwnd, ref paneId, out pane));
            return operation(pane);
        }
        catch (COMException ex) { error = ex.Message; return false; }
        finally
        {
            if (pane != IntPtr.Zero) Marshal.Release(pane);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (text != IntPtr.Zero) WindowsDeleteString(text);
            if (init >= 0) RoUninitialize();
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetForWindow(IntPtr self, IntPtr hwnd, ref Guid iid, out IntPtr pane);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PaneRequest(IntPtr self, out byte accepted);
    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, uint length, out IntPtr handle);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr handle);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
}
