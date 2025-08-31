using System.Collections.Generic;

public class ChapterNode
{
    public string Title { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<ChapterNode> SubChapters { get; set; } = new();
}
