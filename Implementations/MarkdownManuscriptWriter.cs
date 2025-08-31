using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class MarkdownManuscriptWriter : IManuscriptWriter
{
    public async Task SaveAsync(BookSpecification spec, bool final = false)
    {
        var fullManuscript = new StringBuilder();
        
        // 1. Metadata
        fullManuscript.AppendLine($"# {spec.Title}");
        fullManuscript.AppendLine();
        fullManuscript.AppendLine($"**Público Objetivo:** {spec.TargetAudience}");
        fullManuscript.AppendLine($"**Tema:** {spec.Topic}");
        fullManuscript.AppendLine();

        // 2. Introduction
        fullManuscript.AppendLine("## Introducción");
        fullManuscript.AppendLine();
        fullManuscript.AppendLine(spec.Introduction);
        fullManuscript.AppendLine();

        // 3. Table of Contents
        fullManuscript.AppendLine("## Tabla de Contenidos");
        fullManuscript.AppendLine();
        AppendChapterTree(fullManuscript, spec.TableOfContents, "");
        fullManuscript.AppendLine();

        // 4. Summaries
        fullManuscript.AppendLine("## Resúmenes por Sección");
        fullManuscript.AppendLine();
        AppendSummaries(fullManuscript, spec.TableOfContents);
        fullManuscript.AppendLine();

        // 5. Full Content
        fullManuscript.AppendLine("---");
        fullManuscript.AppendLine();
        AppendContent(fullManuscript, spec.TableOfContents, 1);

        await System.IO.File.WriteAllTextAsync("manuscrito.md", fullManuscript.ToString(), Encoding.UTF8);
        Logger.Append("Archivo manuscrito.md actualizado");

        if (final)
        {
            var onlyChapters = new StringBuilder();
            AppendContent(onlyChapters, spec.TableOfContents, 1, includeHeaders: false);
            await System.IO.File.WriteAllTextAsync("manuscrito_capitulos.md", onlyChapters.ToString(), Encoding.UTF8);
            Logger.Append("Archivo manuscrito_capitulos.md actualizado (final)");
        }
    }

    private void AppendChapterTree(StringBuilder sb, List<ChapterNode> nodes, string indent)
    {
        foreach (var node in nodes)
        {
            sb.AppendLine($"{indent}- {node.Number} {node.Title}");
            if (node.SubChapters.Any())
            {
                AppendChapterTree(sb, node.SubChapters, indent + "  ");
            }
        }
    }

    private void AppendSummaries(StringBuilder sb, List<ChapterNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Summary))
            {
                sb.AppendLine($"### {node.Number} {node.Title}");
                sb.AppendLine(node.Summary);
                sb.AppendLine();
            }
            if (node.SubChapters.Any())
            {
                AppendSummaries(sb, node.SubChapters);
            }
        }
    }

    private void AppendContent(StringBuilder sb, List<ChapterNode> nodes, int level, bool includeHeaders = true)
    {
        var headerPrefix = new string('#', level + 1);
        foreach (var node in nodes)
        {
            if (includeHeaders)
            {
                sb.AppendLine($"{headerPrefix} {node.Number} {node.Title}");
                sb.AppendLine();
            }
            sb.AppendLine(node.Content);
            sb.AppendLine();

            if (node.SubChapters.Any())
            {
                AppendContent(sb, node.SubChapters, level + 1, includeHeaders);
            }
        }
    }
}
