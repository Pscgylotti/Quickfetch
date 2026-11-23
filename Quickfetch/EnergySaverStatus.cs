using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Quickfetch;

internal static class EnergySaverStatus
{
    // Windows supplies the current value on registration, including while plugged in.
    private static readonly Guid Setting = new("550e8400-e29b-41d4-a716-446655440000");

    internal static async Task<bool> Read(CancellationToken token = default)
    {
        var current = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = new Subscription { Callback = (_, type, data) =>
        {
            if (type == 0x8013 && data != IntPtr.Zero && Marshal.PtrToStructure<Guid>(data) == Setting &&
                Marshal.ReadInt32(data, 16) == sizeof(int))
                current.TrySetResult(Marshal.ReadInt32(data, 20) != 0);
            return 0;
        }};
        var error = PowerSettingRegisterNotification(in Setting, 2, ref subscription, out var handle);
        if (error != 0) throw new Win32Exception(error);
        try { return await current.Task.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false); }
        finally { PowerSettingUnregisterNotification(handle); GC.KeepAlive(subscription.Callback); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint Notify(IntPtr context, uint type, IntPtr setting);
    [StructLayout(LayoutKind.Sequential)]
    private struct Subscription { public Notify Callback; public IntPtr Context; }
    [DllImport("powrprof")] private static extern int PowerSettingRegisterNotification(in Guid setting, uint flags, ref Subscription recipient, out IntPtr handle);
    [DllImport("powrprof")] private static extern int PowerSettingUnregisterNotification(IntPtr handle);
}
