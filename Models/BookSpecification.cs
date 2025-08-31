using System.Collections.Generic;

public class BookSpecification
{
    public string Title { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string ManualSummary { get; set; } = string.Empty;
    public string Introduction { get; set; } = string.Empty;
    public List<ChapterNode> TableOfContents { get; set; } = new();
}
