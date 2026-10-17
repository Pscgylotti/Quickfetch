namespace Quickfetch.Core;

public static class QueryEditor
{
    public static (string Text, int Caret) Apply(string text, int start, int length, string key)
    {
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        if (key == "clear") return ("", 0);
        if (key == "backspace")
        {
            if (length > 0) return (text.Remove(start, length), start);
            return start > 0 ? (text.Remove(start - 1, 1), start - 1) : (text, start);
        }
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return (text.Remove(start, length).Insert(start, key), start + 1);
        return (text, start);
    }
}
