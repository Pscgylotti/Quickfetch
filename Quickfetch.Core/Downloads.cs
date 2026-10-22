using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Quickfetch.Core;

public enum DownloadStatus { Queued, Running, Paused, Completed, Failed, Cancelled }
public sealed class DownloadOptions
{
    public string Folder { get; set; } = "";
    public string ProxyMode { get; set; } = "system";
    public string ProxyAddress { get; set; } = "";
    public string FFmpegPath { get; set; } = "";
}
public sealed class DownloadRange
{
    public long Start { get; set; }
    public long End { get; set; }
    public long Next { get; set; }
    [JsonIgnore] public int? Worker { get; set; }
    public DownloadRange Clone() => new() { Start = Start, End = End, Next = Next, Worker = Worker };
}
public sealed class DownloadJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Referer { get; set; } = "";
    public DownloadStatus Status { get; set; }
    public long Order { get; set; }
    public long? Size { get; set; }
    public bool Ranged { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? Modified { get; set; }
    public List<DownloadRange> Ranges { get; set; } = [];
    public long[] Pointers { get; set; } = new long[8];
    public long SingleBytes { get; set; }
    public string Error { get; set; } = "";
    public string StreamKind { get; set; } = "";
    public string Phase { get; set; } = "";
    public int WorkerCount { get; set; } = 8;
    public List<StreamPart> Parts { get; set; } = [];
    public string CompletedPath { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    [JsonIgnore] public long Downloaded => StreamKind.Length > 0 ? Parts.Sum(p => p.Bytes) : Ranged ? Ranges.Sum(r => r.Next - r.Start) : SingleBytes;
    [JsonIgnore] public string PartialPath => Path.Combine(Folder, ".quickfetch-" + Id.ToString("N") + ".part");
    public DownloadJob Clone() => new() { Id = Id, Url = Url, Name = Name, Folder = Folder, Referer = Referer, Status = Status, Order = Order, Size = Size, Ranged = Ranged, ETag = ETag, Modified = Modified, Ranges = Ranges.Select(r => r.Clone()).ToList(), Pointers = (long[])Pointers.Clone(), SingleBytes = SingleBytes, Error = Error, StreamKind = StreamKind, Phase = Phase, WorkerCount = WorkerCount, Parts = Parts.Select(p => p.Clone()).ToList(), CompletedPath = CompletedPath, Created = Created };
}
public sealed record DownloadProbe(Uri Url, string Name, long? Size, bool Ranged, string? ETag, DateTimeOffset? Modified, bool IsFile, string StreamKind = "");
public sealed record DownloadRequest(string Url, string? Name = null, string? Referer = null, long? ExpectedSize = null);

// One queued task at a time; only active transfers do work or write checkpoints.
public sealed partial class DownloadManager : IDisposable
{
    private readonly object _gate = new();
    private readonly List<DownloadJob> _jobs = [];
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<DownloadOptions> _options;
    private readonly string _state;
    private readonly Task _pump;
    private CancellationTokenSource? _activeCancellation;
    private DownloadJob? _active;
    private SafeFileHandle? _activeFile;
    private long _order, _lastCheckpoint;
    public event Action<DownloadJob>? Completed;
    public event Action<DownloadJob>? Added;
    public const int Workers = 8;
    private const int BufferSize = 64 * 1024;
    public DownloadManager(string state, Func<DownloadOptions> options)
    {
        _state = state; _options = options;
        try
        {
            if (File.Exists(state) && new FileInfo(state).Length < 16 * 1024 * 1024)
                foreach (var job in JsonSerializer.Deserialize<List<DownloadJob>>(File.ReadAllText(state)) ?? [])
                {
                    if (!Valid(job)) continue;
                    if (job.Status == DownloadStatus.Running) job.Status = DownloadStatus.Queued;
                    _jobs.Add(job); _order = Math.Max(_order, job.Order);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        _pump = Task.Run(Pump);
        Wake();
    }
    public static bool TryUrl(string text, out Uri uri)
    {
        text = text.Trim();
        if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) text = "https://" + text;
        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https" && parsed.UserInfo.Length == 0)
        { uri = parsed; return true; }
        uri = null!; return false;
    }
    public static string SafeName(string? candidate)
    {
        var name = (candidate ?? "download.bin").Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(c => c >= 32 && !"<>:\"/\\|?*".Contains(c)).ToArray()).Trim().TrimEnd('.', ' ');
        if (name.Length > 160) name = name[..140] + Path.GetExtension(name)[..Math.Min(16, Path.GetExtension(name).Length)];
        if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase)) name = "_" + name;
        return name;
    }
    private static bool Valid(DownloadJob job) => job is not null && job.Id != Guid.Empty && TryUrl(job.Url, out _) && !string.IsNullOrWhiteSpace(job.Folder)
        && Path.IsPathFullyQualified(job.Folder) && job.Name == SafeName(job.Name) && job.Pointers is { Length: 8 }
        && job.StreamKind is not null && job.Parts is not null && job.Parts.Count <= 10000 && job.Parts.All(p => p is not null && TryUrl(p.Url, out _) && p.Bytes >= 0)
        && job.Ranges is not null && job.Ranges.Count <= 2048 && job.Ranges.All(r => r.Start >= 0 && r.Next >= r.Start && r.End >= r.Start && r.Next <= r.End + 1 && r.End < job.Size);
    private HttpClient Client()
    {
        var options = _options();
        var handler = new SocketsHttpHandler { MaxConnectionsPerServer = Workers, AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(20), PooledConnectionLifetime = TimeSpan.FromMinutes(10), UseProxy = options.ProxyMode != "none" };
        if (options.ProxyMode == "custom")
        {
            if (!Uri.TryCreate(options.ProxyAddress, UriKind.Absolute, out var proxy) || proxy.Scheme is not ("http" or "https" or "socks5") || proxy.UserInfo.Length != 0) throw new IOException("代理地址无效；支持 http / https / socks5，无内嵌账号密码。");
            handler.Proxy = new WebProxy(proxy);
        }
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
    private static HttpRequestMessage Request(Uri url, string? referer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Quickfetch/0.0.4"); request.Headers.AcceptEncoding.ParseAdd("identity");
        if (TryUrl(referer ?? "", out var refUri)) request.Headers.Referrer = refUri;
        return request;
    }
    public async Task<DownloadProbe> ProbeAsync(string text, string? referer = null, CancellationToken token = default)
    {
        if (!TryUrl(text, out var uri)) throw new IOException("请输入 HTTP 或 HTTPS 网址。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var client = Client(); using var request = Request(uri, referer); request.Headers.Range = new RangeHeaderValue(0, 0);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var disposition = response.Content.Headers.ContentDisposition;
        var media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        var final = response.RequestMessage!.RequestUri!;
        var streamKind = media is "application/vnd.apple.mpegurl" or "application/x-mpegurl" or "audio/mpegurl" || final.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ? "hls" : "";
        var file = disposition?.DispositionType.Equals("attachment", StringComparison.OrdinalIgnoreCase) == true || media is not null && media is not ("text/html" or "application/xhtml+xml");
        var range = response.Content.Headers.ContentRange;
        var ranged = response.StatusCode == HttpStatusCode.PartialContent && range is { From: 0, To: 0, Length: > 0 } && response.Content.Headers.ContentEncoding.Count == 0;
        var size = ranged ? range!.Length : response.StatusCode == HttpStatusCode.OK ? response.Content.Headers.ContentLength : null;
        var raw = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"') ?? Uri.UnescapeDataString(final.AbsolutePath.Split('/').Last());
        return new(final, streamKind.Length > 0 ? SafeName(Path.GetFileNameWithoutExtension(raw) + ".mp4") : SafeName(raw), streamKind.Length > 0 ? null : size, streamKind.Length == 0 && ranged, response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : null, response.Content.Headers.LastModified, file || streamKind.Length > 0, streamKind);
    }
    public Guid Enqueue(DownloadProbe probe, DownloadRequest request)
    {
        if (!probe.IsFile) throw new IOException("链接返回网页，需要登录或不能直接下载。");
        if (request.ExpectedSize is > 0 && probe.Size is long expected && probe.StreamKind.Length == 0 && request.ExpectedSize != expected) throw new IOException("文件长度与浏览器任务不一致，保留浏览器下载。");
        var folder = Path.GetFullPath(_options().Folder); Directory.CreateDirectory(folder);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) throw new IOException("下载服务已停止。");
            if (_jobs.Count >= 1000) throw new IOException("下载记录已达 1,000 项，请先清空已结束记录。");
            var job = new DownloadJob { Url = probe.Url.AbsoluteUri, Name = SafeName(probe.StreamKind.Length > 0 || string.IsNullOrWhiteSpace(request.Name) ? probe.Name : request.Name), Folder = folder, Referer = request.Referer ?? "", Size = probe.Size, Ranged = probe.Ranged, ETag = probe.ETag, Modified = probe.Modified, StreamKind = probe.StreamKind, Order = ++_order, Status = DownloadStatus.Queued };
            _jobs.Add(job);
            try { Save(); } catch { _jobs.Remove(job); throw; }
            Wake();
            try { Added?.Invoke(job.Clone()); } catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
            return job.Id;
        }
    }
    public DownloadJob[] Snapshot() { lock (_gate) return _jobs.OrderBy(j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled ? 1 : 0).ThenBy(j => j.Order).Select(j => j.Clone()).ToArray(); }
    public void Pause(Guid id)
    {
        lock (_gate)
        {
            var job = _jobs.Find(j => j.Id == id); if (job is null || job.Status is not (DownloadStatus.Queued or DownloadStatus.Running)) return;
            job.Status = DownloadStatus.Paused; if (_active == job) _activeCancellation?.Cancel(); else Save(); Wake();
        }
    }
    public void Resume(Guid id)
    {
        lock (_gate)
        {
            var job = _jobs.Find(j => j.Id == id); if (job is null || job.Status is not (DownloadStatus.Paused or DownloadStatus.Failed)) return;
            job.Status = DownloadStatus.Queued; job.Error = ""; job.Order = ++_order;
            if (_active != job) Save(); Wake();
        }
    }
    public void Cancel(Guid id)
    {
        lock (_gate)
        {
            var job = _jobs.Find(j => j.Id == id); if (job is null || job.Status == DownloadStatus.Completed) return;
            if (job.Status == DownloadStatus.Cancelled) return;
            job.Status = DownloadStatus.Cancelled;
            if (_active == job) _activeCancellation?.Cancel(); else { DeletePartial(job); Save(); }
            Wake();
        }
    }
    public void ClearHistory() { lock (_gate) { _jobs.RemoveAll(j => j != _active && j.Status is DownloadStatus.Completed or DownloadStatus.Cancelled); Save(); } }
    private void Wake() { if (_wake.CurrentCount == 0) try { _wake.Release(); } catch (SemaphoreFullException) { } }
    private void Save()
    {
        if (_activeFile is not null) RandomAccess.FlushToDisk(_activeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(_state)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_jobs);
        if (bytes.Length >= 16 * 1024 * 1024) throw new IOException("下载历史过大，请清理已结束记录后重试。");
        var temporary = _state + ".tmp"; File.WriteAllBytes(temporary, bytes); File.Move(temporary, _state, true);
    }
    private async Task Pump()
    {
        while (!_stop.IsCancellationRequested)
        {
            DownloadJob? job;
            lock (_gate)
            {
                job = _jobs.Where(j => j.Status == DownloadStatus.Queued).OrderBy(j => j.Order).FirstOrDefault();
                if (job is not null) { _active = job; job.Status = DownloadStatus.Running; _activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); }
            }
            if (job is null) { try { await _wake.WaitAsync(_stop.Token); } catch (OperationCanceledException) { break; } continue; }
            try
            {
                await Transfer(job, _activeCancellation!.Token);
                lock (_gate) if (job.Status == DownloadStatus.Running)
                {
                    var destination = UniquePath(job.Folder, job.Name);
                    File.Move(job.PartialPath, destination, false);
                    if (OperatingSystem.IsWindows()) try { File.WriteAllText(destination + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
                    job.CompletedPath = destination; job.Status = DownloadStatus.Completed;
                }
                if (job.Status == DownloadStatus.Completed) { try { Completed?.Invoke(job.Clone()); } catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { } }
            }
            catch (OperationCanceledException) { lock (_gate) if (job.Status == DownloadStatus.Running && !_stop.IsCancellationRequested) { job.Status = DownloadStatus.Failed; job.Error = "连接超时，请继续重试。"; } }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { lock (_gate) if (job.Status == DownloadStatus.Running) { job.Status = DownloadStatus.Failed; job.Error = ex.Message; } }
            finally
            {
                lock (_gate)
                {
                    if (_stop.IsCancellationRequested && job.Status == DownloadStatus.Running) job.Status = DownloadStatus.Queued;
                    foreach (var range in job.Ranges) range.Worker = null;
                    foreach (var part in job.Parts) part.Worker = null;
                    if (job.Status == DownloadStatus.Cancelled) DeletePartial(job);
                    _activeCancellation?.Dispose(); _activeCancellation = null; _active = null; _activeFile = null;
                    try { Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { job.Error = "保存队列失败：" + ex.Message; }
                }
            }
        }
    }
    private async Task Transfer(DownloadJob job, CancellationToken cancellation)
    {
        if (job.StreamKind.Length > 0) { await TransferStream(job, cancellation); return; }
        using var client = Client();
        var fresh = await ProbeAsync(job.Url, job.Referer, cancellation);
        if (!fresh.IsFile) throw new IOException("链接已失效或返回登录网页。");
        lock (_gate)
        {
            if (job.Downloaded > 0 && (!fresh.Ranged || fresh.ETag is null && fresh.Modified is null)) job.Phase = "服务器无法安全续传，恢复时从头下载";
            else if (job.Downloaded > 0 && (fresh.Size != job.Size || fresh.ETag != job.ETag || fresh.Modified != job.Modified)) job.Phase = "源文件已变化，从头重新下载";
            if (!File.Exists(job.PartialPath) || fresh.Size != job.Size || fresh.ETag != job.ETag || fresh.Modified != job.Modified || !fresh.Ranged || (job.Downloaded > 0 && fresh.ETag is null && fresh.Modified is null))
            { job.Ranges.Clear(); job.SingleBytes = 0; job.Pointers = new long[Workers]; }
            job.Size = fresh.Size; job.Ranged = fresh.Ranged; job.ETag = fresh.ETag; job.Modified = fresh.Modified;
            if (job.Ranged && job.Ranges.Count == 0 && job.Size is > 0)
            {
                var count = (int)Math.Min(job.Size.Value < 10 * 1024 * 1024 ? 1 : Workers, job.Size.Value);
                job.WorkerCount = count;
                for (int i = 0; i < count; i++)
                {
                    var start = job.Size.Value / count * i; var end = i == count - 1 ? job.Size.Value - 1 : job.Size.Value / count * (i + 1) - 1;
                    job.Ranges.Add(new() { Start = start, End = end, Next = start }); job.Pointers[i] = start;
                }
            }
        }
        using var file = File.OpenHandle(job.PartialPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, FileOptions.Asynchronous);
        lock (_gate)
        {
            _activeFile = file; _lastCheckpoint = Environment.TickCount64;
            if (!job.Ranged || job.Downloaded == 0) RandomAccess.SetLength(file, 0);
            if (job.Size is long size) RandomAccess.SetLength(file, size);
            Save();
        }
        try
        {
            if (job.Ranged)
            {
                using var failFast = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                async Task Run(int worker) { try { await RangedWorker(client, file, job, worker, failFast.Token); } catch { failFast.Cancel(); throw; } }
                var tasks = Enumerable.Range(0, job.WorkerCount).Select(Run).ToList();
                if (job.WorkerCount == 1)
                {
                    var previous = job.Downloaded; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!tasks[0].IsCompleted)
                    {
                        if (await Task.WhenAny(tasks[0], Task.Delay(3000, failFast.Token)) == tasks[0]) break;
                        lock (_gate)
                        {
                            var downloaded = job.Downloaded; var speed = (downloaded - previous) / Math.Max(.1, clock.Elapsed.TotalSeconds); previous = downloaded; clock.Restart();
                            if (speed >= 256 * 1024 || job.Size - downloaded < 1024 * 1024) continue;
                            job.WorkerCount = Workers; job.Phase = "单路低于 256 KiB/s，已扩为 8 worker";
                        }
                        tasks.AddRange(Enumerable.Range(1, Workers - 1).Select(Run)); break;
                    }
                }
                try { await Task.WhenAll(tasks); }
                catch { var failed = tasks.FirstOrDefault(t => t.Exception is not null); if (failed?.Exception?.InnerException is Exception ex) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw(); throw; }
            }
            else await Single(client, file, job, cancellation);
            cancellation.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_gate) { RandomAccess.FlushToDisk(file); _activeFile = null; Save(); }
        }
    }
    private DownloadRange? Claim(DownloadJob job, int worker)
    {
        var range = job.Ranges.FirstOrDefault(r => r.Worker is null && r.Next <= r.End);
        if (range is null && job.Ranges.Count < 2048)
        {
            var donor = job.Ranges.Where(r => r.Worker is not null && r.End - r.Next >= 256 * 1024).OrderByDescending(r => r.End - r.Next).FirstOrDefault();
            if (donor is not null)
            {
                var middle = donor.Next + (donor.End - donor.Next) / 2;
                range = new() { Start = middle + 1, Next = middle + 1, End = donor.End }; donor.End = middle; job.Ranges.Add(range);
            }
        }
        if (range is not null) { range.Worker = worker; job.Pointers[worker] = range.Next; }
        return range;
    }
    private async Task RangedWorker(HttpClient client, SafeFileHandle file, DownloadJob job, int worker, CancellationToken cancellation)
    {
        var buffer = new byte[BufferSize];
        while (true)
        {
            DownloadRange? range; lock (_gate) range = Claim(job, worker);
            if (range is null) return;
            var failures = 0;
            while (true)
            {
                long start, end; lock (_gate) { start = range.Next; end = range.End; }
                if (start > end) break;
                try
                {
                    using var request = Request(new Uri(job.Url), job.Referer); request.Headers.Range = new(start, end);
                    if (job.ETag is not null) request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(job.ETag));
                    else if (job.Modified is DateTimeOffset modified) request.Headers.IfRange = new RangeConditionHeaderValue(modified);
                    using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); headersTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
                    response.EnsureSuccessStatusCode();
                    var contentRange = response.Content.Headers.ContentRange;
                    if (response.StatusCode != HttpStatusCode.PartialContent || contentRange?.From != start || contentRange.To != end || contentRange.Length != job.Size || response.Content.Headers.ContentEncoding.Count > 0 || job.ETag is not null && response.Headers.ETag is not null && response.Headers.ETag.ToString() != job.ETag)
                        throw new InvalidDataException("服务器更改了文件或未正确返回分段；已停止以保护文件完整性。");
                    using var stream = await response.Content.ReadAsStreamAsync(cancellation);
                    while (true)
                    {
                        lock (_gate) if (range.Next > range.End) break;
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        var read = await stream.ReadAsync(buffer, timeout.Token); if (read == 0) throw new IOException("分段连接提前结束。");
                        lock (_gate)
                        {
                            var count = (int)Math.Min(read, range.End - range.Next + 1);
                            if (count > 0) { RandomAccess.Write(file, buffer.AsSpan(0, count), range.Next); range.Next += count; job.Pointers[worker] = range.Next; Checkpoint(); }
                        }
                        failures = 0;
                    }
                }
                catch (Exception ex) when ((ex is HttpRequestException or IOException or OperationCanceledException) && ex is not InvalidDataException && !cancellation.IsCancellationRequested && ++failures <= 3)
                { await Task.Delay(TimeSpan.FromSeconds(failures * 2), cancellation); }
            }
            lock (_gate) range.Worker = null;
        }
    }
    private async Task Single(HttpClient client, SafeFileHandle file, DownloadJob job, CancellationToken cancellation)
    {
        using var request = Request(new Uri(job.Url), job.Referer);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count > 0) throw new IOException("服务器返回了不支持的响应。");
        using var stream = await response.Content.ReadAsStreamAsync(cancellation); var buffer = new byte[BufferSize];
        while (true)
        {
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var read = await stream.ReadAsync(buffer, readTimeout.Token); if (read == 0) break;
            lock (_gate) { RandomAccess.Write(file, buffer.AsSpan(0, read), job.SingleBytes); job.SingleBytes += read; job.Pointers[0] = job.SingleBytes; Checkpoint(); }
        }
        if (job.Size is long size && job.SingleBytes != size) throw new IOException("文件长度与服务器声明不一致。");
    }
    private void Checkpoint()
    {
        if (Environment.TickCount64 - _lastCheckpoint < 5000) return;
        if (_activeFile is not null) RandomAccess.FlushToDisk(_activeFile);
        Save(); _lastCheckpoint = Environment.TickCount64;
    }
    private static string UniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        for (int i = 1; File.Exists(path); i++) path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + " (" + i + ")" + Path.GetExtension(name));
        return path;
    }
    private static void DeletePartial(DownloadJob job) { try { File.Delete(job.PartialPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } DeleteParts(job); }
    public void Dispose()
    {
        _stop.Cancel(); lock (_gate) _activeCancellation?.Cancel(); Wake();
        try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
    }
}
