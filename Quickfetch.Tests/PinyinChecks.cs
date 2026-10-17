using Quickfetch.Core;

internal static class PinyinChecks
{
    internal static void Run(Action<bool, string> check)
    {
        ShortcutEntry Entry(string name) => new(name, "C:\\Missing\\" + name + ".lnk");
        SearchResult Match(string name, string query) => SearchMatcher.Search([Entry(name)], query).Single();
        check(Match("微信", "weixin") is { Tier: 0, ExactName: true, IsPinyin: true }, "Pinyin full names must match Chinese-only shortcuts exactly.");
        check(Match("微信", "wx") is { Tier: 2, MatchLabel: "拼音首字母" }, "Chinese pinyin initials must use the initials tier.");
        check(Match("微信", "WEIXIN").ExactName, "Pinyin matching must ignore case.");
        check(Match("微信", "wei").Tier == 1, "A partial pinyin prefix must use the prefix tier.");
        check(Match("微信", "xin").Tier == 3, "An internal pinyin substring must remain below initials.");
        check(Match("网易云音乐", "wangyiyunyinyue").ExactName && Match("网易云音乐", "wyyyy").Tier == 2, "Music must use yue in full spelling and initials.");
        check(Match("QQ音乐", "qqyy").Tier == 2 && Match("QQ音乐", "qyy").Tier == 2, "Mixed initials must support both preserved Latin runs and word initials.");
        check(Match("腾讯Meeting", "txm").Tier == 2 && Match("腾讯Meeting", "txmeeting").Tier == 2, "Mixed Chinese/English names must support both abbreviation styles.");
        check(Match("计算器2", "jsq2").Tier == 2 && Match("计算器2", "jisuanqi2").ExactName, "Digits must survive pinyin conversion and abbreviation.");
        check(Match("重庆银行", "chongqingyinhang").ExactName && Match("重庆银行", "cqyh").Tier == 2, "Common polyphone phrases must use their natural readings.");
        check(Match("中文Code", "code") is { Tier: 0, IsPinyin: false }, "Literal English words inside a mixed name must retain whole-word priority and the literal match label.");
        var order = SearchMatcher.Search([Entry("AwxTool"), Entry("微信"), Entry("wxLauncher"), Entry("WX")], "wx");
        check(order.Select(x => x.Name).SequenceEqual(new[] { "WX", "wxLauncher", "微信", "AwxTool" }), "Pinyin initials must follow full-name/prefix matches and precede internal substrings.");
        check(SearchMatcher.Search([Entry("微信"), Entry("计算器"), Entry("😀")], "").Count == 2, "Chinese-only shortcuts must be listed; symbol-only names must remain excluded.");
        var cached = Entry("微信"); var hash = cached.GetHashCode();
        var key = cached.SearchName;
        check(ReferenceEquals(key, cached.SearchName), "Repeated searches must reuse the cached pinyin name.");
        check(hash == cached.GetHashCode() && cached == Entry("微信"), "Populating a search cache must preserve shortcut record identity and hashing.");
        var renamed = cached with { Name = "计算器" };
        check(renamed.SearchName == "jisuanqi", "Renamed shortcut records must not retain the old pinyin cache.");
        check(cached.Initial == "W", "A Chinese shortcut without an icon must show its pinyin initial.");
        var ranked = UsageRanking.Rank(order, new Dictionary<string, AppUsage> { [order.Single(x => x.Name == "微信").Path] = new(500, DateTimeOffset.UtcNow, 500) }, DateTimeOffset.UtcNow);
        check(ranked[0].Name == "WX" && ranked[1].Name == "wxLauncher", "High pinyin usage must not cross higher-priority matching tiers.");
        check(SearchMatcher.HasSearchableName("微信") && !SearchMatcher.HasSearchableName("😀"), "Catalog eligibility must accept Chinese names without resolving shortcut targets.");
        check(SearchMatcher.Normalize("绿 色") == "lvse", "The standard ASCII v spelling must be usable for ü syllables.");
        check(FileSearchQuery.SupportsPinyin(1, 5, 0, 1387) && !FileSearchQuery.SupportsPinyin(1, 5, 0, 1386) && !FileSearchQuery.SupportsPinyin(1, 4, 1, 1026), "Everything capabilities must respect the native pinyin version boundary.");
        check(FileSearchQuery.WithPinyin("weixin", true) == "<weixin|pinyin-quan:weixin|pinyin-jian:weixin>", "Plain file queries must combine literal, full pinyin and initials server-side.");
        check(FileSearchQuery.WithPinyin("wx yy", true).Contains("> <"), "Space-delimited file terms must keep AND semantics.");
        foreach (var literal in new[] { "ext:txt wx", "C:\\Files\\wx", "*.txt", "\"wx\"", "regex:wx", "微信", "123" })
            check(FileSearchQuery.WithPinyin(literal, true) == literal, "Advanced and non-Latin queries must remain unchanged: " + literal);
        check(FileSearchQuery.WithPinyin("weixin", false) == "weixin", "Older Everything versions must keep literal searching.");
    }
}
