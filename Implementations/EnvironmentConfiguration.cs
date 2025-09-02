using System;

public class EnvironmentConfiguration : IConfiguration
{
    public bool IsDryRun => (Environment.GetEnvironmentVariable("DRY_RUN") ?? "false").Trim().ToLowerInvariant() == "true";
    public bool ShowUsage => (Environment.GetEnvironmentVariable("SHOW_USAGE") ?? "false").Trim().ToLowerInvariant() == "true";
    public string OpenApiKey => Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    public string Model => Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() ?? "gpt-5-mini";
    public int MaxTokensPerCall => int.TryParse(Environment.GetEnvironmentVariable("OPENAI_MAX_COMPLETION_TOKENS"), out var mt) && mt > 0 ? mt : 4096;
    public int NodeDetailWords
        => int.TryParse(Environment.GetEnvironmentVariable("NODE_DETAIL_WORDS"), out var dw)
            ? dw // 0 o negativos = ilimitado
            : (DemoMode ? 900 : 0);
    public int NodeSummaryWords
        => int.TryParse(Environment.GetEnvironmentVariable("NODE_SUMMARY_WORDS"), out var sw)
            ? (sw > 0 ? sw : 180)
            : 180;
    public int PrevChapterTailChars => 4000;
    public TimeSpan HttpTimeout => int.TryParse(Environment.GetEnvironmentVariable("OPENAI_HTTP_TIMEOUT_SECONDS"), out var ts) && ts > 0 ? TimeSpan.FromSeconds(ts) : TimeSpan.FromMinutes(5);
    public bool TreatRefusalAsError => (Environment.GetEnvironmentVariable("TREAT_REFUSAL_AS_ERROR") ?? "true").Trim().ToLowerInvariant() == "true";
    public bool DemoMode => (Environment.GetEnvironmentVariable("DEMO_MODE") ?? "true").Trim().ToLowerInvariant() == "true";
    public bool DebugLogging => (Environment.GetEnvironmentVariable("DEBUG") ?? "true").Trim().ToLowerInvariant() == "true";
    public bool UseResponsesApi => (Environment.GetEnvironmentVariable("USE_RESPONSES_API") ?? "false").Trim().ToLowerInvariant() == "true";
    public bool EnableWebSearch => (Environment.GetEnvironmentVariable("ENABLE_WEB_SEARCH") ?? "false").Trim().ToLowerInvariant() == "true";
    public bool CacheSystemInput => (Environment.GetEnvironmentVariable("CACHE_SYSTEM_INPUT") ?? "true").Trim().ToLowerInvariant() == "true";
    public bool CacheBookContext => (Environment.GetEnvironmentVariable("CACHE_BOOK_CONTEXT") ?? "true").Trim().ToLowerInvariant() == "true";
    public bool ResponsesStrictJson => (Environment.GetEnvironmentVariable("RESPONSES_STRICT_JSON") ?? "false").Trim().ToLowerInvariant() == "true";
    public string? IndexMdPath => Environment.GetEnvironmentVariable("INDEX_MD_PATH");
    public bool CustomBeautifyEnabled => (Environment.GetEnvironmentVariable("CUSTOM_MD_BEAUTIFY") ?? "true").Trim().ToLowerInvariant() == "true";
    public bool StripLinks => (Environment.GetEnvironmentVariable("STRIP_LINKS") ?? "false").Trim().ToLowerInvariant() == "true";
}
