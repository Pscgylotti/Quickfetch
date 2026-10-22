using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quickfetch.Core;

namespace Quickfetch;

internal sealed record FilePreview(string Title, string Details, string Text, ImageSource? Image);
internal static class FilePreviewService
{
    internal const int MaxTextBytes = 32768, MaxImageWidth = 640, MaxImageHeight = 480;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static async Task<FilePreview> LoadAsync(FileEntry file, CancellationToken cancellation)
    {
        await Gate.WaitAsync(cancellation);
        var completion = new TaskCompletionSource<FilePreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(Read(file, cancellation)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellation); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { Gate.Release(); }
        }) { IsBackground = true, Name = "Quickfetch file preview" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return await completion.Task;
    }
    private static FilePreview Read(FileEntry file, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var details = new List<string> { file.IsFolder ? Loc.T("文件夹") : Path.GetExtension(file.Path).ToUpperInvariant().TrimStart('.') + Loc.T(" 文件"), Loc.T("大小：") + file.SizeText, Loc.T("修改：") + file.DateText };
        if (file.IsFolder) return new(file.Name, string.Join("\n", details), Loc.T("文件夹预览不递归读取内容。"), FileResultIcons.Get(file));
        var extension = Path.GetExtension(file.Path).ToLowerInvariant();
        string text = ""; ImageSource? image = null;
        if (new[] { ".txt", ".md", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".config", ".cs", ".cpp", ".h", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".ps1", ".bat", ".sql", ".srt", ".vtt" }.Contains(extension))
        {
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[MaxTextBytes]; var length = 0;
            while (length < bytes.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                var read = stream.Read(bytes, length, bytes.Length - length); if (read == 0) break; length += read;
            }
            using var memory = new MemoryStream(bytes, 0, length);
            using var reader = new StreamReader(memory, new UTF8Encoding(false, false), true);
            text = reader.ReadToEnd();
            if (text.Contains('\0')) text = Loc.T("无法预览二进制内容。");
            if (stream.Length > length) text += Loc.T("\n\n…仅显示文件开头 32 KiB。");
        }
        else if (extension is ".exe" or ".dll" or ".sys")
        {
            cancellation.ThrowIfCancellationRequested();
            image = RegisteredAppCatalog.Extract(file.Path, 0x4, 128, 128);
            var version = FileVersionInfo.GetVersionInfo(file.Path);
            Add(details, Loc.T("产品"), version.ProductName); Add(details, Loc.T("描述"), version.FileDescription);
            Add(details, Loc.T("文件版本"), version.FileVersion); Add(details, Loc.T("产品版本"), version.ProductVersion); Add(details, Loc.T("公司"), version.CompanyName);
        }
        else
        {
            var video = new[] { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v" }.Contains(extension);
            var audio = new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma" }.Contains(extension);
            if (video || audio)
            {
                ReadMediaProperties(file.Path, details);
                cancellation.ThrowIfCancellationRequested();
                var probe = FindTool("ffprobe.exe");
                if (probe is not null)
                {
                    try
                    {
                        var json = RunTool(probe, ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_format", "-show_streams", "-of", "json", file.Path], 1024 * 1024, cancellation);
                        ReadProbe(json, details);
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
                var ffmpeg = FindTool("ffmpeg.exe");
                if (ffmpeg is not null)
                {
                    try
                    {
                        var bytes = RunTool(ffmpeg, ["-nostdin", "-v", "error", "-protocol_whitelist", "file,pipe", "-i", file.Path, "-map", "0:v:0", "-frames:v", "1", "-vf", "scale=640:480:force_original_aspect_ratio=decrease", "-f", "image2pipe", "-c:v", "png", "pipe:1"], 2 * 1024 * 1024, cancellation);
                        image = Decode(bytes);
                        details.Add(video ? Loc.T("预览：首个可解码视频帧") : Loc.T("预览：内嵌专辑封面"));
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or ArgumentException) { }
                }
                if (image is null)
                {
                    image = RegisteredAppCatalog.Extract(file.Path, 0x8, MaxImageWidth, MaxImageHeight);
                    details.Add(video ? Loc.T("预览：Windows 缩略图（需对应解码器）") : Loc.T("无可用专辑封面时显示默认音乐图标"));
                }
            }
            else
            {
                cancellation.ThrowIfCancellationRequested();
                image = RegisteredAppCatalog.Extract(file.Path, 0x8, MaxImageWidth, MaxImageHeight);
                if (image is BitmapSource bitmap) details.Add(Loc.F($"预览分辨率：{bitmap.PixelWidth} × {bitmap.PixelHeight}"));
                ReadMediaProperties(file.Path, details);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return new(file.Name, string.Join("\n", details.Distinct()), text, text.Length > 0 ? null : image ?? FileResultIcons.Get(file));
    }
    private static void ReadMediaProperties(string path, List<string> details)
    {
        object? shell = null, folder = null, item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).NameSpace(Path.GetDirectoryName(path)); item = ((dynamic)folder!).ParseName(Path.GetFileName(path));
            foreach (var (property, label) in new[] { ("System.Image.Dimensions", Loc.T("原始分辨率")), ("System.Video.FrameWidth", Loc.T("视频宽度")), ("System.Video.FrameHeight", Loc.T("视频高度")), ("System.Media.Duration", Loc.T("时长")), ("System.Audio.SampleRate", Loc.T("采样率 (Hz)")), ("System.Audio.EncodingBitrate", Loc.T("音频比特率 (bit/s)")), ("System.Video.EncodingBitrate", Loc.T("视频比特率 (bit/s)")), ("System.Music.AlbumTitle", Loc.T("专辑")), ("System.Music.Artist", Loc.T("艺术家")) })
            {
                object? value = ((dynamic)item!).ExtendedProperty(property);
                if (value is null) continue;
                var display = value is Array array ? string.Join(", ", array.Cast<object>()) : Convert.ToString(value);
                if (property == "System.Media.Duration" && ulong.TryParse(display, out var ticks)) display = TimeSpan.FromTicks((long)Math.Min(ticks, (ulong)long.MaxValue)).ToString();
                Add(details, label, display);
            }
        }
        catch (COMException) { }
        finally { Release(item); Release(folder); Release(shell); }
    }
    private static void ReadProbe(byte[] bytes, List<string> details)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var duration) && double.TryParse(duration.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds >= 0 && seconds < 86400000)
                details.Add(Loc.T("媒体时长：") + TimeSpan.FromSeconds(seconds).ToString(@"d\.hh\:mm\:ss"));
            if (format.TryGetProperty("bit_rate", out var rate)) details.Add(Loc.T("总比特率 (bit/s)：") + rate.ToString());
            if (format.TryGetProperty("tags", out var tags)) foreach (var tag in tags.EnumerateObject())
                if (tag.Name.ToLowerInvariant() is "album" or "artist" or "title") Add(details, tag.Name, tag.Value.ToString());
        }
        if (document.RootElement.TryGetProperty("streams", out var streams)) foreach (var stream in streams.EnumerateArray())
        {
            if (stream.TryGetProperty("width", out var width) && stream.TryGetProperty("height", out var height)) details.Add(Loc.F($"媒体分辨率：{width} × {height}"));
            if (stream.TryGetProperty("sample_rate", out var sampleRate)) details.Add(Loc.T("采样率 (Hz)：") + sampleRate.ToString());
            if (stream.TryGetProperty("codec_name", out var codec)) details.Add(Loc.T("编码：") + codec.ToString());
        }
    }
    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = MaxImageWidth; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    private static string? FindTool(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(folder => Path.Combine(folder.Trim('"'), name)).FirstOrDefault(File.Exists);
    private static byte[] RunTool(string tool, string[] arguments, int limit, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException(Loc.T("无法启动媒体预览。"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(4000);
        using var cancel = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
        // Drain stderr without retaining unbounded parser diagnostics.
        var drain = Task.Run(async () => { var buffer = new char[1024]; while (await process.StandardError.ReadAsync(buffer) > 0) { } });
        using var output = new MemoryStream(); var block = new byte[8192]; int count;
        while ((count = process.StandardOutput.BaseStream.Read(block, 0, block.Length)) > 0)
        {
            if (output.Length + count > limit) { process.Kill(true); throw new IOException(Loc.T("媒体预览超过读取限制。")); }
            output.Write(block, 0, count);
        }
        process.WaitForExit(); drain.GetAwaiter().GetResult(); cancellation.ThrowIfCancellationRequested();
        if (timeout.IsCancellationRequested || process.ExitCode != 0) throw new IOException(Loc.T("媒体预览不可用或超时。"));
        return output.ToArray();
    }
    private static void Add(List<string> details, string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) details.Add(name + "：" + value[..Math.Min(512, value.Length)]); }
    private static void Release(object? obj) { if (obj is not null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
}
