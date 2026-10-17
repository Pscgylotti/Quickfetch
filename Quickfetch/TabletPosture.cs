using System.Runtime.InteropServices;

namespace Quickfetch;

internal static class TabletPosture
{
    public static bool IsTablet
    {
        get
        {
            if (GetSystemMetrics(95) == 0) return false;
            // Surface detachables and convertible hinges report slate posture here.
            if (GetSystemMetrics(0x2003) == 0) return true;
            return GetAutoRotationState(out var state) && IsTabletRotationState(state);
        }
    }
    internal static bool IsTabletRotationState(uint state) => (state & (0x80u | 0x20u | 0x10u | 0x08u)) == 0;
    // AR_LAPTOP / NOTSUPPORTED / NOSENSOR / REMOTESESSION are not evidence of tablet posture.
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetAutoRotationState(out uint state);
}
