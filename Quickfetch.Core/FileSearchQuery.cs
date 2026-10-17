namespace Quickfetch.Core;

public static class FileSearchQuery
{
    public static bool SupportsPinyin(int major, int minor, int revision, int build) => major > 1 || major == 1 && (minor > 5 || minor == 5 && (revision > 0 || revision == 0 && build >= 1387));
    public static string WithPinyin(string query, bool supported)
    {
        // Preserve paths, wildcards, quoted literals and Everything operators exactly as entered.
        if (!supported || !query.Any(char.IsAsciiLetter) || query.Any(c => !char.IsAsciiLetterOrDigit(c) && !char.IsWhiteSpace(c))) return query;
        return string.Join(" ", query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => $"<{term}|pinyin-quan:{term}|pinyin-jian:{term}>"));
    }
}
