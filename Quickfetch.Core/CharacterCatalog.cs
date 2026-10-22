using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Quickfetch.Core;

public sealed class CharacterEntry
{
    public string Text { get; }
    public string Name { get; }
    public string EnglishName { get; }
    public string Group { get; }
    internal string SearchText { get; }
    public CharacterEntry(string text, string name, string englishName, string group, string keywords)
    {
        Text = text; EnglishName = englishName; Name = name == englishName ? englishName : name; Group = group;
        SearchText = " " + Normalize((Name == EnglishName ? "" : Name + " ") + EnglishName + " " + keywords) + " ";
    }
    public string CodePoints => string.Join(" ", Text.EnumerateRunes().Select(r => "U+" + r.Value.ToString("X4")));
    public string Hint => TextLocalization.IsEnglish ? EnglishName + "\n" + CodePoints : Name + "\n" + EnglishName + "\n" + CodePoints;
    // Combining marks get a dotted-circle preview; insertion keeps the actual mark.
    public string Preview => Rune.GetUnicodeCategory(Text.EnumerateRunes().First()) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark ? "◌" + Text : Text;
    internal static string Normalize(string value) => value.ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
}

public sealed class CharacterStyle
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Note { get; init; } = "";
    public Dictionary<string, string> Map { get; init; } = [];
    public string Convert(string source)
    {
        var text = new StringBuilder();
        foreach (var rune in source.EnumerateRunes()) text.Append(Map.TryGetValue(rune.ToString(), out var replacement) ? replacement : rune.ToString());
        return text.ToString().Normalize(NormalizationForm.FormC);
    }
    public int Unsupported(string source) => source.Count(c => char.IsAsciiLetterOrDigit(c) && !Map.ContainsKey(c.ToString()));
}

public sealed record CharacterWord(string Original, string Text);
public sealed record CharacterConversion(string Name, string Text, string Note, IReadOnlyList<CharacterWord> Words);

public sealed class CharacterCatalog
{
    private static readonly Lazy<CharacterCatalog> Shared = new(() => new CharacterCatalog());
    public static CharacterCatalog Instance => Shared.Value;
    public static IReadOnlyList<string> Groups { get; } = ["全部", "科学符号", "拉丁与音标", "希腊字母", "平假名", "片假名", "Emoji", "数字", "标点与箭头", "其他"];
    public IReadOnlyList<CharacterEntry> Entries { get; }
    public IReadOnlyList<CharacterStyle> Styles { get; }
    private readonly Dictionary<string, CharacterEntry> _byText;
    private readonly LinkedList<(string Query, string Group, int Limit, IReadOnlyList<CharacterEntry> Results)> _cache = new();
    private static readonly string[] Favorites = ["∞", "∫", "∑", "√", "π", "α", "β", "θ", "Ω", "∂", "∇", "≈", "≠", "≤", "≥", "±", "×", "÷", "°", "℃", "→", "⇒", "ℝ", "ℂ", "∈", "∀", "∃", "😀", "😂", "🥰", "👍", "🎉", "❤️", "🔥", "✅", "🚀"];
    private static readonly IReadOnlyDictionary<string, int> FavoriteOrder = Favorites.Select((text, index) => (text, index)).ToDictionary(x => x.text, x => x.index);
    private CharacterCatalog()
    {
        using (var reader = new StreamReader(Resource("characters.tsv.gz"), Encoding.UTF8))
        {
            var entries = new List<CharacterEntry>();
            while (reader.ReadLine() is { } line)
            {
                var fields = line.Split('\t');
                if (fields.Length == 5 && fields[0].Length > 0) entries.Add(new(fields[0], fields[1], fields[2], Groups.FirstOrDefault(g => g == fields[3]) ?? fields[3], fields[4]));
            }
            Entries = entries.OrderBy(e => FavoriteOrder.GetValueOrDefault(e.Text, 100)).ThenBy(e => Rune.GetRuneAt(e.Text, 0).Value).ToArray();
        }
        _byText = Entries.ToDictionary(e => e.Text);
        using var stream = Resource("character-styles.json.gz");
        Styles = JsonSerializer.Deserialize<CharacterStyle[]>(stream) ?? [];
    }
    private static Stream Resource(string name) => new GZipStream(typeof(CharacterCatalog).Assembly.GetManifestResourceStream("Quickfetch.Core.Data." + name) ?? throw new InvalidDataException("字符资源缺失。"), CompressionMode.Decompress);
    public IReadOnlyList<CharacterEntry> Search(string query, string group = "全部", int limit = 240)
    {
        query = query.Trim();
        limit = Math.Clamp(limit, 1, 1000);
        lock (_cache)
        {
            for (var node = _cache.First; node is not null; node = node.Next)
                if (node.Value.Query == query && node.Value.Group == group && node.Value.Limit == limit) { var value = node.Value; _cache.Remove(node); _cache.AddFirst(value); return value.Results; }
        }
        var normalized = CharacterEntry.Normalize(query);
        var tokens = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var code = query.StartsWith("U+", StringComparison.OrdinalIgnoreCase) && int.TryParse(query.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) && Rune.IsValid(cp) ? char.ConvertFromUtf32(cp) : null;
        var singleLetter = query.Length == 1 && char.IsAsciiLetter(query[0]);
        var results = new List<CharacterEntry>(Math.Min(limit, 240));
        var exact = _byText.GetValueOrDefault(code ?? query);
        if (exact is not null && (group == "全部" || exact.Group == group)) results.Add(exact);
        var terms = singleLetter ? new[] { " " + normalized + " " } : tokens;
        foreach (var entry in Entries)
        {
            if (results.Count >= limit) break;
            if (entry == exact || group != "全部" && entry.Group != group) continue;
            var matches = true;
            foreach (var term in terms) if (!entry.SearchText.Contains(term, StringComparison.Ordinal) && (term[0] < 128 || !entry.Group.Contains(term, StringComparison.Ordinal))) { matches = false; break; }
            if (matches) results.Add(entry);
        }
        var array = results.ToArray();
        if (query.Length <= 128) lock (_cache) { _cache.AddFirst((query, group, limit, array)); if (_cache.Count > 8) _cache.RemoveLast(); }
        return array;
    }
    public IReadOnlyList<CharacterConversion> Convert(string query)
    {
        if (!query.Any(char.IsAsciiLetterOrDigit)) return [];
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Styles.Select(style => new CharacterConversion(style.Name, style.Convert(query), TextLocalization.T(style.Note) + (style.Unsupported(query) is > 0 and var missing ? TextLocalization.F($" · {missing} 个字符保留原文") : "") + (words.Length > 24 ? TextLocalization.T(" · 下方预览前 24 词；整段结果完整") : ""), words.Length > 1 ? words.Take(24).Select(word => new CharacterWord(word, style.Convert(word))).ToArray() : [])).ToArray();
    }
}
