using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

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
        try
        {
            if (!string.IsNullOrEmpty(Logger.RunDirectory))
            {
                var backPath = System.IO.Path.Combine(Logger.RunDirectory, "manuscrito.md");
                await System.IO.File.WriteAllTextAsync(backPath, fullManuscript.ToString(), Encoding.UTF8);
                Logger.Append($"Copia en back: {backPath}");
            }
        }
        catch { /* no interrumpir el flujo por copia fallida */ }

        if (final)
        {
            var onlyChapters = new StringBuilder();
            // Título del libro
            onlyChapters.AppendLine($"# {spec.Title}");
            onlyChapters.AppendLine();
            AppendContent(onlyChapters, spec.TableOfContents, 1, includeHeaders: true);
            await System.IO.File.WriteAllTextAsync("manuscrito_capitulos.md", onlyChapters.ToString(), Encoding.UTF8);
            Logger.Append("Archivo manuscrito_capitulos.md actualizado (final)");
            try
            {
                if (!string.IsNullOrEmpty(Logger.RunDirectory))
                {
                    var backPath2 = System.IO.Path.Combine(Logger.RunDirectory, "manuscrito_capitulos.md");
                    await System.IO.File.WriteAllTextAsync(backPath2, onlyChapters.ToString(), Encoding.UTF8);
                    Logger.Append($"Copia en back: {backPath2}");
                }
            }
            catch { }
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
        // Niveles: 1 => ## Capítulo, 2 => ### Subcapítulo, 3 => #### Sub-sub.
        // Capar en 4 (# título global se escribe aparte)
        var headerLevel = Math.Min(level + 1, 4);
        var headerPrefix = new string('#', headerLevel);
        foreach (var node in nodes)
        {
            if (includeHeaders)
            {
                sb.AppendLine($"{headerPrefix} {node.Number} {node.Title}");
                sb.AppendLine();
            }
            var content = node.Content ?? string.Empty;
            // Sanitizar encabezados internos para no superar nivel #### y evitar duplicar el H3 del subcapítulo
            content = SanitizeInternalHeadings(content, node.Number, node.Title);
            sb.AppendLine(content);
            sb.AppendLine();

            if (node.SubChapters.Any())
            {
                AppendContent(sb, node.SubChapters, level + 1, includeHeaders);
            }
        }
    }

    private static string SanitizeInternalHeadings(string content, string number, string title)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var headerPattern = new Regex($@"^\s*#+\s+{Regex.Escape(number)}\s+{Regex.Escape(title)}\s*$", RegexOptions.Compiled);

        var result = new StringBuilder(content.Length + 64);
        bool firstLineProcessed = false;
        foreach (var raw in lines)
        {
            var line = raw;
            if (!firstLineProcessed)
            {
                // Si la primera línea repite el encabezado del subcapítulo, eliminarla
                if (Regex.IsMatch(line, $@"^\s*#+\s+{Regex.Escape(number)}\s+{Regex.Escape(title)}\s*$"))
                {
                    firstLineProcessed = true;
                    continue;
                }
                firstLineProcessed = true;
            }

            // Normalizar cualquier encabezado a como máximo nivel 4 (####)
            if (Regex.IsMatch(line, @"^\s*#{1,6}\s+"))
            {
                // Capturar el texto del encabezado y reescribir con ####
                var text = Regex.Replace(line, @"^\s*#{1,6}\s+", "").TrimEnd();
                line = $"#### {text}";
            }

            result.AppendLine(line);
        }

        return result.ToString().TrimEnd('\n');
    }
}
