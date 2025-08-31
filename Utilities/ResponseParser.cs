using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public static class ResponseParser
{
    public static List<string> ParseStringArray(string json, bool fallbackByLines)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
        }
        catch { /* fallback */ }

        return fallbackByLines ? json.Replace("\r", "").Split('\n').Select(l => l.TrimStart('-', ' ', '\t')).Where(l => !string.IsNullOrWhiteSpace(l)).ToList() : new List<string>();
    }

    public static Dictionary<int, string> ParseNumberedObject(string json, int expected)
    {
        var dict = new Dictionary<int, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out var k))
                    {
                        var v = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(v)) dict[k] = v.Trim();
                    }
                }
            }
        }
        catch { /* fallback to ensure keys exist */ }

        for (int k = 1; k <= expected; k++)
        {
            if (!dict.ContainsKey(k)) dict[k] = "(pendiente)";
        }
        return dict;
    }

    public static (string? msg, string? refusal, string? finishReason) ParseChoice(JsonElement root)
    {
        try
        {
            var choice = root.GetProperty("choices")[0];
            var message = choice.GetProperty("message");
            var msg = message.GetProperty("content").GetString();
            string? refusal = null;
            if (message.TryGetProperty("refusal", out var r) && r.ValueKind != JsonValueKind.Null) refusal = r.GetString();
            string? finishReason = null;
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String) finishReason = fr.GetString();
            return (msg, refusal, finishReason);
        }
        catch { return (string.Empty, null, null); }
    }

    public static (long prompt, long completion) ParseUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var usage))
        {
            usage.TryGetProperty("prompt_tokens", out var pt);
            usage.TryGetProperty("completion_tokens", out var ct);
            return (pt.TryGetInt64(out var p) ? p : 0, ct.TryGetInt64(out var c) ? c : 0);
        }
        return (0, 0);
    }
    
    public static string? TryExtractFirstJsonObject(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        int depth = 0; int start = -1;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '{') { if (depth == 0) start = i; depth++; }
            else if (s[i] == '}') { depth--; if (depth == 0 && start >= 0) return s.Substring(start, i - start + 1); }
        }
        return null;
    }
}
