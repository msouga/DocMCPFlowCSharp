public static class Utils
{
    public static string Tail(string text, int tailChars) =>
        string.IsNullOrEmpty(text) || text.Length <= tailChars ? text : text[^tailChars..];

    public static string Escape(string s) => s.Replace("\"", "\\\"");
}

