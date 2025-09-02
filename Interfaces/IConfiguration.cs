using System;

public interface IConfiguration
{
    bool IsDryRun { get; }
    bool ShowUsage { get; }
    string OpenApiKey { get; }
    string Model { get; }
    int MaxTokensPerCall { get; }
    int NodeDetailWords { get; }
    int NodeSummaryWords { get; }
    int PrevChapterTailChars { get; }
    TimeSpan HttpTimeout { get; }
    bool TreatRefusalAsError { get; }
    bool DemoMode { get; }
    bool DebugLogging { get; }
    bool UseResponsesApi { get; }
    bool EnableWebSearch { get; }
    bool CacheSystemInput { get; }
    bool CacheBookContext { get; }
    bool ResponsesStrictJson { get; }
    string? IndexMdPath { get; }
    bool CustomBeautifyEnabled { get; }
    bool StripLinks { get; }
}
