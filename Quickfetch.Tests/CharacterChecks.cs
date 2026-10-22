using System.Text;
using Quickfetch.Core;

internal static class CharacterChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var catalog = CharacterCatalog.Instance;
        check(catalog.Entries.Count > 40000 && catalog.Styles.Count >= 23, "The bundled offline catalogue must include Unicode names, emoji sequences and conversion maps.");
        check(catalog.Search("积分").Any(c => c.Text == "∫") && catalog.Search("integral").Any(c => c.Text == "∫"), "Scientific symbols must support Chinese and English name search.");
        check(catalog.Search("阿尔法").Any(c => c.Text == "α") && catalog.Search("lambda").Any(c => c.Text == "λ"), "Greek names must include Chinese aliases and common lambda spelling.");
        check(catalog.Search("a", "希腊字母").Any(c => c.Text == "α") && catalog.Search("a").Any(c => c.Text == "α"), "A Latin letter search must also expose corresponding Greek romanization variants.");
        check(catalog.Search("hiragana ka").Any(c => c.Text == "か") && catalog.Search("shi", "片假名").Any(c => c.Text == "シ"), "Kana must be searchable by Unicode names and common romanization.");
        check(catalog.Search("平假名").All(c => c.Group == "平假名") && catalog.Search("a", "拉丁与音标").Any(c => c.Text == "á"), "Group names and accented Latin base letters must be searchable.");
        check(catalog.Search("笑脸", "Emoji").Count > 0 && catalog.Search("family", "Emoji").Any(c => c.Text.Contains('\u200D')), "Emoji names must include Chinese annotations and complete joined sequences.");
        check(catalog.Search("U+03C0")[0].Text == "π" && catalog.Search("😀")[0].Text == "😀", "Code-point and direct-glyph lookup must rank exact matches first.");
        check(catalog.Search("", "Emoji").Count == 240 && catalog.Search("no-such-character-quickfetch").Count == 0, "Search results must be bounded and unrelated searches must stay empty.");
        CharacterStyle Style(string id) => catalog.Styles.Single(s => s.Id == id);
        check(Style("bold").Convert("Hello 123") == "𝐇𝐞𝐥𝐥𝐨 𝟏𝟐𝟑", "Bold conversion must preserve spaces and convert every ASCII letter and digit.");
        check(Style("italic").Convert("h") == "ℎ" && Style("script").Convert("BEHego") == "ℬℰℋℯℊℴ" && Style("double").Convert("CHNPQRZ") == "ℂℍℕℙℚℝℤ", "Unassigned mathematical-alphabet gaps must map to their legacy letterlike symbols.");
        check(Style("super").Convert("0123456789") == "⁰¹²³⁴⁵⁶⁷⁸⁹" && Style("sub").Convert("0123456789") == "₀₁₂₃₄₅₆₇₈₉", "All superscript and subscript digits must convert accurately.");
        check(Style("sub").Convert("xq中文😀") == "ₓq中文😀" && Style("sub").Unsupported("xq") == 1, "Unavailable styles must preserve original characters rather than invent substitutes.");
        check(Style("acute").Convert("a e") == "á é", "Latin diacritics must produce canonical composed text where available.");
        var multi = catalog.Convert("Hello  world\n123").Single(s => s.Name == "数学粗体");
        check(multi.Text == "𝐇𝐞𝐥𝐥𝐨  𝐰𝐨𝐫𝐥𝐝\n𝟏𝟐𝟑" && multi.Words.Select(w => w.Text).SequenceEqual(new[] { "𝐇𝐞𝐥𝐥𝐨", "𝐰𝐨𝐫𝐥𝐝", "𝟏𝟐𝟑" }), "Whole-text and per-word previews must share the same conversion while retaining original whitespace.");
        check(catalog.Convert("中文😀").Count == 0 && catalog.Entries.Select(e => e.Text).Distinct().Count() == catalog.Entries.Count, "Non-Latin input must not create meaningless style previews, and catalogue entries must be unique.");
        check(catalog.Styles.All(s => s.Map.Values.All(value => value.EnumerateRunes().All(r => Rune.IsValid(r.Value)))), "Conversion maps must contain valid Unicode scalar values.");
        check(CharacterTextEditor.Apply("A👨‍👩‍👧‍👦B", 12, 0, "backspace") == ("AB", 1), "The floating keyboard must delete a joined emoji as one text element.");
        check(CharacterTextEditor.Apply("a\u0301b", 2, 0, "backspace") == ("b", 0), "Backspace must not leave an orphaned combining accent.");
        check(CharacterTextEditor.Apply("A😀B", 3, 0, "left").Caret == 1 && CharacterTextEditor.Apply("A😀B", 1, 0, "right").Caret == 3, "Caret arrows must move across whole Unicode text elements.");
        check(CharacterTextEditor.Apply("AB", 1, 1, "x") == ("Ax", 2) && CharacterTextEditor.Apply("AB", 1, 0, "space") == ("A B", 2), "Virtual typing must replace the current selection or insert at its caret.");
    }
}
