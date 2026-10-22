using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Quickfetch.Core;

namespace Quickfetch;

public sealed class EverythingClient : IDisposable
{
    private readonly IntPtr _replyWindow;
    private TaskCompletionSource<FileSearchPage>? _pending;
    private uint _replyId = 0x51460000;
    private bool _disposed;
    private sealed record Capabilities(IntPtr Window, uint ProcessId, bool Pinyin);
    private Capabilities? _capabilities;
    public bool PinyinApplied { get; private set; }
    public string Instance { get; private set; } = "";
    public static bool IsAvailable => FindWindow("EVERYTHING_TASKBAR_NOTIFICATION_(1.5a)", null) != IntPtr.Zero || FindWindow("EVERYTHING_TASKBAR_NOTIFICATION", null) != IntPtr.Zero;
    public EverythingClient(IntPtr replyWindow) => _replyWindow = replyWindow;
    public async Task<FileSearchPage> SearchAsync(string query, uint sort, uint maximum, CancellationToken token)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EverythingClient));
        _pending?.TrySetCanceled();
        var completion = _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = ++_replyId;
        var server = IntPtr.Zero;
        foreach (var instance in new[] { "1.5a", "" })
        {
            server = FindWindow("EVERYTHING_TASKBAR_NOTIFICATION" + (instance.Length == 0 ? "" : "_(" + instance + ")"), null);
            if (server != IntPtr.Zero) { Instance = instance.Length == 0 ? Loc.T("默认") : instance; break; }
        }
        if (server == IntPtr.Zero) throw new IOException(Loc.T("未连接到 Everything。请先启动 Everything（支持 1.5a / 1.4）。"));
        bool pinyinApplied = false;
        using var cancellation = token.Register(() => completion.TrySetCanceled(token));
        await Task.Run(() =>
        {
            if (token.IsCancellationRequested) return;
            var expanded = FileSearchQuery.WithPinyin(query, SupportsPinyin(server));
            if (token.IsCancellationRequested) return;
            pinyinApplied = !expanded.Equals(query, StringComparison.Ordinal);
            var bytes = EverythingProtocol.BuildQuery(unchecked((uint)_replyWindow.ToInt64()), id, expanded, sort, maximum);
            var memory = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                var data = new CopyData { Id = (UIntPtr)18, Size = bytes.Length, Data = memory };
                if (SendMessageTimeout(server, 0x4A, _replyWindow, ref data, 2, 2000, out var accepted) == IntPtr.Zero || accepted == UIntPtr.Zero)
                    completion.TrySetException(new IOException(Loc.T("Everything 没有接受查询，请检查其 IPC 接口是否启用。")));
            }
            finally { Marshal.FreeHGlobal(memory); }
        }, token);
        var page = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        if (id == _replyId) PinyinApplied = pinyinApplied;
        return page;
    }
    private bool SupportsPinyin(IntPtr server)
    {
        GetWindowThreadProcessId(server, out var processId);
        var cached = _capabilities;
        if (cached is not null && cached.Window == server && cached.ProcessId == processId) return cached.Pinyin;
        int VersionPart(int command) => SendMessageTimeoutValue(server, 0x400, new IntPtr(command), IntPtr.Zero, 2, 300, out var value) != IntPtr.Zero ? unchecked((int)value.ToUInt64()) : -1;
        var major = VersionPart(0); if (major < 0) return false;
        var minor = VersionPart(1); if (minor < 0) return false;
        var revision = 0; var build = 0;
        if (major == 1 && minor == 5)
        {
            revision = VersionPart(2); if (revision < 0) return false;
            build = VersionPart(3); if (build < 0) return false;
        }
        var supported = FileSearchQuery.SupportsPinyin(major, minor, revision, build);
        _capabilities = new(server, processId, supported);
        return supported;
    }
    public bool HandleMessage(int message, IntPtr lParam)
    {
        if (message != 0x4A || lParam == IntPtr.Zero || _disposed) return false;
        var data = Marshal.PtrToStructure<CopyData>(lParam);
        var id = data.Id.ToUInt64();
        if (id < 0x51460000 || id > uint.MaxValue) return false;
        if (id != _replyId || _pending is null) return true;
        try
        {
            if (data.Data == IntPtr.Zero || data.Size < 20 || data.Size > 32 * 1024 * 1024) throw new InvalidDataException(Loc.T("Everything 返回数据大小无效。"));
            var bytes = new byte[data.Size];
            Marshal.Copy(data.Data, bytes, 0, bytes.Length);
            _pending.TrySetResult(EverythingProtocol.Parse(bytes));
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentException) { _pending.TrySetException(ex); }
        return true;
    }
    public void Dispose() { _disposed = true; _pending?.TrySetCanceled(); }
    [StructLayout(LayoutKind.Sequential)] private struct CopyData { public UIntPtr Id; public int Size; public IntPtr Data; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, ref CopyData data, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] private static extern IntPtr SendMessageTimeoutValue(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
