using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;
using Markdig;
using Markdig.Renderers.Normalize;
using Markdig.Syntax;
using Microsoft.Extensions.Logging;

public class MarkdownManuscriptWriter : IManuscriptWriter
{
    private readonly ILogger<MarkdownManuscriptWriter> _logger;
    private readonly IConfiguration _config;

    public MarkdownManuscriptWriter(ILogger<MarkdownManuscriptWriter> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
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
        var introText = spec.Introduction ?? string.Empty;
        if (_config.StripLinks) introText = StripLinksForPrint(introText);
        fullManuscript.AppendLine(introText);
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

        // Normalización global del documento: primero Markdig, luego reglas propias (opcionales)
        var fullText = fullManuscript.ToString();
        fullText = NormalizeWithMarkdig(fullText);
        if (_config.CustomBeautifyEnabled)
        {
            fullText = FixColonBacktickSpacing(fullText);
            fullText = BeautifyLists(fullText);
        }

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
                await System.IO.File.WriteAllTextAsync(backPath, fullText, Encoding.UTF8);
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
            // Introducción global debajo del título (si existe)
            if (!string.IsNullOrWhiteSpace(spec.Introduction))
            {
                onlyChapters.AppendLine("## Introducción");
                onlyChapters.AppendLine();
                onlyChapters.AppendLine(spec.Introduction.Trim());
                onlyChapters.AppendLine();
            }
            AppendContent(onlyChapters, spec.TableOfContents, 1, includeHeaders: true);
            var onlyChaptersText = onlyChapters.ToString();
            onlyChaptersText = NormalizeWithMarkdig(onlyChaptersText);
            if (_config.CustomBeautifyEnabled)
            {
                onlyChaptersText = FixColonBacktickSpacing(onlyChaptersText);
                onlyChaptersText = BeautifyLists(onlyChaptersText);
            }
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
                    await System.IO.File.WriteAllTextAsync(backPath2, onlyChaptersText, Encoding.UTF8);
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
                var sum = node.Summary ?? string.Empty;
                if (_config.StripLinks) sum = StripLinksForPrint(sum);
                sb.AppendLine(sum);
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
        // Encabezado del nodo: 1 => ## Capítulo, 2 => ### Subcapítulo, 3 => #### Sub-sub.
        // Para el encabezado del nodo, capamos en 4 (# título global se escribe aparte).
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
            // Sanitizar encabezados internos: ajustarlos a un nivel relativo al encabezado del nodo
            // y evitar duplicar el título del propio nodo.
            content = SanitizeInternalHeadings(content, node.Number, node.Title, headerLevel);
            if (_config.CustomBeautifyEnabled)
            {
                // Asegurar separación entre ':' y la siguiente que inicia con backticks
                content = FixColonBacktickSpacing(content);
                // Mejorar legibilidad de listas con líneas en blanco estratégicas
                content = BeautifyLists(content);
            }
            if (_config.StripLinks)
            {
                content = StripLinksForPrint(content);
            }
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
            // Eliminar sugerencias/meta del asistente (p. ej. "Si quieres, puedo …") para que no aparezcan en el manual
            content = StripAssistantMetaSuggestions(content);
            sb.AppendLine(content);
            // Insertar marcadores de gráficos sugeridos (visibles) al final de la sección
            if (node.Diagrams.Any())
            {
                foreach (var d in node.Diagrams)
                {
                    var fmt = string.IsNullOrWhiteSpace(d.Format) ? "(formato no especificado)" : d.Format;
                    var place = string.IsNullOrWhiteSpace(d.Placement) ? "end" : d.Placement;
                    sb.AppendLine($"[DIAGRAMA] {node.Number} {node.Title} → {d.Name} | Formato: {fmt} | Ubicación: {place}");
                }
                sb.AppendLine();
            }
            sb.AppendLine();

            if (node.SubChapters.Any())
            {
                AppendContent(sb, node.SubChapters, level + 1, includeHeaders);
            }
        }
    }

    private static string SanitizeInternalHeadings(string content, string number, string title, int parentHeaderLevel)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder(content.Length + 64);

        // Patrones globales para detectar líneas que repitan el encabezado del propio nodo
        var numEsc = Regex.Escape(number);
        var titleEsc = Regex.Escape(title);
        var dupRegexes = new[]
        {
            new Regex($@"^\s*#{1,6}\s*{numEsc}\s*[\u2014\u2013\-:]?\s*{titleEsc}\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex($@"^\s*#{1,6}\s*{titleEsc}\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex($@"^\s*{numEsc}\s*[\u2014\u2013\-:]?\s*{titleEsc}\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        bool firstNonEmptyProcessed = false;
        foreach (var raw in lines)
        {
            var line = raw;

            // Omitir espacios en blanco iniciales hasta ver la primera línea no vacía
            if (!firstNonEmptyProcessed && string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Omitir cualquier línea que duplique el encabezado del propio nodo (en cualquier posición)
            if (dupRegexes.Any(rx => rx.IsMatch(line)))
            {
                firstNonEmptyProcessed = true;
                continue;
            }

            if (!firstNonEmptyProcessed)
            {
                firstNonEmptyProcessed = true;
            }

            // Normalizar cualquier encabezado interno a un nivel relativo al encabezado del nodo
            // Si el nodo usa 'headerLevel', los encabezados internos se bajan a 'min(headerLevel+1, 6)'
            if (Regex.IsMatch(line, @"^\s*#{1,6}\s+"))
            {
                var text = Regex.Replace(line, @"^\s*#{1,6}\s+", "").TrimEnd();
                // Quitar prefijos numéricos del texto del encabezado (p. ej. "1.1 Título" → "Título")
                text = Regex.Replace(text, @"^\s*(?:\d+(?:\.\d+)*)\s+", "");
                var internalLevel = Math.Min(parentHeaderLevel + 1, 6);
                line = new string('#', internalLevel) + " " + text;
                result.AppendLine(line);
                continue;
            }

            // Convertir líneas que son rótulos de sección con numeración en encabezados internos
            var mNum = Regex.Match(line, @"^\s*(\d+(?:\.\d+)*)\s+(\S.*)$");
            if (mNum.Success)
            {
                var text = mNum.Groups[2].Value.TrimEnd();
                var internalLevel = Math.Min(parentHeaderLevel + 1, 6);
                line = new string('#', internalLevel) + " " + text;
                result.AppendLine(line);
                continue;
            }

            // Opcional: etiquetas comunes sin numeración que suelen ser rótulos
            if (Regex.IsMatch(line.Trim(), @"^(Descripci[oó]n\s+general|Resumen|Introducci[oó]n)\s*$", RegexOptions.IgnoreCase))
            {
                var text = line.Trim();
                var internalLevel = Math.Min(parentHeaderLevel + 1, 6);
                line = new string('#', internalLevel) + " " + text;
                result.AppendLine(line);
                continue;
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

    // Quita bloques de "sugerencias del asistente" que no deben figurar en el manual,
    // como frases del tipo "Si quieres, puedo …", "¿Quieres que …?", etc., y sus listas inmediatas.
    private static string StripAssistantMetaSuggestions(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder(content.Length);
        bool inCode = false;
        bool skippingSuggestionBlock = false;

        // Patrones en ES y EN comunes de ofertas/preguntas meta
        var triggers = new System.Text.RegularExpressions.Regex(
            @"^(\s*(-\s+)?)?(si\s+quieres|si\s+prefieres|puedo\s+|podemos\s+|¿quieres\s+que|¿te\s+gustar[ií]a\s+que|if\s+you\s+want,?\s+i\s+can|would\s+you\s+like\s+me\s+to|i\s+can\s+provide)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var raw in lines)
        {
            var line = raw;
            var trimmed = line.TrimStart();

            // Control de fences de código: no modificar dentro de bloques de código
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inCode = !inCode;
                result.AppendLine(line);
                continue;
            }
            if (inCode)
            {
                result.AppendLine(line);
                continue;
            }

            if (!skippingSuggestionBlock && triggers.IsMatch(trimmed))
            {
                // Empezar a omitir desde esta línea y, si vienen bullets o líneas vacías, seguir omitiendo hasta un párrafo normal
                skippingSuggestionBlock = true;
                continue;
            }

            if (skippingSuggestionBlock)
            {
                // Mientras sea bullet/continuación o línea vacía, seguir omitiendo
                if (string.IsNullOrWhiteSpace(line))
                {
                    // Mantener omisión a través de líneas en blanco; finaliza cuando aparezca un párrafo normal
                    continue;
                }
                var isBullet = System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*([-*+]\s+|\d+\.\s+)");
                if (isBullet)
                {
                    continue;
                }
                // Párrafo normal: dejar de omitir a partir de aquí
                skippingSuggestionBlock = false;
            }

            result.AppendLine(line);
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

    private static string NormalizeWithMarkdig(string input)
    {
        try
        {
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            var doc = Markdown.Parse(input, pipeline);
            using var sw = new StringWriter();
            var renderer = new NormalizeRenderer(sw);
            pipeline.Setup(renderer);
            renderer.Write(doc);
            return sw.ToString();
        }
        catch
        {
            // En caso de cualquier problema con Markdig, devolver el texto original
            return input;
        }
    }

    // Reglas de embellecimiento de Markdown (extensible)
    private static string BeautifyLists(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder(content.Length + 64);

        bool inCode = false;
        string prevLine = "";
        string prevNonBlankLine = "";
        bool prevNonBlankIsBullet = false;
        int prevNonBlankBulletIndent = -1; // válido si prevNonBlankIsBullet
        bool lastEmittedBlank = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Control rudimentario de fences de código
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
            {
                inCode = !inCode;
                result.AppendLine(line);
                prevLine = line;
                continue;
            }
            if (inCode)
            {
                result.AppendLine(line);
                prevLine = line;
                continue;
            }

            // Detectar bullets con su indentación
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\s*)-\s+");
            bool isBullet = m.Success;
            int indent = isBullet ? m.Groups[1].Value.Length : -1;

            // Reglas pedidas:
            // - Antes de que empiece una lista y después de un párrafo NO lista: agregar una línea en blanco.
            // - Dentro de la lista NO agregar líneas en blanco, salvo si empieza una sublista y
            //   la línea no vacía anterior termina en ':' → agregar línea en blanco antes de la sublista.
            if (isBullet)
            {
                if (!string.IsNullOrWhiteSpace(prevNonBlankLine))
                {
                    if (!prevNonBlankIsBullet)
                    {
                        // Párrafo (no lista) → Lista
                        if (!lastEmittedBlank)
                        {
                            result.AppendLine("");
                            lastEmittedBlank = true;
                        }
                    }
                    else
                    {
                        // Lista → ¿Sublista?
                        if (indent > prevNonBlankBulletIndent && prevNonBlankLine.TrimEnd().EndsWith(":"))
                        {
                            if (!lastEmittedBlank)
                            {
                                result.AppendLine("");
                                lastEmittedBlank = true;
                            }
                        }
                    }
                }
            }

            result.AppendLine(line);
            lastEmittedBlank = string.IsNullOrWhiteSpace(line);
            prevLine = line;
            if (!string.IsNullOrWhiteSpace(line))
            {
                prevNonBlankLine = line;
                prevNonBlankIsBullet = isBullet;
                prevNonBlankBulletIndent = isBullet ? indent : -1;
            }
        }

        return result.ToString().TrimEnd('\n').Replace("\n", System.Environment.NewLine);
    }

    private static string StripLinksForPrint(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(content.Length);
        bool inCode = false;
        var linkRx = new Regex(@"\[([^\]]+)\]\((https?://[^\s)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var autoLinkRx = new Regex(@"<https?://[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var bareUrlRx = new Regex(@"https?://[^\s)\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (var raw in lines)
        {
            var line = raw;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inCode = !inCode;
                sb.AppendLine(line);
                continue;
            }
            if (inCode)
            {
                sb.AppendLine(line);
                continue;
            }
            // [text](url) -> text
            line = linkRx.Replace(line, "$1");
            // <http://...> -> ''
            line = autoLinkRx.Replace(line, "");
            // bare urls -> ''
            line = bareUrlRx.Replace(line, "");
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd('\n');
    }
}
