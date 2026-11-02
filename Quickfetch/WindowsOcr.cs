using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Quickfetch;

internal sealed record CaptureWord(string Text, double X, double Y, double Width, double Height, int Line);
internal sealed record CaptureText(CaptureWord[] Words, string Language);
internal static class WindowsOcr
{
    internal static string RuntimeFolder => Path.Combine(AppContext.BaseDirectory, "windows-ai-ocr");
    internal static async Task<CaptureText> Recognize(BitmapSource image, CancellationToken cancellation)
    {
        using var session = new WindowsAiOcrSession(); return await session.Recognize(image, cancellation);
    }
    internal static async Task Register(CancellationToken cancellation)
    {
        var script = Path.Combine(RuntimeFolder, "Register-WindowsAiOcr.ps1");
        if (!File.Exists(script)) throw new IOException(Loc.T("Windows AI OCR 组件未安装。"));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-File", script, "-RuntimeDirectory", RuntimeFolder }) start.ArgumentList.Add(value);
        using var process = Process.Start(start) ?? throw new IOException(Loc.T("无法注册 Windows AI OCR。"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var stop = timeout.Token.Register(() => WindowsAiOcrSession.Kill(process));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token); await output; var diagnostic = await error;
        if (process.ExitCode != 0) throw new IOException(Loc.T("无法注册 Windows AI OCR：") + diagnostic);
    }
}

internal sealed class WindowsAiOcrSession : IDisposable
{
    private Process? _process;
    private bool _ready, _closed;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idle;
    internal int? ProcessId => _process is { HasExited: false } process ? process.Id : null;
    internal WindowsAiOcrSession() => _idle = new Timer(_ => { if (!_gate.Wait(0)) return; try { Stop(); } finally { _gate.Release(); } }, null, Timeout.Infinite, Timeout.Infinite);
    internal async Task<CaptureText> Recognize(BitmapSource image, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this); cancellation.ThrowIfCancellationRequested(); _idle.Change(Timeout.Infinite, Timeout.Infinite);
            if (_process is null || _process.HasExited)
            {
                Stop(); var executable = Path.Combine(WindowsOcr.RuntimeFolder, "Quickfetch.WindowsAI.Ocr.exe");
                if (!File.Exists(executable)) throw new IOException(Loc.T("Windows AI OCR 组件未安装。"));
                var start = new ProcessStartInfo(executable) { WorkingDirectory = WindowsOcr.RuntimeFolder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
                _process = Process.Start(start) ?? throw new IOException(Loc.T("无法启动 Windows AI OCR。"));
                _process.ErrorDataReceived += (_, _) => { }; _process.BeginErrorReadLine();
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromMinutes(3));
            using var stop = timeout.Token.Register(() => { var process = _process; if (process is not null) Kill(process); });
            try
            {
                if (!_ready) { using var opened = await Ask(new { action = "open" }, timeout.Token); EnsureOk(opened.RootElement); _ready = true; }
                // Leave room for WPF's outward pixel rounding at fractional scales.
                var scale = Math.Min(1, Math.Sqrt(3_990_000.0 / ((long)image.PixelWidth * image.PixelHeight)));
                BitmapSource input = image;
                if (scale < 1)
                {
                    do { input = new TransformedBitmap(image, new ScaleTransform(scale, scale)); input.Freeze(); scale *= .999; }
                    while ((long)input.PixelWidth * input.PixelHeight > 4_000_000);
                }
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(input)); using var png = new MemoryStream(); encoder.Save(png);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                using var response = await Ask(new { action = "recognize", png = Convert.ToBase64String(png.GetBuffer(), 0, (int)png.Length) }, timeout.Token); EnsureOk(response.RootElement);
                return ReadText(response.RootElement.GetProperty("data"), image.PixelWidth, image.PixelHeight);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { Stop(); throw new IOException(Loc.T("Windows AI OCR 超时，请重试。")); }
            catch { Stop(); throw; }
        }
        finally { try { if (!_closed) _idle.Change(30_000, Timeout.Infinite); } catch (ObjectDisposedException) when (_closed) { } finally { _gate.Release(); } }
    }
    private async Task<JsonDocument> Ask<T>(T request, CancellationToken cancellation)
    {
        var process = _process ?? throw new IOException(Loc.T("Windows AI OCR 进程已退出。"));
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellation); await process.StandardInput.FlushAsync(cancellation);
        var line = await process.StandardOutput.ReadLineAsync(cancellation);
        if (line is null || line.Length > 8 * 1024 * 1024) throw new IOException(Loc.T("Windows AI OCR 进程已退出或返回无效结果。"));
        return JsonDocument.Parse(line);
    }
    private static void EnsureOk(JsonElement response)
    {
        if (response.TryGetProperty("ok", out var ok) && ok.GetBoolean()) return;
        var code = response.TryGetProperty("error", out var error) ? error.GetString() : "invalid-result";
        var detail = response.TryGetProperty("detail", out var message) ? message.GetString() : "";
        var stage = response.TryGetProperty("stage", out var step) ? step.GetString() : "";
        throw new IOException((code == "windows-ai-identity" ? Loc.T("Windows AI OCR 需要注册组件身份，请点击“注册 AI 组件”。") : Loc.T("Windows AI OCR 无法使用：")) + $" {code} [{stage}] {detail}");
    }
    internal static CaptureText ReadText(JsonElement data, int width, int height)
    {
        double sourceWidth = data.GetProperty("width").GetDouble(), sourceHeight = data.GetProperty("height").GetDouble();
        if (!double.IsFinite(sourceWidth + sourceHeight) || sourceWidth < 1 || sourceHeight < 1) throw new IOException(Loc.T("Windows AI OCR 返回无效尺寸。"));
        var words = new List<CaptureWord>(); int lineIndex = 0;
        foreach (var line in data.GetProperty("lines").EnumerateArray())
        {
            var segments = line.GetProperty("words").EnumerateArray().ToArray();
            if (segments.Length == 0) Add(line); else foreach (var word in segments) Add(word);
            lineIndex++;
        }
        return new(words.ToArray(), "Windows AI OCR · NPU");
        void Add(JsonElement word)
        {
            var polygon = word.GetProperty("polygon").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            if (polygon.Length != 8 || polygon.Any(value => !double.IsFinite(value)) || words.Count >= 8000) return;
            var x = Math.Clamp(new[] { polygon[0], polygon[2], polygon[4], polygon[6] }.Min() * width / sourceWidth, 0, width);
            var y = Math.Clamp(new[] { polygon[1], polygon[3], polygon[5], polygon[7] }.Min() * height / sourceHeight, 0, height);
            var right = Math.Clamp(new[] { polygon[0], polygon[2], polygon[4], polygon[6] }.Max() * width / sourceWidth, x, width);
            var bottom = Math.Clamp(new[] { polygon[1], polygon[3], polygon[5], polygon[7] }.Max() * height / sourceHeight, y, height);
            if (right > x && bottom > y) words.Add(new(word.GetProperty("text").GetString() ?? "", x, y, right - x, bottom - y, lineIndex));
        }
    }
    private void Stop() { _ready = false; var process = _process; _process = null; if (process is not null) { Kill(process); process.Dispose(); } }
    internal static void Kill(Process process) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
    public void Dispose() { _closed = true; _idle.Dispose(); var process = _process; if (process is not null) Kill(process); if (_gate.Wait(0)) { try { Stop(); } finally { _gate.Release(); } } }
}
