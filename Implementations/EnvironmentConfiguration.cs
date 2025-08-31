using System;

public class EnvironmentConfiguration : IConfiguration
{
    public bool IsDryRun => (Environment.GetEnvironmentVariable("DRY_RUN") ?? "false").Trim().ToLowerInvariant() == "true";
    public bool ShowUsage => (Environment.GetEnvironmentVariable("SHOW_USAGE") ?? "false").Trim().ToLowerInvariant() == "true";
    public string OpenApiKey => Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    public string Model => Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() ?? "gpt-5-mini";
    public int MaxTokensPerCall => int.TryParse(Environment.GetEnvironmentVariable("OPENAI_MAX_COMPLETION_TOKENS"), out var mt) && mt > 0 ? mt : 4096;
    public int TargetWordsPerChapter
        => int.TryParse(Environment.GetEnvironmentVariable("TARGET_WORDS_PER_CHAPTER"), out var tw) && tw > 0
            ? tw
            : (DemoMode ? 900 : 6500);
    public int PrevChapterTailChars => 4000;
    public TimeSpan HttpTimeout => int.TryParse(Environment.GetEnvironmentVariable("OPENAI_HTTP_TIMEOUT_SECONDS"), out var ts) && ts > 0 ? TimeSpan.FromSeconds(ts) : TimeSpan.FromMinutes(5);
    public bool TreatRefusalAsError => (Environment.GetEnvironmentVariable("TREAT_REFUSAL_AS_ERROR") ?? "true").Trim().ToLowerInvariant() == "true";
    public bool DemoMode => (Environment.GetEnvironmentVariable("DEMO_MODE") ?? "true").Trim().ToLowerInvariant() == "true";
    public int ContentCallsLimit
        => int.TryParse(Environment.GetEnvironmentVariable("CONTENT_CALLS_LIMIT"), out var limit) && limit > 0
            ? limit
            : 8;
    public bool DebugLogging => (Environment.GetEnvironmentVariable("DEBUG") ?? "true").Trim().ToLowerInvariant() == "true";
}
