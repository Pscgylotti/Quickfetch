using System.Globalization;
using System.Text.Json;

namespace Quickfetch.Core;

public static class TextLocalization
{
    public static string Language { get; set; } = "zh-CN";
    public static bool IsEnglish => Language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
    private static readonly Lazy<Dictionary<string, string>> Translations = new(() =>
    {
        using var stream = typeof(TextLocalization).Assembly.GetManifestResourceStream("Quickfetch.Core.Data.translations.json") ?? throw new InvalidDataException("Missing translations.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    });
    public static IReadOnlyDictionary<string, string> All => Translations.Value;
    public static string T(string source) => IsEnglish ? Translations.Value.GetValueOrDefault(source, source) : source;
    public static string F(FormattableString value) => string.Format(IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("zh-CN"), T(value.Format), value.GetArguments());
}
