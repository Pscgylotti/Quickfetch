using System.Text.Json;

namespace Quickfetch.Core;

public sealed record UrlVisit(string Url, bool IsFile, string Name, AppUsage Usage);
public sealed class UrlHistory
{
    private readonly string _path;
    private readonly Dictionary<string, UrlVisit> _visits = new(StringComparer.Ordinal);
    public UrlHistory(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length < 1024 * 1024)
                foreach (var visit in JsonSerializer.Deserialize<UrlVisit[]>(File.ReadAllText(path)) ?? [])
                    if (_visits.Count < 256 && visit is not null && visit.Name is not null && TryAddress(visit.Url, out var uri) && visit.Usage is not null && double.IsFinite(visit.Usage.Weight) && visit.Usage.Count >= 0 && visit.Url.Length <= 8192) _visits[uri.AbsoluteUri] = visit;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
    }
    public static bool TryAddress(string input, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();
        if (!input.Contains("://", StringComparison.Ordinal) && !input.Any(char.IsWhiteSpace) && input.Contains('.') && !input.Contains('\\')) input = "https://" + input;
        return DownloadManager.TryUrl(input, out uri) && !string.IsNullOrEmpty(uri.Host);
    }
    public async Task<DownloadProbe> ResolveAsync(string input, Func<string, Task<DownloadProbe>> probe)
    {
        if (!TryAddress(input, out var uri)) throw new IOException(TextLocalization.T("请输入 HTTP 或 HTTPS 网址。"));
        if (_visits.TryGetValue(uri.AbsoluteUri, out var visit) && !visit.IsFile) return new(uri, visit.Name, null, false, null, null, false);
        return await probe(uri.AbsoluteUri);
    }
    public IReadOnlyList<UrlVisit> Search(string query, DateTimeOffset now, int limit = 80)
    {
        var words = query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return _visits.Values.Where(v => words.All(word => v.Url.Contains(word, StringComparison.OrdinalIgnoreCase) || v.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => UsageRanking.Score(v.Usage, now)).ThenByDescending(v => v.Usage.LastUsed).ThenBy(v => v.Url, StringComparer.Ordinal).Take(Math.Clamp(limit, 1, 256)).ToArray();
    }
    public void Record(string input, bool isFile, string name, DateTimeOffset now)
    {
        if (!TryAddress(input, out var uri) || uri.AbsoluteUri.Length > 8192) return;
        var key = uri.AbsoluteUri;
        _visits[key] = new(key, isFile, string.IsNullOrWhiteSpace(name) ? uri.Host : name[..Math.Min(name.Length, 256)], UsageRanking.Record(_visits.GetValueOrDefault(key)?.Usage, now));
        foreach (var old in _visits.Values.OrderByDescending(v => UsageRanking.Score(v.Usage, now)).ThenByDescending(v => v.Usage.LastUsed).Skip(256).ToArray()) _visits.Remove(old.Url);
        Save();
    }
    public void Clear() { _visits.Clear(); Save(); }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_visits.Values);
        while (bytes.Length >= 1024 * 1024 && _visits.Count > 1)
        {
            var oldest = _visits.Values.MinBy(v => UsageRanking.Score(v.Usage, DateTimeOffset.UtcNow))!;
            _visits.Remove(oldest.Url); bytes = JsonSerializer.SerializeToUtf8Bytes(_visits.Values);
        }
        var temporary = _path + ".tmp"; File.WriteAllBytes(temporary, bytes); File.Move(temporary, _path, true);
    }
}
