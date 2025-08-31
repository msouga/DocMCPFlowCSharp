using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

public class MarkdownManuscriptWriter : IManuscriptWriter
{
    private readonly ILogger<MarkdownManuscriptWriter> _logger;

    public MarkdownManuscriptWriter(ILogger<MarkdownManuscriptWriter> logger)
    {
        _logger = logger;
    }
    public async Task SaveAsync(BookSpecification spec, bool final = false)
    {
        var fullManuscript = new StringBuilder();
        
        // 1. Portada y metadatos
        fullManuscript.AppendLine($"# {spec.Title}");
        fullManuscript.AppendLine();
        // Resumen del manual (overview del nodo raíz)
        if (!string.IsNullOrWhiteSpace(spec.ManualSummary))
        {
            fullManuscript.AppendLine(spec.ManualSummary.Trim());
            fullManuscript.AppendLine();
        }
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

        // Guardado: sólo cuando final==true; si no, no escribimos a disco
        if (!final)
        {
            _logger.LogInformation("[Preview] Se omitió escritura de manuscrito.md (no final)");
            return;
        }
        try
        {
            // Borrar copias antiguas en raíz si existen
            if (System.IO.File.Exists("manuscrito.md")) System.IO.File.Delete("manuscrito.md");
        }
        catch { }
        try
        {
            if (!string.IsNullOrEmpty(RunContext.BackRunDirectory))
            {
                var backPath = System.IO.Path.Combine(RunContext.BackRunDirectory, "manuscrito.md");
                await System.IO.File.WriteAllTextAsync(backPath, fullManuscript.ToString(), Encoding.UTF8);
                _logger.LogInformation("Archivo manuscrito.md guardado en back: {Path}", backPath);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo escribir manuscrito.md en back"); }

        if (final)
        {
            var onlyChapters = new StringBuilder();
            // Título del libro
            onlyChapters.AppendLine($"# {spec.Title}");
            onlyChapters.AppendLine();
            AppendContent(onlyChapters, spec.TableOfContents, 1, includeHeaders: true);
            try
            {
                // Borrar copias antiguas en raíz si existen
                if (System.IO.File.Exists("manuscrito_capitulos.md")) System.IO.File.Delete("manuscrito_capitulos.md");
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(RunContext.BackRunDirectory))
                {
                    var backPath2 = System.IO.Path.Combine(RunContext.BackRunDirectory, "manuscrito_capitulos.md");
                    await System.IO.File.WriteAllTextAsync(backPath2, onlyChapters.ToString(), Encoding.UTF8);
                    _logger.LogInformation("Archivo manuscrito_capitulos.md guardado en back: {Path}", backPath2);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "No se pudo escribir manuscrito_capitulos.md en back"); }
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
            // Asegurar separación entre líneas que terminan en ':' y la siguiente que inicia con backticks
            content = FixColonBacktickSpacing(content);
            // Si es un capítulo con subcapítulos, impedir que el overview duplique rótulos de subcapítulos
            if (level == 1 && node.SubChapters.Any())
            {
                content = StripSubchapterLabelLines(content, node);
                var trimmed = content.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 60)
                {
                    content = ComposeChapterOverviewFallback(node);
                }
            }
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
    private static string StripSubchapterLabelLines(string content, ChapterNode chapter)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder(content.Length);
        foreach (var raw in lines)
        {
            var line = raw;
            bool isLabel = false;
            foreach (var sc in chapter.SubChapters)
            {
                var patternPlain = $@"^\s*{System.Text.RegularExpressions.Regex.Escape(sc.Number)}\s+{System.Text.RegularExpressions.Regex.Escape(sc.Title)}\s*$";
                var patternH4 = $@"^\s*####\s+{System.Text.RegularExpressions.Regex.Escape(sc.Number)}\s+{System.Text.RegularExpressions.Regex.Escape(sc.Title)}\s*$";
                if (System.Text.RegularExpressions.Regex.IsMatch(line, patternPlain) ||
                    System.Text.RegularExpressions.Regex.IsMatch(line, patternH4))
                {
                    isLabel = true;
                    break;
                }
            }
            if (!isLabel) result.AppendLine(line);
        }
        return result.ToString().TrimEnd('\n');
    }

    private static string ComposeChapterOverviewFallback(ChapterNode chapter)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(chapter.Summary))
        {
            sb.AppendLine(chapter.Summary.Trim());
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine($"Este capítulo, \"{chapter.Title}\", ofrece una visión general de los temas clave y prepara el terreno para los apartados siguientes.");
            sb.AppendLine();
        }

        if (chapter.SubChapters.Any())
        {
            var titles = chapter.SubChapters.Select(sc => sc.Title.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (titles.Count > 0)
            {
                string listado = titles.Count switch
                {
                    1 => titles[0],
                    2 => $"{titles[0]} y {titles[1]}",
                    _ => string.Join(", ", titles.Take(titles.Count - 1)) + $" y {titles.Last()}"
                };
                sb.AppendLine($"En particular, se abordan aspectos como {listado}, ofreciendo un hilo conductor que permite entender cómo encajan entre sí.");
                sb.AppendLine();
            }
        }

        sb.AppendLine("Al finalizar, el lector contará con un mapa conceptual claro del capítulo y sabrá qué esperar en los apartados posteriores.");
        return sb.ToString().Trim();
    }

    

    private static string FixColonBacktickSpacing(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        // Regla general: siempre asegurar dos saltos de línea después de una línea que termina en ':'
        // Si ya hay dos saltos, no hace nada; si hay uno, inserta uno extra.
        var text = content.Replace("\r\n", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @":\n(?!\n)",
            ":\n\n",
            System.Text.RegularExpressions.RegexOptions.Multiline
        );
        return text.Replace("\n", System.Environment.NewLine);
    }
}
