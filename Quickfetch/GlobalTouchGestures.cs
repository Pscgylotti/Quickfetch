using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Quickfetch.Core;
using Forms = System.Windows.Forms;

namespace Quickfetch;

// Raw Input observes touchscreen HID packets globally without installing a driver or stealing touch focus.
// It cannot consume the original touch stream; see docs/touch-gestures.md.
internal sealed class GlobalTouchGestures : IDisposable
{
    private readonly GestureSettings _settings;
    private readonly Action<GestureMatch, int, int> _matched;
    private readonly Action<string> _status;
    private readonly Thread _thread;
    private readonly Dictionary<IntPtr, Device?> _devices = new();
    private Dispatcher? _dispatcher;
    private HwndSource? _source;
    private IntPtr _buffer;
    private uint _capacity;
    private ScreenMapping? _mapping;
    private int _disposed;
    internal GlobalTouchGestures(GestureSettings settings, Action<GestureMatch, int, int> matched, Action<string> status)
    {
        _settings = settings; _matched = matched; _status = status;
        _thread = new Thread(Run) { Name = "Quickfetch touchscreen", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
    }
    private void Run()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            // An invisible top-level window receives display/settings broadcasts; message-only windows do not.
            using var source = new HwndSource(new HwndSourceParameters("Quickfetch touch input") { WindowStyle = 0, ExtendedWindowStyle = 0x08000080 });
            _source = source; source.AddHook(Messages);
            var registration = new[] { new RawDevice { Page = 0x0D, Usage = 4, Flags = 0x2100, Target = source.Handle } };
            if (!RegisterRawInputDevices(registration, 1, (uint)Marshal.SizeOf<RawDevice>())) throw new Win32Exception(Marshal.GetLastWin32Error());
            _status("全局监听已开启 · 等待触屏输入");
            if (Volatile.Read(ref _disposed) == 0) Dispatcher.Run();
            registration[0].Flags = 1; registration[0].Target = IntPtr.Zero;
            RegisterRawInputDevices(registration, 1, (uint)Marshal.SizeOf<RawDevice>());
        }
        catch (Exception ex) { _status("触屏监听不可用：" + ex.Message); }
        finally
        {
            foreach (var device in _devices.Values) device?.Dispose(); _devices.Clear();
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _source = null;
        }
    }
    private IntPtr Messages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0xFE) // WM_INPUT_DEVICE_CHANGE: discard descriptor, parser and stale contacts.
        {
            if (_devices.Remove(lParam, out var device)) device?.Dispose();
        }
        if (message == 0x7E || message == 0x1A)
        {
            _mapping = null;
            foreach (var device in _devices.Values) device?.Reset();
        }
        if (message != 0xFF || Volatile.Read(ref _disposed) != 0) return IntPtr.Zero;
        try { Read(lParam); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            foreach (var device in _devices.Values) device?.Reset();
            _status("触屏数据无法解析：" + ex.Message);
        }
        // Leave unhandled so DefWindowProc performs foreground WM_INPUT cleanup.
        return IntPtr.Zero;
    }
    private void Read(IntPtr input)
    {
        uint size = 0; uint header = (uint)(8 + IntPtr.Size * 2);
        if (GetRawInputData(input, 0x10000003, IntPtr.Zero, ref size, header) == uint.MaxValue || size < header + 8 || size > 1024 * 1024) return;
        if (size > _capacity) { if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer); _buffer = Marshal.AllocHGlobal((int)size); _capacity = size; }
        if (GetRawInputData(input, 0x10000003, _buffer, ref size, header) == uint.MaxValue || Marshal.ReadInt32(_buffer) != 2) return;
        var handle = Marshal.ReadIntPtr(_buffer, 8);
        if (!_devices.TryGetValue(handle, out var device))
        {
            device = Device.Open(handle); _devices.Add(handle, device);
            _status(device is null ? "此触屏的 HID 格式暂不支持" : "全局监听中 · 已识别触屏");
        }
        if (device is null) return;
        var length = Marshal.ReadInt32(_buffer, (int)header); var count = Marshal.ReadInt32(_buffer, (int)header + 4);
        if (length <= 0 || count < 0 || (long)length * count > size - header - 8) return;
        for (var i = 0; i < count; i++)
        {
            var report = new byte[length]; Marshal.Copy(_buffer + (int)header + 8 + i * length, report, 0, length);
            var frame = device.Parse(report, Environment.TickCount64);
            if (frame is null) continue;
            _mapping ??= GetScreenMapping();
            if (_mapping is null) continue;
            var bounds = _mapping.Bounds; var scale = _mapping.Scale; var rotation = _mapping.Rotation;
            var mapped = frame.Select(p =>
            {
                var (x, y) = rotation switch { 1 => (1 - p.Y, p.X), 2 => (1 - p.X, 1 - p.Y), 3 => (p.Y, 1 - p.X), _ => (p.X, p.Y) };
                return new TouchContact(p.Id, x * (bounds.Width - 1) / scale, y * (bounds.Height - 1) / scale, p.Down);
            }).ToArray();
            var match = device.Recognizer.Frame(mapped, Environment.TickCount64, bounds.Width / scale, bounds.Height / scale, _settings.TapDistance);
            if (match is { } found && (found.Gesture != TouchGesture.TwoFingerTap || _settings.TwoFingerRightClick))
                _matched(found, bounds.Left + (int)Math.Round(found.FirstX * scale), bounds.Top + (int)Math.Round(found.FirstY * scale));
        }
    }
    private sealed record ScreenMapping(System.Drawing.Rectangle Bounds, double Scale, int Rotation);
    private ScreenMapping? GetScreenMapping()
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.DisplayDeviceName) ?? Forms.Screen.PrimaryScreen;
        if (screen is null) return null;
        var bounds = screen.Bounds;
        var center = new NativePoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
        double scale = 1;
        if (GetDpiForMonitor(MonitorFromPoint(center, 2), 0, out var dpi, out _) == 0 && dpi != 0) scale = dpi / 96d;
        return new(bounds, scale, DisplayRotation(screen.DeviceName));
    }
    private static int DisplayRotation(string name)
    {
        var mode = new byte[220]; BitConverter.GetBytes((ushort)220).CopyTo(mode, 68);
        return EnumDisplaySettings(name, -1, mode) ? BitConverter.ToInt32(mode, 84) : 0;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send); } catch (InvalidOperationException) { }
        if (Thread.CurrentThread != _thread) _thread.Join(2000);
    }
    internal static string ProbeDevices()
    {
        uint count = 0;
        if (GetRawInputDeviceList(null, ref count, (uint)Marshal.SizeOf<RawDeviceList>()) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        var devices = new RawDeviceList[count];
        if (GetRawInputDeviceList(devices, ref count, (uint)Marshal.SizeOf<RawDeviceList>()) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        var supported = 0;
        foreach (var raw in devices.Take((int)count).Where(d => d.Type == 2)) { using var device = Device.Open(raw.Handle); if (device is not null) supported++; }
        return $"Raw Input devices: {count}; supported touchscreen collections: {supported}";
    }
    // Builds HID reports in memory against the real descriptor; never injects desktop input.
    internal static int CheckNativeParser()
    {
        uint count = 0; GetRawInputDeviceList(null, ref count, (uint)Marshal.SizeOf<RawDeviceList>());
        var devices = new RawDeviceList[count];
        if (GetRawInputDeviceList(devices, ref count, (uint)Marshal.SizeOf<RawDeviceList>()) == uint.MaxValue) return 0;
        var verified = 0;
        foreach (var raw in devices.Take((int)count).Where(d => d.Type == 2))
        {
            using var device = Device.Open(raw.Handle);
            if (device is null) continue;
            device.CheckReports(); verified++;
        }
        return verified;
    }

    private sealed class Device : IDisposable
    {
        private readonly IntPtr _data;
        private readonly ValueCap[] _values;
        private readonly ushort _reportLength;
        private readonly (ushort Link, byte Report, ValueCap X, ValueCap Y)[] _fingers;
        private readonly TouchFrameAssembler _frames = new();
        public readonly TouchGestureRecognizer Recognizer = new();
        private Device(IntPtr data, ValueCap[] values, ushort reportLength)
        {
            _data = data; _values = values; _reportLength = reportLength;
            _fingers = values.Where(v => v.Page == 0x0D && v.Usage == 0x51).Select(v => (v.Link, v.Report,
                values.FirstOrDefault(x => x.Page == 1 && x.Usage == 0x30 && x.Link == v.Link && x.Report == v.Report),
                values.FirstOrDefault(y => y.Page == 1 && y.Usage == 0x31 && y.Link == v.Link && y.Report == v.Report))).ToArray();
        }
        public static Device? Open(IntPtr handle)
        {
            uint size = 0;
            GetRawInputDeviceInfo(handle, 0x20000005, IntPtr.Zero, ref size);
            if (size == 0 || size > 1024 * 1024) return null;
            var data = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputDeviceInfo(handle, 0x20000005, data, ref size) == uint.MaxValue) return null;
                var caps = new byte[64];
                if (HidP_GetCaps(data, caps) < 0 || BitConverter.ToUInt16(caps, 0) != 4 || BitConverter.ToUInt16(caps, 2) != 0x0D) return null;
                ushort count = BitConverter.ToUInt16(caps, 48);
                if (count == 0 || count > 1024) return null;
                var buffer = new byte[count * 72];
                if (HidP_GetValueCaps(0, buffer, ref count, data) < 0) return null;
                var values = Enumerable.Range(0, count).SelectMany(i => ValueCap.Read(buffer, i * 72)).ToArray();
                var result = new Device(data, values, BitConverter.ToUInt16(caps, 4));
                if (result._fingers.Length == 0 || result._fingers.Any(f => f.X.Max <= f.X.Min || f.Y.Max <= f.Y.Min) || !values.Any(v => v.Page == 0x0D && v.Usage == 0x54)) return null;
                data = IntPtr.Zero; return result;
            }
            finally { if (data != IntPtr.Zero) Marshal.FreeHGlobal(data); }
        }
        private bool Value(ushort page, ushort link, ushort usage, byte[] report, out uint value) => HidP_GetUsageValue(0, page, link, usage, out value, _data, report, (uint)report.Length) >= 0;
        public TouchContact[]? Parse(byte[] report, long now)
        {
            var countCap = _values.FirstOrDefault(v => v.Page == 0x0D && v.Usage == 0x54 && (v.Report == 0 || v.Report == report[0]));
            if (countCap.Usage == 0 || !Value(0x0D, countCap.Link, 0x54, report, out var count) || count > 256) return null;
            var slots = new List<TouchContact>();
            foreach (var finger in _fingers)
            {
                if (finger.Report != 0 && finger.Report != report[0]) continue;
                if (!Value(0x0D, finger.Link, 0x51, report, out var id) || !Value(1, finger.Link, 0x30, report, out var x) || !Value(1, finger.Link, 0x31, report, out var y)) continue;
                var usages = new ushort[32]; uint number = (uint)usages.Length;
                if (HidP_GetUsages(0, 0x0D, finger.Link, usages, ref number, _data, report, (uint)report.Length) < 0) { Reset(); return null; }
                var tip = usages.Take((int)number).Contains((ushort)0x42);
                slots.Add(new TouchContact((int)id, Math.Clamp((x - (double)finger.X.Min) / (finger.X.Max - (double)finger.X.Min), 0, 1),
                    Math.Clamp((y - (double)finger.Y.Min) / (finger.Y.Max - (double)finger.Y.Min), 0, 1), tip));
            }
            return _frames.Add((int)count, slots, now);
        }
        public void Reset() { _frames.Reset(); Recognizer.Reset(); }
        public void CheckReports()
        {
            // Exercise parallel and hybrid packets using the device's own report layout.
            var reportId = _fingers[0].Report;
            var slots = _fingers.Where(f => f.Report == reportId).Take(2).ToArray();
            TouchContact[]? result = null;
            for (var first = 0; first < 2; first += slots.Length)
            {
                var report = new byte[_reportLength];
                if (HidP_InitializeReportForID(0, reportId, _data, report, (uint)report.Length) < 0) throw new InvalidOperationException("Cannot initialize touchscreen HID fixture.");
                void Set(ushort page, ushort link, ushort usage, uint value)
                {
                    if (HidP_SetUsageValue(0, page, link, usage, value, _data, report, (uint)report.Length) < 0) throw new InvalidOperationException("Cannot encode touchscreen HID fixture.");
                }
                var countCap = _values.First(v => v.Page == 0x0D && v.Usage == 0x54 && (v.Report == 0 || v.Report == reportId));
                Set(0x0D, countCap.Link, 0x54, first == 0 ? 2u : 0u);
                for (var i = 0; i < slots.Length && first + i < 2; i++)
                {
                    var finger = slots[i];
                    Set(0x0D, finger.Link, 0x51, (uint)(first + i + 1));
                    Set(1, finger.Link, 0x30, (uint)(finger.X.Min + (finger.X.Max - finger.X.Min) * (0.2 + 0.1 * (first + i))));
                    Set(1, finger.Link, 0x31, (uint)(finger.Y.Min + (finger.Y.Max - finger.Y.Min) * 0.4));
                    var tip = new ushort[] { 0x42 }; uint number = 1;
                    if (HidP_SetUsages(0, 0x0D, finger.Link, tip, ref number, _data, report, (uint)report.Length) < 0) throw new InvalidOperationException("Cannot encode touchscreen tip switch.");
                }
                result = Parse(report, 100 + first);
            }
            if (result is null || result.Length != 2 || result.Any(p => !p.Down) || result.Select(p => p.Id).Distinct().Count() != 2 || Math.Abs(result[0].X - 0.2) > 0.01 || Math.Abs(result[1].X - 0.3) > 0.01)
                throw new InvalidOperationException("Native touchscreen HID decode check failed.");
            Reset();
        }
        public void Dispose() => Marshal.FreeHGlobal(_data);
    }
    private readonly record struct ValueCap(ushort Page, byte Report, ushort Link, ushort Usage, int Min, int Max)
    {
        internal static IEnumerable<ValueCap> Read(byte[] data, int offset)
        {
            var first = BitConverter.ToUInt16(data, offset + 56);
            var last = data[offset + 12] != 0 ? BitConverter.ToUInt16(data, offset + 58) : first;
            if (last < first || last - first > 64) yield break;
            for (var usage = (int)first; usage <= last; usage++)
                yield return new(BitConverter.ToUInt16(data, offset), data[offset + 2], BitConverter.ToUInt16(data, offset + 6), (ushort)usage, BitConverter.ToInt32(data, offset + 40), BitConverter.ToInt32(data, offset + 44));
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawDeviceList { public IntPtr Handle; public uint Type; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList([In, Out] RawDeviceList[]? devices, ref uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, [Out] byte[] caps);
    [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, [Out] byte[] caps, ref ushort length, IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr data, byte[] report, uint length);
    [DllImport("hid.dll")] private static extern int HidP_GetUsages(int type, ushort page, ushort link, [Out] ushort[] usages, ref uint count, IntPtr data, byte[] report, uint length);
    [DllImport("hid.dll")] private static extern int HidP_InitializeReportForID(int type, byte id, IntPtr data, [Out] byte[] report, uint length);
    [DllImport("hid.dll")] private static extern int HidP_SetUsageValue(int type, ushort page, ushort link, ushort usage, uint value, IntPtr data, [In, Out] byte[] report, uint length);
    [DllImport("hid.dll")] private static extern int HidP_SetUsages(int type, ushort page, ushort link, ushort[] usages, ref uint count, IntPtr data, [In, Out] byte[] report, uint length);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int mode, [Out] byte[] settings);
}
