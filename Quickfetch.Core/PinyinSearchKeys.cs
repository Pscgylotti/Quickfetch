using System.Text;
using System.Text.RegularExpressions;
using TinyPinyin;

namespace Quickfetch.Core;

internal sealed partial record PinyinSearchKeys(string Original, string Full, string[] Initials, string[] Words, bool HasChinese)
{
    // Small phrase corrections for common application names; other polyphones use the library's default reading.
    private static readonly (string Text, string[] Readings)[] Phrases =
    [
        ("音乐", ["yin", "yue"]), ("乐器", ["yue", "qi"]), ("银行", ["yin", "hang"]),
        ("重庆", ["chong", "qing"]), ("重复", ["chong", "fu"]), ("重启", ["chong", "qi"]),
        ("长安", ["chang", "an"]), ("长沙", ["chang", "sha"]), ("长城", ["chang", "cheng"])
    ];
    [GeneratedRegex("[A-Za-z0-9\\u3007\\u4E00-\\u9FFF]+", RegexOptions.CultureInvariant)]
    private static partial Regex NameWords();
    internal static bool IsSearchable(string name) => name.Any(c => char.IsAsciiLetterOrDigit(c) || PinyinHelper.IsChinese(c));
    private static string[] Readings(string text)
    {
        var result = text.Select(c => PinyinHelper.IsChinese(c) ? PinyinHelper.GetPinyin(c).ToLowerInvariant() : "").ToArray();
        for (var i = 0; i < text.Length; i++)
        foreach (var phrase in Phrases)
            if (text.AsSpan(i).StartsWith(phrase.Text, StringComparison.Ordinal))
                for (var j = 0; j < phrase.Readings.Length; j++) result[i + j] = phrase.Readings[j];
        return result;
    }
    internal static string Normalize(string text)
    {
        if (!text.Any(PinyinHelper.IsChinese)) return string.Concat(text.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();
        var syllables = Readings(text); var full = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
            if (syllables[i].Length > 0) full.Append(syllables[i]);
            else if (char.IsAsciiLetterOrDigit(text[i])) full.Append(char.ToLowerInvariant(text[i]));
        return full.ToString();
    }
    internal static PinyinSearchKeys Create(string name)
    {
        var hasChinese = name.Any(PinyinHelper.IsChinese);
        var initials = new HashSet<string>(StringComparer.Ordinal)
        {
            string.Concat(name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize).Where(word => word.Length > 0).Select(word => word[0]))
        };
        if (hasChinese)
        {
            var syllables = Readings(name);
            var shortName = new StringBuilder(); var mixed = new StringBuilder(); bool firstAscii = true;
            for (var i = 0; i < name.Length; i++)
            {
                if (syllables[i].Length > 0)
                {
                    shortName.Append(syllables[i][0]); mixed.Append(syllables[i][0]); firstAscii = true;
                }
                else if (char.IsAsciiLetterOrDigit(name[i]))
                {
                    var c = char.ToLowerInvariant(name[i]); mixed.Append(c);
                    if (firstAscii || char.IsAsciiDigit(c)) shortName.Append(c);
                    firstAscii = false;
                }
                else firstAscii = true;
            }
            initials.Add(shortName.ToString()); initials.Add(mixed.ToString());
        }
        var words = NameWords().Matches(name).Select(match => Normalize(match.Value))
            .Concat(Regex.Matches(name, "[A-Za-z0-9]+", RegexOptions.CultureInvariant).Select(match => match.Value.ToLowerInvariant()))
            .Where(word => word.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return new(name, Normalize(name), initials.ToArray(), words, hasChinese);
    }
}
