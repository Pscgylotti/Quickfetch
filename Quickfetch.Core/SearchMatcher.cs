using System.Text.RegularExpressions;

namespace Quickfetch.Core;

public sealed record ShortcutEntry(string Name, string Path)
{
    public string Initial => Name.FirstOrDefault(char.IsAsciiLetterOrDigit).ToString().ToUpperInvariant();
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
}

public sealed record SearchResult(ShortcutEntry Entry, int Tier, int Position, int SubstringLength,
    double SubstringRatio, int CharacterOverlap, double CharacterRatio, bool ExactName)
{
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public string Initial => Entry.Initial;
    public string Location => Entry.Directory;
    public string MatchLabel => Tier switch
    {
        0 => ExactName ? "完整名称" : "完整单词",
        1 => "开头匹配",
        2 => "首字母匹配",
        3 => "中间匹配",
        4 => "连续字串",
        5 => "字母重叠",
        _ => "快捷方式"
    };
}

public static partial class SearchMatcher
{
    [GeneratedRegex("[A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();

    public static string Normalize(string text) => string.Concat(text.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();

    public static IReadOnlyList<SearchResult> Search(IEnumerable<ShortcutEntry> entries, string query)
    {
        var needle = Normalize(query);
        if (needle.Length == 0)
            return entries.Where(e => Normalize(e.Name).Length > 0)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .Select(e => new SearchResult(e, 6, 0, 0, 0, 0, 0, false)).ToArray();

        var matches = new List<SearchResult>();
        foreach (var entry in entries)
        {
            var name = Normalize(entry.Name);
            if (name.Length == 0) continue;
            var exactName = name == needle;
            var fullWord = Words().Matches(entry.Name).Any(m => m.Value.Equals(needle, StringComparison.OrdinalIgnoreCase));
            var position = name.IndexOf(needle, StringComparison.Ordinal);
            var common = LongestCommonSubstring(name, needle);
            var overlap = CharacterOverlap(name, needle);
            if (overlap == 0) continue;
            var initials = string.Concat(entry.Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize).Where(word => word.Length > 0).Select(word => word[0]));
            var initialsMatch = initials.Length >= 2 && needle.Length >= 2 && initials.StartsWith(needle, StringComparison.Ordinal);
            var tier = exactName || fullWord ? 0 : position == 0 ? 1 : initialsMatch ? 2 : position > 0 ? 3 : common >= 2 ? 4 : 5;
            matches.Add(new(entry, tier, position < 0 ? int.MaxValue : position, common,
                (double)common / needle.Length, overlap, (double)overlap / needle.Length, exactName));
        }
        return matches.OrderBy(m => m.Tier)
            .ThenByDescending(m => m.ExactName)
            .ThenBy(m => m.Tier is 0 or 1 or 3 ? m.Position : 0)
            .ThenByDescending(m => m.Tier == 4 ? m.SubstringRatio : 0)
            .ThenByDescending(m => m.Tier >= 4 ? m.CharacterOverlap : 0)
            .ThenByDescending(m => m.Tier >= 4 ? m.CharacterRatio : 0)
            .ThenBy(m => Normalize(m.Name).Length)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Continuous overlap, not a subsequence: a gap breaks the matching run.
    public static int LongestCommonSubstring(string a, string b)
    {
        var row = new int[b.Length + 1];
        var longest = 0;
        foreach (var ch in a)
            for (var j = b.Length; j > 0; j--)
            {
                row[j] = ch == b[j - 1] ? row[j - 1] + 1 : 0;
                longest = Math.Max(longest, row[j]);
            }
        return longest;
    }

    // Multiset intersection: repeated query letters cannot match one name letter twice.
    public static int CharacterOverlap(string a, string b)
    {
        Span<int> counts = stackalloc int[128];
        foreach (var ch in a) if (ch < 128) counts[ch]++;
        var overlap = 0;
        foreach (var ch in b)
            if (ch < 128 && counts[ch] > 0) { counts[ch]--; overlap++; }
        return overlap;
    }
}
