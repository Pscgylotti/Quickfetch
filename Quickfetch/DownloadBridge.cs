using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Quickfetch.Core;

namespace Quickfetch;

internal sealed record BridgeReply(bool Ok, string Message, Guid? Id = null);
internal static class DownloadBridge
{
    internal const string ExtensionId = "bbidpnpannkfhebdncjehboepamgobfm";
    internal const string HostName = "com.quickfetch.downloads";
    internal const string FirefoxId = "quickfetch@pscgylotti";
    internal static bool IsNativeArgs(string[] args) => args.Contains("--native-host") || args.FirstOrDefault()?.StartsWith("chrome-extension://", StringComparison.Ordinal) == true || args.Contains(FirefoxId);
    private static string PipeName => "Quickfetch.Downloads." + (System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);
    internal static async Task<T?> Read<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        var size = BitConverter.ToInt32(header); if (size is < 1 or > 65536) throw new IOException("消息长度无效。");
        var body = new byte[size]; await stream.ReadExactlyAsync(body, token); return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    internal static async Task Write<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    internal static async Task Serve(DownloadManager manager, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(30000);
                BridgeReply reply;
                try
                {
                    var request = await Read<DownloadRequest>(pipe, timeout.Token) ?? throw new IOException("下载请求为空。");
                    var probe = await manager.ProbeAsync(request.Url, request.Referer, timeout.Token);
                    reply = new(true, "已加入 Quickfetch 下载队列。", manager.Enqueue(probe, request));
                }
                catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException or ArgumentException or UnauthorizedAccessException)
                { reply = new(false, "Quickfetch 无法接管：" + ex.Message); }
                await Write(pipe, reply, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException) { if (token.IsCancellationRequested) break; }
        }
    }
    private static async Task<BridgeReply> Send(DownloadRequest request, bool start)
    {
        using var timeout = new CancellationTokenSource(35000);
        for (int attempt = 0; attempt < (start ? 12 : 1); attempt++)
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(500, timeout.Token); await Write(pipe, request, timeout.Token);
                return await Read<BridgeReply>(pipe, timeout.Token) ?? new(false, "Quickfetch 未响应。");
            }
            catch (TimeoutException)
            {
                if (start && attempt == 0) StartDesktop();
                if (!start) break;
                await Task.Delay(300, timeout.Token);
            }
        }
        return new(false, "请先启动 Quickfetch 后重试。");
    }
    private static void StartDesktop()
    {
        Exception? error = null;
        var thread = new Thread(() => { try { StartDesktopSta(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        if (!thread.Join(5000)) throw new IOException("桌面启动超时。");
        if (error is not null) throw new IOException("无法从桌面启动 Quickfetch。", error);
    }
    private static void StartDesktopSta()
    {
        object? shell = null, windows = null, desktop = null, document = null, application = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            windows = ((dynamic)shell!).Windows(); int handle = 0;
            desktop = ((dynamic)windows).FindWindowSW(0, 0, 8, ref handle, 1);
            document = ((dynamic)desktop!).Document; application = ((dynamic)document).Application;
            ((dynamic)application).ShellExecute(Environment.ProcessPath!, "--background", AppContext.BaseDirectory, "open", 1);
        }
        finally { foreach (var obj in new[] { application, document, desktop, windows, shell }) if (obj is not null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    }
    internal static int NativeMain(string[] args)
    {
        try
        {
            var firefox = args.Length >= 2 && args[1] == FirefoxId && string.Equals(Path.GetFullPath(args[0]), Path.Combine(Settings.Storage, "native-download-host-firefox.json"), StringComparison.OrdinalIgnoreCase);
            if (!args.Contains("--native-host") && !firefox && args.FirstOrDefault() != "chrome-extension://" + ExtensionId + "/") return 1;
            var request = Read<DownloadRequest>(Console.OpenStandardInput(), CancellationToken.None).GetAwaiter().GetResult();
            if (request is null) return 1;
            BridgeReply reply;
            try { reply = Send(request, true).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or COMException) { reply = new(false, ex.Message); }
            Write(Console.OpenStandardOutput(), reply, CancellationToken.None).GetAwaiter().GetResult(); return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { return 1; }
    }
    internal static string Install()
    {
        var folder = Path.Combine(Settings.Storage, "browser-extensions"); Directory.CreateDirectory(folder);
        foreach (var browser in new[] { "edge", "chrome", "firefox" })
        {
            var destination = Path.Combine(folder, browser); Directory.CreateDirectory(destination);
            foreach (var name in typeof(App).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("Quickfetch.Edge.", StringComparison.Ordinal)))
            {
                var file = name["Quickfetch.Edge.".Length..];
                if (file == (browser == "firefox" ? "manifest.json" : "firefox-manifest.json")) continue;
                if (file == "firefox-manifest.json") file = "manifest.json";
                using var source = typeof(App).Assembly.GetManifestResourceStream(name)!;
                using var target = File.Create(Path.Combine(destination, file)); source.CopyTo(target);
            }
        }
        var manifestPath = Path.Combine(Settings.Storage, "native-download-host.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { name = HostName, description = "Quickfetch download bridge", path = Environment.ProcessPath, type = "stdio", allowed_origins = new[] { "chrome-extension://" + ExtensionId + "/" } }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var browser in new[] { @"Software\Microsoft\Edge", @"Software\Google\Chrome" })
        { using var key = Registry.CurrentUser.CreateSubKey(browser + @"\NativeMessagingHosts\" + HostName); key.SetValue("", manifestPath); }
        var firefoxPath = Path.Combine(Settings.Storage, "native-download-host-firefox.json");
        File.WriteAllText(firefoxPath, JsonSerializer.Serialize(new { name = HostName, description = "Quickfetch download bridge", path = Environment.ProcessPath, type = "stdio", allowed_extensions = new[] { FirefoxId } }, new JsonSerializerOptions { WriteIndented = true }));
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Mozilla\NativeMessagingHosts\" + HostName)) key.SetValue("", firefoxPath);
        return folder;
    }
    internal static string DefaultFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var ptr) == 0)
            try { return Marshal.PtrToStringUni(ptr)!; } finally { Marshal.FreeCoTaskMem(ptr); }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (DownloadBridge.IsNativeArgs(args)) return DownloadBridge.NativeMain(args);
        if (args.Contains("--install-browser-bridge")) { DownloadBridge.Install(); return 0; }
        var app = new App(); app.InitializeComponent(); return app.Run();
    }
}
