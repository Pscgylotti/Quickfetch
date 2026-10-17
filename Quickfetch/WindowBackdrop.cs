using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Quickfetch;

public static class WindowBackdrop
{
    public static string Apply(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return "Pending";
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark = key?.GetValue("AppsUseLightTheme") is int light && light == 0 ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var rounded = 2;
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        // Use a controlled Acrylic tint instead of the system transient-window material,
        // whose solid fallback can cover this taskbar-less WPF launcher.
        var none = 1;
        DwmSetWindowAttribute(handle, 38, ref none, sizeof(int));
        var accent = new AccentPolicy { State = 4, Flags = 2, Gradient = dark != 0 ? 0x551E1916u : 0x66FFFFFFu };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        var applied = false;
        try
        {
            Marshal.StructureToPtr(accent, memory, false);
            var data = new CompositionData { Attribute = 19, Data = memory, Size = (UIntPtr)Marshal.SizeOf<AccentPolicy>() };
            applied = SetWindowCompositionAttribute(handle, ref data) != 0;
        }
        catch (EntryPointNotFoundException) { }
        finally { Marshal.FreeHGlobal(memory); }
        if (!applied)
        {
            var acrylic = 3;
            DwmSetWindowAttribute(handle, 38, ref acrylic, sizeof(int));
        }
        // Clear again after native frame setup and on layout/activation transitions.
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        return applied ? "Acrylic" : "SystemAcrylic";
    }
    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int State, Flags; public uint Gradient; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public IntPtr Data; public UIntPtr Size; }
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr handle, ref CompositionData data);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
