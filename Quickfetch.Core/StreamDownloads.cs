using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Quickfetch.Core;

public sealed class StreamPart
{
    public string Url { get; set; } = "";
    public long? Offset { get; set; }
    public long? Length { get; set; }
    public string? KeyUrl { get; set; }
    public string IV { get; set; } = "";
    public bool Done { get; set; }
    public long Bytes { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public int? Worker { get; set; }
    public StreamPart Clone() => new() { Url = Url, Offset = Offset, Length = Length, KeyUrl = KeyUrl, IV = IV, Done = Done, Bytes = Bytes, Worker = Worker };
    internal string Identity => Url + "|" + Offset + "|" + Length + "|" + KeyUrl + "|" + IV;
}

public sealed partial class DownloadManager
{
    public static string? FindFFmpeg(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => Path.Combine(folder.Trim('"'), OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg")).FirstOrDefault(File.Exists);
    }
    private static string PartFolder(DownloadJob job) => job.PartialPath + ".hls";
    private static string PartPath(DownloadJob job, int index) => Path.Combine(PartFolder(job), index.ToString("D6") + ".bin");
    private static string Resolve(Uri root, string reference)
    {
        var uri = new Uri(root, reference);
        if (!TryUrl(uri.AbsoluteUri, out _)) throw new IOException(TextLocalization.T("清单包含不支持的资源地址。"));
        return uri.AbsoluteUri;
    }
    private async Task<string> Text(HttpClient client, string url, string referer, CancellationToken cancellation)
    {
        using var request = Request(new Uri(url), referer);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(30000);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
        using var source = await response.Content.ReadAsStreamAsync(timeout.Token); using var memory = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0) { if (memory.Length + count > 2 * 1024 * 1024) throw new IOException(TextLocalization.T("媒体清单超过 2 MiB 限制。")); memory.Write(buffer, 0, count); }
        return Encoding.UTF8.GetString(memory.ToArray());
    }
    private static Dictionary<string, string> Attributes(string line) => Regex.Matches(line[(line.IndexOf(':') + 1)..], "([A-Z0-9-]+)=(\"[^\"]*\"|[^,]*)")
        .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim('"'), StringComparer.Ordinal);
    internal static List<StreamPart> ParsePlaylist(string playlist, Uri root)
    {
        if (!playlist.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("#EXTM3U", StringComparison.Ordinal)) throw new IOException(TextLocalization.T("不是有效的 HLS 清单。"));
        if (!playlist.Contains("#EXT-X-ENDLIST", StringComparison.Ordinal)) throw new IOException(TextLocalization.T("当前仅支持有结束标记的点播 HLS；直播清单请在浏览器中播放。"));
        if (playlist.Contains("#EXT-X-SESSION-KEY", StringComparison.Ordinal) || playlist.Contains("#EXT-X-MEDIA:", StringComparison.Ordinal)) throw new IOException(TextLocalization.T("清单包含独立音轨或会话加密，需要专用媒体工具。"));
        var parts = new List<StreamPart>(); string? key = null; string? iv = null; long sequence = 0, offset = 0; long? length = null;
        foreach (var raw in playlist.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:")) sequence = long.Parse(line.Split(':')[1], CultureInfo.InvariantCulture);
            else if (line.StartsWith("#EXT-X-KEY:"))
            {
                var attributes = Attributes(line); var method = attributes.GetValueOrDefault("METHOD");
                if (method == "NONE") { key = null; iv = null; continue; }
                if (method != "AES-128" || attributes.GetValueOrDefault("KEYFORMAT", "identity") != "identity") throw new IOException(TextLocalization.T("不支持 DRM / SAMPLE-AES 加密流。"));
                key = Resolve(root, attributes.GetValueOrDefault("URI") ?? throw new IOException(TextLocalization.T("AES 密钥地址缺失。"))); iv = attributes.GetValueOrDefault("IV");
            }
            else if (line.StartsWith("#EXT-X-MAP:"))
            {
                var attributes = Attributes(line); var map = new StreamPart { Url = Resolve(root, attributes["URI"]) };
                if (attributes.TryGetValue("BYTERANGE", out var bounds)) { var values = bounds.Split('@'); map.Length = long.Parse(values[0]); map.Offset = values.Length > 1 ? long.Parse(values[1]) : 0; }
                if (key is not null) throw new IOException(TextLocalization.T("暂不支持加密的 fMP4 初始化段。"));
                parts.Add(map);
            }
            else if (line.StartsWith("#EXT-X-BYTERANGE:"))
            {
                var values = line[(line.IndexOf(':') + 1)..].Split('@'); length = long.Parse(values[0]); if (values.Length > 1) offset = long.Parse(values[1]);
                if (length <= 0 || offset < 0 || offset > long.MaxValue - length) throw new IOException(TextLocalization.T("无效的 HLS 字节区间。"));
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                var sequenceBytes = new byte[16]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(sequenceBytes.AsSpan(8), sequence++);
                var initialization = iv is null ? Convert.ToHexString(sequenceBytes) : iv.Replace("0x", "", StringComparison.OrdinalIgnoreCase).PadLeft(32, '0');
                if (key is not null && (initialization.Length != 32 || initialization.Any(c => !Uri.IsHexDigit(c)))) throw new IOException(TextLocalization.T("AES IV 无效。"));
                parts.Add(new() { Url = Resolve(root, line), Offset = length is not null ? offset : null, Length = length, KeyUrl = key, IV = key is null ? "" : initialization });
                if (length is long count) offset += count; length = null;
                if (parts.Count > 10000) throw new IOException(TextLocalization.T("清单超过 10,000 个分片限制。"));
            }
        }
        if (parts.Count == 0) throw new IOException(TextLocalization.T("HLS 清单没有媒体分片。"));
        return parts;
    }
    private async Task TransferStream(DownloadJob job, CancellationToken cancellation)
    {
        var ffmpeg = FindFFmpeg(_options().FFmpegPath) ?? throw new IOException(TextLocalization.T("HLS 合并需要 FFmpeg，请在下载配置中填写路径或加入 PATH。"));
        using var client = Client(); var url = job.Url;
        List<StreamPart>? parsed = null;
        for (int depth = 0; depth < 5; depth++)
        {
            var text = await Text(client, url, job.Referer, cancellation);
            var lines = text.Split('\n').Select(s => s.Trim()).ToArray();
            if (!text.Contains("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) { parsed = ParsePlaylist(text, new Uri(url)); break; }
            if (text.Contains("#EXT-X-MEDIA:", StringComparison.Ordinal)) throw new IOException(TextLocalization.T("该主清单有独立音轨，当前不支持自动合并。"));
            var variants = new List<(long Bandwidth, string Url)>();
            for (int i = 0; i + 1 < lines.Length; i++) if (lines[i].StartsWith("#EXT-X-STREAM-INF:") && !lines[i + 1].StartsWith('#'))
            { long.TryParse(Attributes(lines[i]).GetValueOrDefault("BANDWIDTH"), out var bandwidth); variants.Add((bandwidth, Resolve(new Uri(url), lines[i + 1]))); }
            if (variants.Count == 0) throw new IOException(TextLocalization.T("主清单没有可用的视频变体。"));
            url = variants.OrderByDescending(v => v.Bandwidth).First().Url;
        }
        if (parsed is null) throw new IOException(TextLocalization.T("HLS 清单嵌套过深。"));
        Directory.CreateDirectory(PartFolder(job));
        lock (_gate)
        {
            var same = job.Parts.Count == parsed.Count && job.Parts.Select(p => p.Identity).SequenceEqual(parsed.Select(p => p.Identity));
            if (!same) job.Parts = parsed;
            for (int i = 0; i < job.Parts.Count; i++)
            {
                var part = job.Parts[i]; part.Worker = null;
                if (!part.Done || !File.Exists(PartPath(job, i)) || new FileInfo(PartPath(job, i)).Length != part.Bytes) { part.Done = false; part.Bytes = 0; }
            }
            job.WorkerCount = 8; job.Size = null; job.Phase = TextLocalization.T("HLS 分片下载"); Save();
        }
        var keys = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<byte[]>>>();
        async Task<byte[]> Key(string keyUrl)
        {
            return await keys.GetOrAdd(keyUrl, u => new Lazy<Task<byte[]>>(async () =>
            {
                using var request = Request(new Uri(u), job.Referer); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(30000);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); var bytes = new byte[17]; var total = 0; int read;
                while (total < 17 && (read = await stream.ReadAsync(bytes.AsMemory(total), timeout.Token)) > 0) total += read;
                if (total != 16) throw new IOException(TextLocalization.T("AES-128 密钥必须是 16 字节。")); return bytes[..16];
            })).Value;
        }
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        async Task Worker(int worker)
        {
            try
            {
                while (true)
                {
                    int index; StreamPart part;
                    lock (_gate)
                    {
                        index = job.Parts.FindIndex(p => !p.Done && p.Worker is null); if (index < 0) return;
                        part = job.Parts[index]; part.Worker = worker; job.Pointers[worker] = index;
                    }
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            var keyBytes = part.KeyUrl is null ? null : await Key(part.KeyUrl);
                            using var request = Request(new Uri(part.Url), job.Referer);
                            if (part.Offset is long offset && part.Length is long length) request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(failed.Token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
                            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
                            if (part.Offset is long expected && (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != expected || response.Content.Headers.ContentRange.To != expected + part.Length - 1)) throw new InvalidDataException(TextLocalization.T("HLS 分片返回了错误的字节区间。"));
                            using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                            using var output = new FileStream(PartPath(job, index), FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, true);
                            using var aes = part.KeyUrl is not null ? Aes.Create() : null;
                            if (aes is not null) { aes.Key = keyBytes!; aes.IV = Convert.FromHexString(part.IV); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; }
                            using var decrypted = aes is null ? null : new CryptoStream(source, aes.CreateDecryptor(), CryptoStreamMode.Read, true);
                            var input = (Stream?)decrypted ?? source; var buffer = new byte[BufferSize]; int count;
                            lock (_gate) part.Bytes = 0;
                            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                            { await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token); lock (_gate) { part.Bytes += count; Checkpoint(); } }
                            if (aes is null && part.Length is long expectedLength && part.Bytes != expectedLength) throw new IOException(TextLocalization.T("HLS 分片长度不符。"));
                            output.Flush(true);
                            lock (_gate) { part.Done = true; part.Worker = null; job.Pointers[worker] = index + 1; Checkpoint(); }
                            break;
                        }
                        catch (Exception ex) when ((ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException) && ex is not InvalidDataException && !failed.IsCancellationRequested && attempt < 3)
                        { await Task.Delay(TimeSpan.FromSeconds((attempt + 1) * 2), failed.Token); }
                    }
                }
            }
            catch { failed.Cancel(); throw; }
        }
        var tasks = Enumerable.Range(0, Workers).Select(Worker).ToArray();
        try { await Task.WhenAll(tasks); }
        catch { var task = tasks.FirstOrDefault(t => t.Exception is not null); if (task?.Exception?.InnerException is Exception ex) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw(); throw; }
        cancellation.ThrowIfCancellationRequested(); lock (_gate) { job.Phase = TextLocalization.T("FFmpeg 合并中"); Save(); }
        await MergeStream(ffmpeg, job, cancellation);
        lock (_gate) { job.Phase = TextLocalization.T("合并完成"); DeleteParts(job); }
    }
    private async Task MergeStream(string ffmpeg, DownloadJob job, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y", "-protocol_whitelist", "pipe", "-fflags", "+genpts", "-i", "pipe:0", "-map", "0", "-c", "copy", "-movflags", "+faststart", "-f", "mp4", job.PartialPath }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException(TextLocalization.T("无法启动 FFmpeg。"));
        using var cancel = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
        var errors = Task.Run(async () => { var builder = new StringBuilder(); var buffer = new char[1024]; int count; while ((count = await process.StandardError.ReadAsync(buffer)) > 0) { builder.Append(buffer, 0, count); if (builder.Length > 4096) builder.Remove(0, builder.Length - 4096); } return builder.ToString(); });
        try
        {
            for (int i = 0; i < job.Parts.Count; i++) { using var file = File.OpenRead(PartPath(job, i)); await file.CopyToAsync(process.StandardInput.BaseStream, cancellation); }
            process.StandardInput.Close(); await process.WaitForExitAsync(cancellation);
            var error = await errors; if (process.ExitCode != 0) throw new IOException(TextLocalization.T("FFmpeg 合并失败：") + error);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            await process.WaitForExitAsync(CancellationToken.None); await errors; throw;
        }
    }
    private static void DeleteParts(DownloadJob job)
    {
        for (int i = 0; i < job.Parts.Count; i++) { try { File.Delete(PartPath(job, i)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        try { if (Directory.Exists(PartFolder(job))) Directory.Delete(PartFolder(job), false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
