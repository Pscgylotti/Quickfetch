using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Quickfetch.Core;

internal static class DownloadChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        check(DownloadManager.TryUrl("https://example.com/a.zip", out _) && !DownloadManager.TryUrl("file:///C:/a", out _) && !DownloadManager.TryUrl("https://user:pass@example.com", out _), "Downloads accept only HTTP(S) URLs without embedded credentials.");
        check(DownloadManager.SafeName("../../CON.txt") == "_CON.txt" && !DownloadManager.SafeName("a<>:\"|?*.exe").Contains(':'), "Remote filenames must remain safe basenames.");
        var parsed = DownloadManager.ParsePlaylist("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:12\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\"\n#EXTINF:1,\na.ts\n#EXT-X-BYTERANGE:32@64\nb.ts\n#EXT-X-ENDLIST", new Uri("https://example.com/media/list.m3u8"));
        check(parsed.Count == 2 && parsed[0].KeyUrl == "https://example.com/media/key.bin" && parsed[0].IV.EndsWith("000C") && parsed[1].Offset == 64 && parsed[1].Length == 32, "HLS must resolve relative segments, AES sequence IVs and byte ranges.");
        try { DownloadManager.ParsePlaylist("#EXTM3U\na.ts", new Uri("https://example.com/list")); check(false, "Live playlists require explicit recording support."); } catch (IOException) { check(true, "Unbounded live streams must be rejected."); }
        try { DownloadManager.ParsePlaylist("#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"key\"\na.ts\n#EXT-X-ENDLIST", new Uri("https://example.com/list")); check(false, "DRM must not be treated as ordinary AES."); } catch (IOException) { check(true, "Unsupported DRM streams must be rejected."); }
        var folder = Path.Combine(Path.GetTempPath(), "Quickfetch-download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        using var server = new Server();
        var options = new DownloadOptions { Folder = folder, ProxyMode = "none" };
        try
        {
            using (var manager = new DownloadManager(Path.Combine(folder, "queue.json"), () => options))
            {
                var page = await manager.ProbeAsync(server.Url("html")); check(!page.IsFile, "HTML must remain a browser URL.");
                var probe = await manager.ProbeAsync(server.Url("range")); check(probe.Ranged && probe.Size == server.Data.Length && probe.Name == "fixture.bin", "The range probe must verify real 206 metadata.");
                try { manager.Enqueue(probe, new(server.Url("range"), ExpectedSize: 1)); check(false, "Browser length mismatches must not cancel the original download."); } catch (IOException) { check(true, "Mismatched browser downloads are rejected before enqueue."); }
                var completedEvents = 0; manager.Completed += _ => Interlocked.Increment(ref completedEvents);
                var id = manager.Enqueue(probe, new(server.Url("range")));
                var done = await Wait(manager, id, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                check(done.Status == DownloadStatus.Completed && SHA256.HashData(File.ReadAllBytes(done.CompletedPath)).SequenceEqual(SHA256.HashData(server.Data)), "Eight-worker downloads must reproduce every source byte.");
                check(done.WorkerCount == 8 && done.Ranges.Count > 8 && done.Ranges.All(r => r.Next == r.End + 1), "Early workers must split outstanding ranges and finish all coverage.");
                check(completedEvents == 1, "One successful task must emit one completion notification.");
                var fallback = await manager.ProbeAsync(server.Url("single")); check(!fallback.Ranged, "Ignored Range requests must select single-stream fallback.");
                var fallbackId = manager.Enqueue(fallback, new(server.Url("single"), "single.bin"));
                var single = await Wait(manager, fallbackId, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                check(single.Status == DownloadStatus.Completed && new FileInfo(single.CompletedPath).Length == server.Small.Length, "Single-stream fallback must complete without corrupt concatenation.");
                var smallId = manager.Enqueue(await manager.ProbeAsync(server.Url("small")), new(server.Url("small"), "small.bin"));
                var small = await Wait(manager, smallId, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                check(small.Status == DownloadStatus.Completed && small.WorkerCount == 1 && small.Ranges.Count == 1, "Fast files below 10 MiB must remain single-worker.");
                var slowId = manager.Enqueue(await manager.ProbeAsync(server.Url("small-slow")), new(server.Url("small-slow"), "adaptive.bin"));
                await Wait(manager, slowId, j => j.Status == DownloadStatus.Running && j.Downloaded > 0);
                check(manager.Snapshot().Single(j => j.Id == slowId).WorkerCount == 1, "Small files must begin with exactly one worker.");
                var adaptive = await Wait(manager, slowId, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                check(adaptive.Status == DownloadStatus.Completed && adaptive.WorkerCount == 8 && adaptive.Phase.Contains("256"), "Measured slow small-file transfers must expand to eight workers.");
                var a = manager.Enqueue(await manager.ProbeAsync(server.Url("slow")), new(server.Url("slow"), "paused.bin"));
                await Wait(manager, a, j => j.Downloaded > 200000);
                var b = manager.Enqueue(probe, new(server.Url("range"), "b.bin")); manager.Pause(b);
                var c = manager.Enqueue(probe, new(server.Url("range"), "c.bin")); manager.Pause(c);
                manager.Pause(a);
                await Wait(manager, a, j => j.Status == DownloadStatus.Paused);
                await Task.Delay(200);
                var frozen = manager.Snapshot().Single(j => j.Id == a); var bytes = frozen.Downloaded; await Task.Delay(200);
                check(!manager.RemoveCompleted(a) && manager.Snapshot().Any(j => j.Id == a), "Removing history must never remove an unfinished download.");
                check(manager.Snapshot().All(j => j.Status != DownloadStatus.Running) && manager.Snapshot().Single(j => j.Id == a).Downloaded == bytes && frozen.Ranges.Count > 0, "All-paused queues must stop and retain frozen segment visualization.");
                manager.Resume(c); manager.Resume(b);
                check(manager.Snapshot().Single(j => j.Id == c).Order < manager.Snapshot().Single(j => j.Id == b).Order, "Resumed tasks must queue in resume order.");
                await Wait(manager, b, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                manager.Resume(a);
                var resumed = await Wait(manager, a, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                check(resumed.Status == DownloadStatus.Completed && SHA256.HashData(File.ReadAllBytes(resumed.CompletedPath)).SequenceEqual(SHA256.HashData(server.Data)), "Paused segment downloads must resume with full data integrity.");
                var bad = manager.Enqueue(probe with { Url = new Uri(server.Url("bad")) }, new(server.Url("bad"), "bad.bin"));
                var rejected = await Wait(manager, bad, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                check(rejected.Status == DownloadStatus.Failed && rejected.CompletedPath.Length == 0, "Invalid Content-Range responses must fail rather than corrupt files.");
                check(manager.RemoveCompleted(id) && !manager.RemoveCompleted(id) && manager.Snapshot().All(j => j.Id != id) && manager.Snapshot().Any(j => j.Id == fallbackId) && File.Exists(done.CompletedPath), "Removing one completed record must preserve its file and other records.");
                check(!File.ReadAllText(Path.Combine(folder, "queue.json")).Contains(id.ToString()), "Single-record removal must be persisted immediately.");
                manager.Cancel(bad); manager.ClearHistory();
                check(manager.Snapshot().Length == 0 && File.Exists(done.CompletedPath), "Clearing history must preserve downloaded files.");
                var persisted = manager.Enqueue(await manager.ProbeAsync(server.Url("slow")), new(server.Url("slow"), "persist.bin"));
                await Wait(manager, persisted, j => j.Downloaded > 100000); manager.Pause(persisted); await Task.Delay(200);
            }
            using (var restored = new DownloadManager(Path.Combine(folder, "queue.json"), () => options))
            {
                var paused = restored.Snapshot().Single(); check(paused.Status == DownloadStatus.Paused && paused.Downloaded > 0, "Restart must preserve explicitly paused tasks and their saved ranges.");
                restored.Resume(paused.Id); var done = await Wait(restored, paused.Id, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                check(done.Status == DownloadStatus.Completed && SHA256.HashData(File.ReadAllBytes(done.CompletedPath)).SequenceEqual(SHA256.HashData(server.Data)), "Restarted partial downloads must remain correct.");
            }
            using (var proxy = new DownloadManager(Path.Combine(folder, "proxy.json"), () => new DownloadOptions { Folder = folder, ProxyMode = "custom", ProxyAddress = server.Url("") }))
            {
                var probe = await proxy.ProbeAsync("http://quickfetch-proxy-test.invalid/file.bin");
                check(probe.Ranged && probe.Size == server.Data.Length, "Custom proxy settings must route requests through the configured local proxy instead of DNS.");
            }
            foreach (var name in new[] { "hls-fixture", "hls-fixture-encrypted" })
            {
                var fixture = Path.GetFullPath(Path.Combine("artifacts", name, "index.m3u8"));
                if (File.Exists(fixture) && DownloadManager.FindFFmpeg() is not null)
                {
                    server.HlsFolder = Path.GetDirectoryName(fixture)!;
                    using var manager = new DownloadManager(Path.Combine(folder, name + ".json"), () => options);
                    var probe = await manager.ProbeAsync(server.Url("hls/index.m3u8")); check(probe.StreamKind == "hls", "HLS URLs must be identified as media jobs.");
                    var id = manager.Enqueue(probe, new(server.Url("hls/index.m3u8")));
                    var done = await Wait(manager, id, j => j.Status is DownloadStatus.Completed or DownloadStatus.Failed);
                    check(done.Status == DownloadStatus.Completed && done.Parts.All(p => p.Done) && new FileInfo(done.CompletedPath).Length > 0, "HLS fragments must download and merge through FFmpeg: " + done.Error);
                }
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) File.Delete(file);
            foreach (var dir in Directory.GetDirectories(folder).OrderByDescending(p => p.Length)) Directory.Delete(dir);
            Directory.Delete(folder);
        }
    }
    private static async Task<DownloadJob> Wait(DownloadManager manager, Guid id, Func<DownloadJob, bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(35);
        while (DateTime.UtcNow < until) { var job = manager.Snapshot().Single(j => j.Id == id); if (ready(job)) return job; await Task.Delay(30); }
        throw new Exception("Download fixture timed out: " + manager.Snapshot().Single(j => j.Id == id).Error);
    }
    private sealed class Server : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _tasks = [];
        public byte[] Data { get; } = new byte[16 * 1024 * 1024];
        public byte[] Small { get; } = new byte[4 * 1024 * 1024];
        public string? HlsFolder { get; set; }
        public Server()
        {
            new Random(42).NextBytes(Data); Array.Copy(Data, Small, Small.Length); _listener.Start();
            _tasks.Add(Task.Run(async () => { try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _tasks.Add(Respond(client)); } } catch (OperationCanceledException) { } }));
        }
        public string Url(string path) => "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/" + path;
        private async Task Respond(TcpClient client)
        {
            using (client)
            try
            {
                using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var request = await reader.ReadLineAsync(_stop.Token); if (request is null) return;
                var path = request.Split(' ')[1].TrimStart('/'); var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token))) { var colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim(); }
                var bytes = path is "small" or "small-slow" or "single" ? Small : Data; var type = "application/octet-stream";
                if (path == "html") { bytes = Encoding.UTF8.GetBytes("<html>test</html>"); type = "text/html"; }
                if (path.StartsWith("hls/") && HlsFolder is not null) { var name = Path.GetFileName(path); bytes = File.ReadAllBytes(Path.Combine(HlsFolder, name)); type = name.EndsWith("m3u8") ? "application/vnd.apple.mpegurl" : "video/mp2t"; }
                long from = 0, to = bytes.Length - 1; var ranged = headers.TryGetValue("Range", out var range) && path != "single";
                if (ranged) { var bounds = range![6..].Split('-'); from = long.Parse(bounds[0]); to = long.Parse(bounds[1]); }
                var length = to - from + 1;
                var response = "HTTP/1.1 " + (ranged ? "206 Partial Content" : "200 OK") + "\r\nContent-Length: " + length + "\r\nContent-Type: " + type + "\r\nETag: \"fixture-v1\"\r\nConnection: close\r\n";
                if (type == "application/octet-stream") response += "Content-Disposition: attachment; filename=fixture.bin\r\n";
                if (ranged) response += $"Content-Range: bytes {(path == "bad" && to > 0 ? from + 1 : from)}-{to}/{bytes.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response + "\r\n"), _stop.Token);
                var delay = path == "small-slow" ? 120 : path == "slow" || path == "range" && from == 0 && to > 0 ? 12 : 0;
                for (var cursor = from; cursor <= to; cursor += 16384)
                { await stream.WriteAsync(bytes.AsMemory((int)cursor, (int)Math.Min(16384, to - cursor + 1)), _stop.Token); if (delay > 0) await Task.Delay(delay, _stop.Token); }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); try { Task.WaitAll(_tasks.ToArray(), 2000); } catch (AggregateException) { } _stop.Dispose(); }
    }
}
