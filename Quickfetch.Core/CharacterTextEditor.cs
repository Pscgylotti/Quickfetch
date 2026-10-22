using System.Globalization;

namespace Quickfetch.Core;

public static class CharacterTextEditor
{
    public static (string Text, int Caret) Apply(string text, int start, int length, string key)
    {
        start = Math.Clamp(start, 0, text.Length); length = Math.Clamp(length, 0, text.Length - start);
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        if (key == "clear") return ("", 0);
        if (key == "left") return (text, length > 0 ? start : boundaries.LastOrDefault(p => p < start));
        if (key == "right") return (text, length > 0 ? start + length : boundaries.FirstOrDefault(p => p > start, text.Length));
        if (key == "backspace")
        {
            if (length == 0 && start > 0) { var previous = boundaries.LastOrDefault(p => p < start); length = start - previous; start = previous; }
            return (text.Remove(start, length), start);
        }
        var insert = key == "space" ? " " : key == "enter" ? "\n" : key;
        return (text.Remove(start, length).Insert(start, insert), start + insert.Length);
    }
}
