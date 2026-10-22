using Quickfetch.Core;

internal static class UrlChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "Quickfetch-urls-" + Guid.NewGuid().ToString("N"), "history.json");
        var now = DateTimeOffset.UtcNow;
        try
        {
            var history = new UrlHistory(path);
            check(UrlHistory.TryAddress("github.com/Pscgylotti", out var address) && address.AbsoluteUri == "https://github.com/Pscgylotti" && !UrlHistory.TryAddress("file:///C:/a", out _) && !UrlHistory.TryAddress("https://user:pass@example.com", out _), "URL mode must accept bare domains but reject local schemes and embedded credentials.");
            for (int i = 0; i < 3; i++) history.Record("https://github.com/Pscgylotti/Quickfetch", false, "GitHub", now.AddHours(-1));
            history.Record("https://example.com/other", false, "Other", now);
            history.Record("https://example.com/old", false, "Old", now.AddDays(-90));
            check(history.Search("", now)[0].Name == "GitHub" && history.Search("", now)[^1].Name == "Old", "URL history must combine usage frequency with decaying recency.");
            check(history.Search("github quickfetch", now).Single().Usage.Count == 3, "URL history must filter multiple text terms and preserve use counts.");
            var restored = new UrlHistory(path); var probes = 0;
            Task<DownloadProbe> Probe(string url) { probes++; return Task.FromResult(new DownloadProbe(new Uri(url), "file.zip", 100, false, null, null, true)); }
            var web = await restored.ResolveAsync("https://github.com/Pscgylotti/Quickfetch", Probe);
            check(!web.IsFile && probes == 0, "Persisted webpage classifications must open directly without network probing.");
            restored.Record("https://example.com/file.zip", true, "file.zip", now);
            check((await restored.ResolveAsync("https://example.com/file.zip", Probe)).IsFile && probes == 1, "Cached files must be checked again before a new transfer.");
            await restored.ResolveAsync("https://example.com/new", Probe);
            check(probes == 2 && restored.Search("/new", now).Count == 0, "Checking alone must not create history before navigation or enqueue succeeds.");
            restored.Record("https://example.com/Case", false, "Upper", now); restored.Record("https://example.com/case", false, "Lower", now);
            check(restored.Search("/case", now).Count == 2, "URL paths must remain case-sensitive identities.");
            restored.Clear();
            check(new UrlHistory(path).Search("", now).Count == 0, "Clearing URL history must also clear persistent webpage classification results.");
            File.WriteAllText(path, "[{\"Url\":null,\"Name\":null,\"Usage\":null},null]");
            check(new UrlHistory(path).Search("", now).Count == 0, "Invalid local history entries must not crash startup.");
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) { foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!)) File.Delete(file); Directory.Delete(Path.GetDirectoryName(path)!); } }
    }
}
