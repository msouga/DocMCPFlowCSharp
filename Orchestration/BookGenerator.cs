using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using Markdig;
using Markdig.Renderers.Normalize;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class BookGenerator : IBookFlowOrchestrator
{
    private readonly IConfiguration _config;
    private readonly IUserInteraction _ui;
    private readonly ILlmClient _llm;
    private readonly IManuscriptWriter _writer;
    private readonly BookSpecification _spec = new();
    private readonly ILogger<BookGenerator> _logger;
    private bool _tocLoadedFromFile = false;

    public BookGenerator(IConfiguration config, IUserInteraction ui, ILlmClient llm, IManuscriptWriter writer, ILogger<BookGenerator> logger)
    {
        _config = config;
        _ui = ui;
        _llm = llm;
        _writer = writer;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        CollectInitialInputs();
        // En modo unattended (parámetros cargados explícitamente), si se indicó INDEX_MD_PATH y no existe, abortar
        if (RunContext.ExecutionParametersLoaded && !string.IsNullOrWhiteSpace(_config.IndexMdPath) && !System.IO.File.Exists(_config.IndexMdPath))
        {
            _logger.LogError("Error TOC File not found: '{Path}'", _config.IndexMdPath);
            throw new InvalidOperationException($"Error TOC File not found: '{_config.IndexMdPath}'");
        }
        _logger.LogInformation("Entradas iniciales — Título: '{Title}', Público: '{Audience}', Tema: '{Topic}'", _spec.Title, _spec.TargetAudience, _spec.Topic);
        await GenerateTableOfContents();
        if (!_tocLoadedFromFile && !RunContext.ExecutionParametersLoaded)
        {
            DisplayAndEditTableOfContents();
        }
        _logger.LogInformation("Índice finalizado por el usuario");
        NumberNodes(_spec.TableOfContents, "", 1);

        // Definir contexto estable del libro para Responses API cacheable (por corrida)
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Contexto del Libro (estable por corrida):");
            sb.AppendLine($"- Título: {_spec.Title}");
            sb.AppendLine($"- Público: {_spec.TargetAudience}");
            sb.AppendLine($"- Tema: {_spec.Topic}");
            sb.AppendLine("- TOC:");
            sb.AppendLine(BuildTocString());
            RunContext.BookContextStable = sb.ToString();
            _logger.LogInformation("BookContextStable preparado (len={Len})", RunContext.BookContextStable.Length);
        }
        catch { /* best-effort */ }

        await GenerateIntroduction();
        _logger.LogInformation("Introducción generada (len={Len})", _spec.Introduction?.Length ?? 0);

        _ui.WriteLine("\n[Proceso] Generando resúmenes para la estructura final...", ConsoleColor.Green);
        await GenerateSummaries();
        DisplaySummaries();
        // Generar resumen del manual (overview del nodo raíz) usando los resúmenes de capítulos
        if (string.IsNullOrWhiteSpace(_spec.ManualSummary))
        {
            try
            {
                var rootNode = new ChapterNode { Title = _spec.Title, Number = "", SubChapters = _spec.TableOfContents };
                var manualOverviewPrompt = PromptBuilder.GetChapterOverviewPrompt(
                    _spec.Title,
                    _spec.Topic,
                    _spec.TargetAudience,
                    rootNode,
                    "(ninguno)",
                    _config.NodeSummaryWords);
                _spec.ManualSummary = await _llm.AskAsync(PromptBuilder.SystemPrompt, manualOverviewPrompt, _config.Model, _config.MaxTokensPerCall);
            }
            catch {}
        }
        await _writer.SaveAsync(_spec);
        _logger.LogInformation("Guardado de manuscrito luego de resúmenes");

        if (_config.IsDryRun)
        {
            _ui.WriteLine("\n[DRY_RUN] Finalizando sin generar contenido. Revisa la estructura y los resúmenes.", ConsoleColor.Yellow);
            return;
        }

        _ui.WriteLine("\n[Proceso] Generando contenido completo del documento...", ConsoleColor.Green);
        await GenerateChapterAndSubchapterContent();
        await _writer.SaveAsync(_spec, final: true);
        _logger.LogInformation("Guardado final de manuscrito (contenido completo)");
        
        _ui.WriteLine("\nListo. Archivo generado: manuscrito.md\n", ConsoleColor.Green);

        // Generar archivo con sugerencias de gráficos (PlantUML/Mermaid/texto)
        try
        {
            await GenerateAndSaveDiagramSuggestions();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron generar sugerencias de gráficos");
        }
    }

    private void CollectInitialInputs()
    {
        var indexPath = _config.IndexMdPath;
        var hasIndexFile = !string.IsNullOrWhiteSpace(indexPath) && System.IO.File.Exists(indexPath);
        // Permitir preconfigurar respuestas por variables de entorno (modo no interactivo)
        string? envAudience = Environment.GetEnvironmentVariable("TARGET_AUDIENCE");
        if (!string.IsNullOrWhiteSpace(envAudience)) envAudience = envAudience!.Trim();
        string? envTopic = Environment.GetEnvironmentVariable("TOPIC");
        if (!string.IsNullOrWhiteSpace(envTopic)) envTopic = envTopic!.Trim();
        string? envTitle = Environment.GetEnvironmentVariable("DOC_TITLE");
        if (!string.IsNullOrWhiteSpace(envTitle)) envTitle = envTitle!.Trim();

        if (hasIndexFile)
        {
            // Precargar título desde H1 del archivo
            TryLoadTitleFromIndexFile();
            // No pedir título. Pedir solo Público y Descripción.
            if (!string.IsNullOrWhiteSpace(envAudience))
            {
                _spec.TargetAudience = envAudience!;
            }
            else
            {
                _spec.TargetAudience = _ui.ReadLine("Público objetivo (ej. Principiante, Experto): ").Trim();
                while (string.IsNullOrWhiteSpace(_spec.TargetAudience))
                    _spec.TargetAudience = _ui.ReadLine("Por favor, ingresa el público objetivo: ").Trim();
            }

            if (!string.IsNullOrWhiteSpace(envTopic))
            {
                _spec.Topic = envTopic!;
            }
            else
            {
                _spec.Topic = _ui.ReadLine("Descripción breve del documento: ").Trim();
                while (string.IsNullOrWhiteSpace(_spec.Topic))
                    _spec.Topic = _ui.ReadLine("Por favor, ingresa una descripción breve: ").Trim();
            }
        }
        else
        {
            // Flujo original (sin índice externo)
            if (!string.IsNullOrWhiteSpace(envTitle))
            {
                _spec.Title = envTitle!;
            }
            else
            {
                _spec.Title = _ui.ReadLine("Título del documento: ").Trim();
                while (string.IsNullOrWhiteSpace(_spec.Title))
                    _spec.Title = _ui.ReadLine("Por favor, ingresa un título: ").Trim();
            }

            if (!string.IsNullOrWhiteSpace(envAudience))
            {
                _spec.TargetAudience = envAudience!;
            }
            else
            {
                _spec.TargetAudience = _ui.ReadLine("Público Objetivo (ej. Principiante, Experto): ").Trim();
                while (string.IsNullOrWhiteSpace(_spec.TargetAudience))
                    _spec.TargetAudience = _ui.ReadLine("Por favor, ingresa el público objetivo: ").Trim();
            }

            if (!string.IsNullOrWhiteSpace(envTopic))
            {
                _spec.Topic = envTopic!;
            }
            else
            {
                _spec.Topic = _ui.ReadLine("Tema (ej. Azure Functions, Patrones de Diseño): ").Trim();
                while (string.IsNullOrWhiteSpace(_spec.Topic))
                    _spec.Topic = _ui.ReadLine("Por favor, ingresa un tema: ").Trim();
            }
        }
    }

    private void TryLoadTitleFromIndexFile()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_config.IndexMdPath) &&
                System.IO.File.Exists(_config.IndexMdPath))
            {
                var path = _config.IndexMdPath!;
                var all = System.IO.File.ReadAllLines(path);
                int h1Index = -1;
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i].Trim();
                    if (t.StartsWith("# "))
                    {
                        _spec.Title = t.Substring(2).Trim();
                        h1Index = i;
                        _logger.LogInformation("Título precargado desde INDEX_MD_PATH: {Title}", _spec.Title);
                        break;
                    }
                }
                // Capturar texto debajo del título (descripción global). Borrar líneas en blanco.
                if (h1Index >= 0)
                {
                    var sb = new StringBuilder();
                    for (int k = h1Index + 1; k < all.Length; k++)
                    {
                        var ln = all[k];
                        if (System.Text.RegularExpressions.Regex.IsMatch(ln, @"^\s{0,3}#{1,6}\s+")) break; // siguiente encabezado
                        if (!string.IsNullOrWhiteSpace(ln)) sb.AppendLine(ln.TrimEnd());
                    }
                    var desc = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(desc))
                    {
                        if (string.IsNullOrWhiteSpace(_spec.Topic)) _spec.Topic = desc;
                        if (string.IsNullOrWhiteSpace(_spec.ManualSummary)) _spec.ManualSummary = desc;
                    }
                }
            }
        }
        catch { /* no-op */ }
    }

    private async Task GenerateTableOfContents()
    {
        // 1) Índice externo por archivo (si existe) anula Demo/LLM
        if (TryLoadTocFromIndexFile())
        {
            _ui.WriteLine("\n[Índice] Cargado desde archivo externo (INDEX_MD_PATH).", ConsoleColor.Yellow);
            _logger.LogInformation("TOC cargado desde archivo externo");
            _tocLoadedFromFile = true;
            return;
        }
        if (_config.DemoMode)
        {
            _ui.WriteLine("\n[Demo] Modo demo activo: usando índice mínimo (2 capítulos × 2 subcapítulos).", ConsoleColor.Yellow);
            _spec.TableOfContents = BuildDemoToc();
            _logger.LogInformation("DemoMode: TOC demo 2x2 construido");
            return;
        }

        _ui.WriteLine("\n[Proceso] Generando propuesta de estructura de capítulos...", ConsoleColor.Green);
        var prompt = PromptBuilder.GetIndexPrompt(_spec.Title, _spec.Topic, _spec.TargetAudience);

        var jsonResponse = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall);

        try
        {
            _spec.TableOfContents = ParseToc(jsonResponse);
            _logger.LogInformation("TOC generado por LLM (nodos raíz={Count})", _spec.TableOfContents.Count);
        }
        catch (JsonException ex)
        {
            _ui.WriteLine($"Error al procesar la estructura JSON: {ex.Message}", ConsoleColor.Red);
            _ui.WriteLine("No se pudo generar una estructura. Saliendo.", ConsoleColor.Red);
            _logger.LogError(ex, "Error al parsear TOC JSON: {Message}", ex.Message);
            Environment.Exit(1);
        }
    }

    private bool TryLoadTocFromIndexFile()
    {
        try
        {
            var idxPath = _config.IndexMdPath;
            if (string.IsNullOrWhiteSpace(idxPath) || !System.IO.File.Exists(idxPath)) return false;

            _logger.LogInformation("Leyendo índice externo desde: {Path}", idxPath);
            var rawText = System.IO.File.ReadAllText(idxPath);
            var lines = rawText.Replace("\r\n", "\n").Split('\n');
            _logger.LogInformation("Archivo índice cargado (bytes={Bytes}, líneas={Lines})", rawText.Length, lines.Length);
            // Muestra un extracto inicial del archivo (primeras 25 líneas)
            var preview = string.Join("\n", lines.Take(25));
            _logger.LogDebug("Preview índice (primeras 25 líneas):\n{Preview}", preview);
            var toc = new List<ChapterNode>();
            var stack = new Dictionary<int, ChapterNode>();
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                var line = raw.TrimEnd();
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.*\S)\s*$");
                if (!m.Success) continue;
                var level = m.Groups[1].Value.Length; // 1..6
                var text = m.Groups[2].Value.Trim();
                // Limpiar prefijos numéricos del texto del encabezado para evitar duplicación
                text = CleanHeadingText(text, level);
                if (level == 1)
                {
                    if (string.IsNullOrWhiteSpace(_spec.Title)) _spec.Title = text;
                    // Capturar descripción global si todavía no fue detectada
                    if (string.IsNullOrWhiteSpace(_spec.ManualSummary))
                    {
                        var descSb = new StringBuilder();
                        int kdesc = i + 1;
                        while (kdesc < lines.Length && string.IsNullOrWhiteSpace(lines[kdesc])) kdesc++; // borrar líneas en blanco
                        while (kdesc < lines.Length)
                        {
                            var peek = lines[kdesc];
                            if (System.Text.RegularExpressions.Regex.IsMatch(peek, @"^\s{0,3}#{1,6}\s+")) break; // próximo encabezado
                            if (!string.IsNullOrWhiteSpace(peek)) descSb.AppendLine(peek.TrimEnd());
                            kdesc++;
                        }
                        var desc = descSb.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(desc))
                        {
                            _spec.ManualSummary = desc;
                            if (string.IsNullOrWhiteSpace(_spec.Topic)) _spec.Topic = desc;
                        }
                    }
                    continue; // H1 es título global
                }

                var node = new ChapterNode { Title = text };
                // Adjuntar a la jerarquía según el nivel
                if (level == 2)
                {
                    toc.Add(node);
                    stack[2] = node;
                    stack.Remove(3); stack.Remove(4); stack.Remove(5); stack.Remove(6);
                }
                else if (level == 3)
                {
                    if (stack.TryGetValue(2, out var parent)) parent.SubChapters.Add(node);
                    stack[3] = node;
                    stack.Remove(4); stack.Remove(5); stack.Remove(6);
                }
                else if (level == 4)
                {
                    if (stack.TryGetValue(3, out var parent)) parent.SubChapters.Add(node);
                    stack[4] = node;
                    stack.Remove(5); stack.Remove(6);
                }
                else // level >= 5
                {
                    if (stack.TryGetValue(4, out var parent)) parent.SubChapters.Add(node);
                    stack[5] = node;
                }

                // Intentar capturar un sumario inline justo debajo del encabezado
                // Regla: tomar todo el texto inmediatamente posterior (ignorando líneas en blanco iniciales)
                // hasta el PRÓXIMO ENCABEZADO o fin de archivo. Se eliminan líneas en blanco para mejorar la recuperación.
                var sbSum = new System.Text.StringBuilder();
                int k = i + 1;
                // Saltar líneas en blanco iniciales entre el encabezado y el párrafo del resumen
                while (k < lines.Length && string.IsNullOrWhiteSpace(lines[k])) k++;
                bool sawAny = false;
                bool inFence = false;
                while (k < lines.Length)
                {
                    var peek = lines[k];
                    var trimmed = peek.TrimStart();
                    // Control de fences de código: preservar contenido tal cual dentro de fences
                    if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                    {
                        inFence = !inFence;
                        sbSum.AppendLine(peek);
                        sawAny = true;
                        k++;
                        continue;
                    }
                    if (!inFence && System.Text.RegularExpressions.Regex.IsMatch(peek, @"^\s{0,3}#{1,6}\s+")) break; // siguiente encabezado
                    if (inFence)
                    {
                        sbSum.AppendLine(peek);
                        sawAny = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(peek))
                    {
                        sbSum.AppendLine(peek.TrimEnd());
                        sawAny = true;
                    }
                    k++;
                }
                var inlineSummary = sbSum.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(inlineSummary) && sawAny)
                {
                    node.Summary = inlineSummary;
                }

                // Continuar el bucle desde la línea actual (no saltamos k para no perder subniveles entre medias)
            }
            if (toc.Count == 0)
            {
                // Fallback: intentar parsear listas con guiones como estructura ("- Capítulo", "  - Subcapítulo")
                var listToc = ParseTocFromBulletList(lines);
                if (listToc.Count > 0)
                {
                    toc = listToc;
                }
            }
            if (toc.Count == 0)
            {
                // Fallback: intentar parsear líneas numeradas tipo "1 Título", "1.1 Subtítulo", etc.
                var numToc = ParseTocFromNumberedLines(lines);
                if (numToc.Count > 0)
                {
                    toc = numToc;
                }
            }
            if (toc.Count > 0)
            {
                _spec.TableOfContents = toc;
                EnsureSummariesPresent(_spec.TableOfContents);
                // Métricas de estructura
                int h2 = toc.Count;
                int h3 = toc.Sum(c => c.SubChapters.Count);
                int h4 = toc.Sum(c => c.SubChapters.Sum(sc => sc.SubChapters.Count));
                _logger.LogInformation("Índice parseado: H2={H2} H3={H3} H4={H4}", h2, h3, h4);
                // Listado plano del TOC
                var sb = new System.Text.StringBuilder();
                void Dump(List<ChapterNode> nodes, string indent)
                {
                    foreach (var n in nodes)
                    {
                        sb.AppendLine($"{indent}- {n.Title}");
                        if (n.SubChapters.Any()) Dump(n.SubChapters, indent + "  ");
                    }
                }
                Dump(_spec.TableOfContents, "");
                _logger.LogDebug("TOC desde archivo:\n{Toc}", sb.ToString());
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al cargar TOC desde INDEX_MD_PATH");
        }
        return false;
    }

    // Fallback para índices definidos como lista con guiones (- item). Indentación con 2 espacios indica nivel.
    private List<ChapterNode> ParseTocFromBulletList(string[] lines)
    {
        var toc = new List<ChapterNode>();
        var stack = new Dictionary<int, ChapterNode>(); // nivel lógico -> nodo
        int? baseIndent = null;
        bool inCode = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmedStart = raw.TrimStart();
            // Evitar bloques de código
            if (trimmedStart.StartsWith("```") || trimmedStart.StartsWith("~~~"))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode) continue;

            var m = System.Text.RegularExpressions.Regex.Match(raw, @"^(\s*)-\s+(.*\S)\s*$");
            if (!m.Success) continue;
            int indent = m.Groups[1].Value.Length;
            string text = m.Groups[2].Value.Trim();
            if (baseIndent == null) baseIndent = indent;
            int rel = Math.Max(0, indent - baseIndent.Value);
            int level = 2 + (rel / 2); // 0 -> H2, 2 -> H3, 4 -> H4, etc.

            var node = new ChapterNode { Title = CleanHeadingText(text, level) };
            if (level <= 2)
            {
                toc.Add(node);
                stack[2] = node;
                stack.Remove(3); stack.Remove(4); stack.Remove(5); stack.Remove(6);
            }
            else if (level == 3)
            {
                if (stack.TryGetValue(2, out var parent)) parent.SubChapters.Add(node);
                stack[3] = node;
                stack.Remove(4); stack.Remove(5); stack.Remove(6);
            }
            else if (level == 4)
            {
                if (stack.TryGetValue(3, out var parent)) parent.SubChapters.Add(node);
                stack[4] = node;
                stack.Remove(5); stack.Remove(6);
            }
            else
            {
                if (stack.TryGetValue(4, out var parent)) parent.SubChapters.Add(node);
                stack[5] = node;
            }

            // Capturar resumen inline no en formato bullet debajo (hasta próxima viñeta o encabezado)
            var sb = new StringBuilder();
            int k = i + 1;
            while (k < lines.Length && string.IsNullOrWhiteSpace(lines[k])) k++;
            bool sawAny = false;
            bool inFence2 = false;
            while (k < lines.Length)
            {
                var peek = lines[k];
                var trimmed = peek.TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    inFence2 = !inFence2;
                    sb.AppendLine(peek);
                    sawAny = true;
                    k++;
                    continue;
                }
                if (!inFence2 && (System.Text.RegularExpressions.Regex.IsMatch(peek, @"^\s{0,3}#{1,6}\s+") || System.Text.RegularExpressions.Regex.IsMatch(peek, @"^\s*-\s+"))) break;
                if (inFence2)
                {
                    sb.AppendLine(peek);
                    sawAny = true;
                }
                else if (!string.IsNullOrWhiteSpace(peek)) { sb.AppendLine(peek.TrimEnd()); sawAny = true; }
                k++;
            }
            var sum = sb.ToString().Trim();
            if (sawAny && !string.IsNullOrWhiteSpace(sum)) node.Summary = sum;
        }
        return toc;
    }

    // Fallback para índices con numeración ("1 Título", "1.1 Subtítulo", ...)
    private List<ChapterNode> ParseTocFromNumberedLines(string[] lines)
    {
        var toc = new List<ChapterNode>();
        var stack = new Dictionary<int, ChapterNode>();
        bool inCode = false;
        var rxNum = new System.Text.RegularExpressions.Regex(@"^\s*(\d+(?:\.\d+)*)\s+(.*\S)\s*$");
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmedStart = raw.TrimStart();
            if (trimmedStart.StartsWith("```") || trimmedStart.StartsWith("~~~"))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode) continue;

            var m = rxNum.Match(raw);
            if (!m.Success) continue;
            var num = m.Groups[1].Value;
            var title = m.Groups[2].Value.Trim();
            int dotCount = num.Count(c => c == '.');
            int level = 2 + dotCount; // 0 puntos -> H2; 1 punto -> H3; 2 -> H4

            var node = new ChapterNode { Title = CleanHeadingText(title, level) };
            if (level <= 2)
            {
                toc.Add(node);
                stack[2] = node;
                stack.Remove(3); stack.Remove(4); stack.Remove(5); stack.Remove(6);
            }
            else if (level == 3)
            {
                if (stack.TryGetValue(2, out var parent)) parent.SubChapters.Add(node);
                stack[3] = node;
                stack.Remove(4); stack.Remove(5); stack.Remove(6);
            }
            else if (level == 4)
            {
                if (stack.TryGetValue(3, out var parent)) parent.SubChapters.Add(node);
                stack[4] = node;
                stack.Remove(5); stack.Remove(6);
            }
            else
            {
                if (stack.TryGetValue(4, out var parent)) parent.SubChapters.Add(node);
                stack[5] = node;
            }

            // Capturar resumen debajo hasta próxima línea numerada, encabezado o bullet
            var sb = new StringBuilder();
            int k = i + 1;
            while (k < lines.Length && string.IsNullOrWhiteSpace(lines[k])) k++;
            bool sawAny = false;
            bool inFence3 = false;
            while (k < lines.Length)
            {
                var peek = lines[k];
                var trimmed = peek.TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    inFence3 = !inFence3;
                    sb.AppendLine(peek);
                    sawAny = true;
                    k++;
                    continue;
                }
                if (!inFence3 && (System.Text.RegularExpressions.Regex.IsMatch(peek, @"^\s{0,3}#{1,6}\s+") ||
                    System.Text.RegularExpressions.Regex.IsMatch(peek, @"^\s*-\s+") ||
                    rxNum.IsMatch(peek))) break;
                if (inFence3)
                {
                    sb.AppendLine(peek);
                    sawAny = true;
                }
                else if (!string.IsNullOrWhiteSpace(peek)) { sb.AppendLine(peek.TrimEnd()); sawAny = true; }
                k++;
            }
            var sum = sb.ToString().Trim();
            if (sawAny && !string.IsNullOrWhiteSpace(sum)) node.Summary = sum;
        }
        return toc;
    }

    private List<ChapterNode> BuildDemoToc()
    {
        return new List<ChapterNode>
        {
            new ChapterNode
            {
                Title = "Conceptos básicos",
                SubChapters =
                {
                    new ChapterNode{ Title = "Introducción" },
                    new ChapterNode{ Title = "Primeros pasos" }
                }
            },
            new ChapterNode
            {
                Title = "Aplicación práctica",
                SubChapters =
                {
                    new ChapterNode{ Title = "Ejemplo guiado" },
                    new ChapterNode{ Title = "Buenas prácticas" }
                }
            }
        };
    }

    private List<ChapterNode> ParseToc(string json)
    {
        var nodes = new List<ChapterNode>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            nodes.Add(ParseChapterNode(element));
        }
        return nodes;
    }

    private ChapterNode ParseChapterNode(JsonElement element)
    {
        var node = new ChapterNode();
        if (element.TryGetProperty("title", out var titleElement))
        {
            node.Title = titleElement.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty("subchapters", out var subchaptersElement) && subchaptersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var subElement in subchaptersElement.EnumerateArray())
            {
                node.SubChapters.Add(ParseChapterNode(subElement));
            }
        }
        return node;
    }

    private void DisplayAndEditTableOfContents()
    {
        string command;
        do
        {
            _ui.WriteLine("\n— Estructura Propuesta —", ConsoleColor.Yellow);
            NumberNodes(_spec.TableOfContents, "", 1);
            DisplayToc(_spec.TableOfContents, "");
            _ui.WriteLine("\nComandos: [editar <num>], [borrar <num>], [agregar <num>], [nuevo], [fin]", ConsoleColor.Yellow);
            command = _ui.ReadLine("> ").Trim().ToLower();

            var parts = command.Split(' ', 2);
            var action = parts[0];
            var target = parts.Length > 1 ? parts[1] : null;

            var (node, parent) = target != null ? FindNode(_spec.TableOfContents, target) : (null, null);

            switch (action)
            {
                case "editar":
                    if (node != null) node.Title = _ui.ReadLine($"Nuevo título para '{node.Title}': ").Trim();
                    else _ui.WriteLine("Número no encontrado.", ConsoleColor.Red);
                    break;
                case "borrar":
                    if (node != null && parent != null) parent.SubChapters.Remove(node);
                    else if (node != null) _spec.TableOfContents.Remove(node);
                    else _ui.WriteLine("Número no encontrado.", ConsoleColor.Red);
                    break;
                case "agregar":
                    if (node != null)
                    {
                        node.SubChapters.Add(new ChapterNode { Title = _ui.ReadLine($"Título del nuevo subcapítulo para '{node.Number} {node.Title}': ").Trim() });
                    }
                    else
                    {
                        _ui.WriteLine("Comando 'agregar' requiere un número de capítulo válido donde anidar el subcapítulo.", ConsoleColor.Red);
                    }
                    break;
                case "nuevo":
                     _spec.TableOfContents.Add(new ChapterNode { Title = _ui.ReadLine("Título del nuevo capítulo principal: ").Trim() });
                    break;
            }
        } while (command != "fin");
    }

    private void DisplayToc(List<ChapterNode> nodes, string indent)
    {
        foreach (var node in nodes)
        {
            var sum = string.IsNullOrWhiteSpace(node.Summary) ? "(sin sumario)" : node.Summary.Replace('\n', ' ').Trim();
            if (sum.Length > 160) sum = sum.Substring(0, 157) + "...";
            _ui.WriteLine($"{indent}{node.Number} {node.Title} — {sum}");
            if (node.SubChapters.Any())
            {
                DisplayToc(node.SubChapters, indent + "  ");
            }
        }
    }

    private void EnsureSummariesPresent(List<ChapterNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.Summary == null) n.Summary = string.Empty;
            if (n.SubChapters.Any()) EnsureSummariesPresent(n.SubChapters);
        }
    }

    private (ChapterNode? node, ChapterNode? parent) FindNode(List<ChapterNode> nodes, string number, ChapterNode? parent = null)
    {
        foreach (var node in nodes)
        {
            if (node.Number == number) return (node, parent);
            var (found, p) = FindNode(node.SubChapters, number, node);
            if (found != null) return (found, p);
        }
        return (null, null);
    }

    private void NumberNodes(List<ChapterNode> nodes, string prefix, int level)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var currentNumber = $"{prefix}{i + 1}";
            node.Number = currentNumber;
            node.Level = level;
            if (node.SubChapters.Any())
            {
                NumberNodes(node.SubChapters, currentNumber + ".", level + 1);
            }
        }
    }

    private async Task GenerateIntroduction()
    {
        _ui.WriteLine("\n[Proceso] Generando introducción del documento...", ConsoleColor.Green);
        var tocString = BuildTocString();
        var prompt = PromptBuilder.GetIntroductionPrompt(_spec.Title, _spec.Topic, _spec.TargetAudience, tocString);
        _spec.Introduction = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall);
        _ui.WriteLine("\n— Introducción Generada —", ConsoleColor.Yellow);
        _ui.WriteLine(_spec.Introduction);
    }

    private async Task GenerateSummaries()
    {
        string previousSummary = "(ninguno)";
        foreach (var chapter in _spec.TableOfContents)
        {
            _ui.WriteLine($"- Generando resúmenes para el bloque del capítulo {chapter.Number} {chapter.Title} ...", ConsoleColor.DarkGray);
            var prompt = PromptBuilder.GetSummariesForChapterBlockPrompt(_spec.Title, _spec.TargetAudience, chapter, previousSummary);
            _logger.LogInformation("LLM Summaries prompt para capítulo {Number}: '{Title}'", chapter.Number, chapter.Title);
            
            var summariesJson = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall, jsonSchema: "{{}}"); // Request JSON output

            try
            {
                using var doc = JsonDocument.Parse(summariesJson);
                int count = 0;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var (node, _) = FindNode(_spec.TableOfContents, prop.Name);
                    if (node != null)
                    {
                        if (string.IsNullOrWhiteSpace(node.Summary))
                        {
                            node.Summary = prop.Value.GetString() ?? "";
                        }
                        count++;
                    }
                }
                _logger.LogInformation("Resúmenes actualizados para capítulo {Number}: {Count} nodos", chapter.Number, count);
            }
            catch (JsonException ex)
            {
                _ui.WriteLine($"Error al procesar resúmenes JSON para el capítulo {chapter.Number}: {ex.Message}.", ConsoleColor.Yellow);
                _logger.LogError(ex, "Error al parsear resúmenes capítulo {Number}: {Message}", chapter.Number, ex.Message);
            }

            previousSummary = chapter.Summary;
        }
    }

    private void DisplaySummaries()
    {
        _ui.WriteLine("\n— Resúmenes Generados —", ConsoleColor.Yellow);
        DisplayNodeSummaries(_spec.TableOfContents);
    }

    private void DisplayNodeSummaries(List<ChapterNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Summary))
            {
                _ui.WriteLine($"\n[{node.Number}] {node.Title}", ConsoleColor.Cyan);
                _ui.WriteLine(node.Summary);
            }
            if (node.SubChapters.Any())
            {
                DisplayNodeSummaries(node.SubChapters);
            }
        }
    }

    private async Task GenerateContent(List<ChapterNode> nodes, ChapterNode? parent)
    {
        var fullToc = BuildTocString();
        foreach (var node in nodes)
        {
            var parentSummary = parent?.Summary ?? "(ninguno)";
            if (parent != null && string.IsNullOrEmpty(parent.Summary))
            {
                var mainChapterNumber = node.Number.Split('.').First();
                var (mainChapter, _) = FindNode(_spec.TableOfContents, mainChapterNumber);
                parentSummary = mainChapter?.Summary ?? "(Resumen del capítulo principal no disponible)";
            }

            _ui.WriteLine($"\n>>> Generando contenido para {node.Number} {node.Title} …", ConsoleColor.Cyan);
            _logger.LogInformation("LLM Content prompt para nodo {Number}: '{Title}'", node.Number, node.Title);
            var prompt = PromptBuilder.GetChapterPrompt(_spec.Title, _spec.Topic, _spec.TargetAudience, fullToc, node, parentSummary, _config.NodeDetailWords);
            node.Content = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall);
            _logger.LogInformation("Contenido recibido nodo {Number} (len={Len}) – guardando", node.Number, node.Content?.Length ?? 0);
            await _writer.SaveAsync(_spec);

            if (node.SubChapters.Any())
            {
                await GenerateContent(node.SubChapters, node);
            }
        }
    }

    private async Task GenerateChapterAndSubchapterContent()
    {
        // Recorre todo el árbol: overview si el nodo tiene hijos; detalle si es hoja
        await GenerateNodeContentRecursive(_spec.TableOfContents, parent: null);
    }

    private async Task GenerateNodeContentRecursive(List<ChapterNode> nodes, ChapterNode? parent)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            _ui.WriteLine($"\n>>> Generando contenido para {node.Number} {node.Title} …", ConsoleColor.Cyan);
            _logger.LogInformation("LLM Content prompt para nodo {Number}: '{Title}'", node.Number, node.Title);

            if (node.SubChapters.Any())
            {
                var prevSummary = parent?.Summary ?? "(ninguno)";
                var overviewPrompt = PromptBuilder.GetChapterOverviewPrompt(
                    _spec.Title,
                    _spec.Topic,
                    _spec.TargetAudience,
                    node,
                    prevSummary,
                    _config.NodeSummaryWords);
                node.Content = await _llm.AskAsync(PromptBuilder.SystemPrompt, overviewPrompt, _config.Model, _config.MaxTokensPerCall);
                if (IsOverviewWeak(node.Content, node))
                {
                    var strictPrompt = overviewPrompt + "\n\nInstrucción adicional: si el texto queda demasiado corto, amplíalo con más detalles contextuales y, si ayuda, añade una tabla Markdown breve para resumir comparativas o categorías relevantes. No enumeres los títulos de subcapítulos.";
                    _logger.LogInformation("Overview débil para nodo {Number}; reintentando con prompt ampliado", node.Number);
                    node.Content = await _llm.AskAsync(PromptBuilder.SystemPrompt, strictPrompt, _config.Model, _config.MaxTokensPerCall);
                }
                _logger.LogInformation("Contenido recibido nodo {Number} (len={Len}) – guardando", node.Number, node.Content?.Length ?? 0);
                await _writer.SaveAsync(_spec);
                await GenerateNodeContentRecursive(node.SubChapters, node);
            }
            else
            {
                var parentSummary = parent?.Summary ?? "(ninguno)";
                var prompt = PromptBuilder.GetChapterPrompt(
                    _spec.Title,
                    _spec.Topic,
                    _spec.TargetAudience,
                    BuildTocString(),
                    node,
                    parentSummary,
                    _config.NodeDetailWords);
                node.Content = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall);
                if (string.IsNullOrWhiteSpace(node.Content))
                {
                    _ui.WriteLine($"[ERROR] El LLM devolvió contenido vacío para la sección {node.Number} {node.Title}.", ConsoleColor.Red);
                    _logger.LogError("Contenido vacío en hoja {Number} — '{Title}'", node.Number, node.Title);
                    throw new InvalidOperationException($"LLM devolvió contenido vacío para la sección {node.Number} {node.Title}.");
                }
                _logger.LogInformation("Contenido recibido nodo {Number} (len={Len}) – guardando", node.Number, node.Content?.Length ?? 0);
                await _writer.SaveAsync(_spec);
            }
        }
    }

    private bool IsOverviewWeak(string? content, ChapterNode chapter)
    {
        if (string.IsNullOrWhiteSpace(content)) return true;
        var text = content.Replace("\r\n", "\n");
        // Filtrar líneas que son rótulos de subcapítulos
        var sb = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            bool isLabel = false;
            foreach (var sc in chapter.SubChapters)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(line, $@"^\s*{System.Text.RegularExpressions.Regex.Escape(sc.Number)}\s+{System.Text.RegularExpressions.Regex.Escape(sc.Title)}\s*$"))
                { isLabel = true; break; }
                if (System.Text.RegularExpressions.Regex.IsMatch(line, $@"^\s*####\s+{System.Text.RegularExpressions.Regex.Escape(sc.Number)}\s+{System.Text.RegularExpressions.Regex.Escape(sc.Title)}\s*$"))
                { isLabel = true; break; }
            }
            if (!isLabel) sb.AppendLine(line);
        }
        var cleaned = sb.ToString().Trim();
        return cleaned.Length < 60; // demasiado corto para ser una sinopsis útil
    }

    private string BuildTocString()
    {
        var sb = new StringBuilder();
        BuildTocStringRecursive(_spec.TableOfContents, sb, "");
        return sb.ToString();
    }

    private void BuildTocStringRecursive(List<ChapterNode> nodes, StringBuilder sb, string indent)
    {
        foreach (var node in nodes)
        {
            sb.AppendLine($"{indent}{node.Number} {node.Title}");
            if (node.SubChapters.Any())
            {
                BuildTocStringRecursive(node.SubChapters, sb, indent + "  ");
            }
        }
    }

    // Quita prefijos numéricos tipo "1 ", "1.2 ", "1.2.3 " del texto del encabezado.
    // En H2 además elimina el prefijo "Capítulo N:" para dejar solo el título puro.
    private static string CleanHeadingText(string text, int level)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Si es H2 y empieza con "Capítulo/Capitulo N:" quitarlo y quedarse con el resto
        if (level == 2)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^\s*Cap[ií]tulo\s+\d+(?:\s*[:\-\.])?\s*(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return m.Groups[1].Value.Trim();
            }
        }

        // Eliminar prefijos numéricos (1, 1.2, 1.2.3, etc.) seguidos de espacios
        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(?:\d+(?:\.\d+)*)\s+", "");
        return cleaned.Trim();
    }

    private async Task GenerateAndSaveDiagramSuggestions()
    {
        _ui.WriteLine("\n[Proceso] Proponiendo diagramas para el manual...", ConsoleColor.Green);
        var sections = new List<(string num, string title, string summary, string content)>();
        void Collect(List<ChapterNode> nodes)
        {
            foreach (var n in nodes)
            {
                sections.Add((n.Number, n.Title, n.Summary ?? string.Empty, n.Content ?? string.Empty));
                if (n.SubChapters.Any()) Collect(n.SubChapters);
            }
        }
        Collect(_spec.TableOfContents);

        // 1) Plan JSON parseable para insertar marcadores
        try
        {
            var jsonPrompt = PromptBuilder.GetDiagramPlanJsonPrompt(
                _spec.Title,
                _spec.Topic,
                _spec.TargetAudience,
                sections.OrderBy(s => s.num, StringComparer.Ordinal)
            );
            // Pedimos JSON explícito (aunque el cliente Chat no fuerza el formato, el prompt lo exige)
            var jsonPlan = await _llm.AskAsync(PromptBuilder.SystemPrompt, jsonPrompt, _config.Model, _config.MaxTokensPerCall, jsonSchema: "{}");
            TryApplyDiagramPlan(jsonPlan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo parsear plan JSON de diagramas; los marcadores no se insertarán");
        }

        // 2) Versión Markdown legible para el archivo de sugerencias
        var prompt = PromptBuilder.GetDiagramSuggestionsPrompt(
            _spec.Title,
            _spec.Topic,
            _spec.TargetAudience,
            sections.OrderBy(s => s.num, StringComparer.Ordinal)
        );
        var suggestions = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall);
        if (string.IsNullOrWhiteSpace(suggestions)) suggestions = "(No se generaron sugerencias de gráficos)";

        // Construir apéndice con fuentes citadas por sección (best-effort)
        var appendix = BuildSourcesAppendix();
        var finalDoc = string.IsNullOrWhiteSpace(appendix) ? suggestions : suggestions + "\n\n" + appendix;
        // Asegurar formato correcto de fences: línea en blanco antes de abrir y después de cerrar
        finalDoc = EnsureFencesSpacing(finalDoc);
        // Normalizar/embellecer Markdown para graficos_sugeridos
        finalDoc = MaybeNormalizeWithMarkdig(finalDoc);
        finalDoc = CleanMarkdownArtifacts(finalDoc);
        if (_config.CustomBeautifyEnabled)
        {
            finalDoc = EnsureSpaceAfterInlineHash(finalDoc);
            finalDoc = FixColonBacktickSpacing(finalDoc);
            finalDoc = BeautifyLists(finalDoc);
        }

        if (!string.IsNullOrEmpty(RunContext.BackRunDirectory))
        {
            var path = System.IO.Path.Combine(RunContext.BackRunDirectory, "graficos_sugeridos.md");
            await System.IO.File.WriteAllTextAsync(path, finalDoc, Encoding.UTF8);
            _logger.LogInformation("Sugerencias de gráficos guardadas en: {Path}", path);
        }
    }

    private void TryApplyDiagramPlan(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("diagrams", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var item in arr.EnumerateArray())
        {
            string section = item.TryGetProperty("section_number", out var sn) ? (sn.GetString() ?? "") : "";
            string name = item.TryGetProperty("name", out var nm) ? (nm.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(name)) continue;
            var (node, _) = FindNode(_spec.TableOfContents, section);
            if (node == null) continue;
            var diag = new DiagramPlanItem
            {
                Name = name,
                Purpose = item.TryGetProperty("purpose", out var pu) ? (pu.GetString() ?? "") : "",
                Format = item.TryGetProperty("format", out var fm) ? (fm.GetString() ?? "") : "",
                Placement = item.TryGetProperty("placement", out var pl) ? (pl.GetString() ?? "end") : "end",
                Code = item.TryGetProperty("code", out var cd) ? (cd.GetString() ?? "") : ""
            };
            node.Diagrams.Add(diag);
        }
    }

    private string BuildSourcesAppendix()
    {
        var sb = new StringBuilder();
        var any = false;
        sb.AppendLine("## Apéndice: Fuentes citadas por sección");
        void Collect(List<ChapterNode> nodes)
        {
            foreach (var n in nodes)
            {
                var urls = ExtractSourcesFromContent(n.Content ?? string.Empty);
                if (urls.Count > 0)
                {
                    any = true;
                    sb.AppendLine($"- {n.Number} {n.Title}");
                    foreach (var u in urls)
                    {
                        sb.AppendLine($"  - {u}");
                    }
                }
                if (n.SubChapters.Any()) Collect(n.SubChapters);
            }
        }
        Collect(_spec.TableOfContents);
        return any ? sb.ToString().TrimEnd() : string.Empty;
    }

    private static List<string> ExtractSourcesFromContent(string content)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(content)) return new List<string>();
        var text = content.Replace("\r\n", "\n");
        // Heurística 1: capturar URLs bajo una sección llamada 'Fuentes'
        var lines = text.Split('\n');
        bool inSources = false; int budget = 0;
        var urlRx = new System.Text.RegularExpressions.Regex(@"https?://[^\s\)\]]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*#{1,6}\s+Fuentes\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*Fuentes\s*:?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                inSources = true; budget = 40; // leer hasta 40 líneas después del encabezado
                continue;
            }
            if (inSources)
            {
                var m = urlRx.Matches(l);
                foreach (System.Text.RegularExpressions.Match mm in m) urls.Add(mm.Value);
                budget--;
                if (budget <= 0 || System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*#{1,6}\s+")) inSources = false;
            }
        }
        // Heurística 2: si no se encontró sección 'Fuentes', capturar URLs globales (máx 5)
        if (urls.Count == 0)
        {
            foreach (System.Text.RegularExpressions.Match m in urlRx.Matches(text))
            {
                urls.Add(m.Value);
                if (urls.Count >= 5) break;
            }
        }
        return urls.ToList();
    }

    private static string EnsureFencesSpacing(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var nl = "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(content.Length + 128);
        bool inFence = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            bool isFence = trimmed.StartsWith("```") || trimmed.StartsWith("~~~");

            if (isFence)
            {
                // Asegurar línea en blanco ANTES de abrir fence
                if (!inFence)
                {
                    // Mirar lo último emitido; si no está en inicio y la última línea no es en blanco, insertar en blanco
                    if (sb.Length > 0)
                    {
                        // Obtener último carácter diferente de \n
                        int len = sb.Length;
                        bool lastIsNewline = len > 0 && sb[len - 1] == '\n';
                        if (!lastIsNewline)
                        {
                            sb.Append(nl);
                        }
                        else
                        {
                            // Ya hay al menos un salto; verificar si hay línea en blanco
                            // Tomar penúltimo salto si existe
                            int lastNl = sb.ToString().LastIndexOf('\n');
                            int prevNl = lastNl > 0 ? sb.ToString().LastIndexOf('\n', lastNl - 1) : -1;
                            if (prevNl >= 0 && lastNl - prevNl <= 1)
                            {
                                // ya hay una línea en blanco
                            }
                        }
                    }
                }
                else
                {
                    // Estamos cerrando un fence (toggle a false más abajo)
                }
                sb.Append(line).Append(nl);
                // Toggle estado fence
                inFence = !inFence;

                // Si acabamos de CERRAR el fence (inFence ahora false), asegurar línea en blanco DESPUÉS
                if (!inFence)
                {
                    // Mirar siguiente línea; si existe y no es en blanco, insertar en blanco
                    if (i + 1 < lines.Length)
                    {
                        var next = lines[i + 1];
                        if (!string.IsNullOrWhiteSpace(next)) sb.Append(nl);
                    }
                    else
                    {
                        // Al final del documento, agregar un salto final
                        sb.Append(nl);
                    }
                }
                continue;
            }

            sb.Append(line).Append(nl);
        }
        return sb.ToString().TrimEnd('\n');
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
            return input;
        }
    }

    private static bool HasPipeTable(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var t = text.Replace("\r\n", "\n");
        var hasPipes = Regex.IsMatch(t, @"^\s*\|.*\|\s*$", RegexOptions.Multiline);
        var hasSep = Regex.IsMatch(t, @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", RegexOptions.Multiline);
        return hasPipes && hasSep;
    }

    private static bool HasGridTable(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var t = text.Replace("\r\n", "\n");
        var hasBorder = Regex.IsMatch(t, @"^\s*\+[-+]+\+\s*$", RegexOptions.Multiline);
        var hasCols = Regex.IsMatch(t, @"^\s*\|.*\|\s*$", RegexOptions.Multiline);
        return hasBorder && hasCols;
    }

    private static string MaybeNormalizeWithMarkdig(string input)
    {
        try
        {
            if (HasPipeTable(input) || HasGridTable(input))
            {
                return input;
            }
        }
        catch { }
        return NormalizeWithMarkdig(input);
    }

    private static string CleanMarkdownArtifacts(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(content.Length);
        var emptyRefRx = new Regex(@"^\s*\[\s*\]\s*:\s*.*$", RegexOptions.Compiled);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (emptyRefRx.IsMatch(line)) continue;
            sb.AppendLine(line);
        }
        var text = sb.ToString();
        text = text.TrimEnd('\n', '\r');
        text += "\n";
        return text.Replace("\n", System.Environment.NewLine);
    }

    private static string FixColonBacktickSpacing(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var text = content.Replace("\r\n", "\n");
        text = Regex.Replace(text, @":\n(?!\n)", ":\n\n", RegexOptions.Multiline);
        return text.Replace("\n", System.Environment.NewLine);
    }

    private static string BeautifyLists(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder(content.Length + 64);
        bool inCode = false;
        string prevNonBlankLine = "";
        bool prevNonBlankIsBullet = false;
        int prevNonBlankBulletIndent = -1;
        bool lastEmittedBlank = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
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
            var m = Regex.Match(line, @"^(\s*)-\s+");
            bool isBullet = m.Success;
            int indent = isBullet ? m.Groups[1].Value.Length : -1;
            if (isBullet)
            {
                if (!string.IsNullOrWhiteSpace(prevNonBlankLine))
                {
                    if (!prevNonBlankIsBullet)
                    {
                        if (!lastEmittedBlank)
                        {
                            result.AppendLine("");
                            lastEmittedBlank = true;
                        }
                    }
                    else
                    {
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
            if (!string.IsNullOrWhiteSpace(line))
            {
                prevNonBlankLine = line;
                prevNonBlankIsBullet = isBullet;
                prevNonBlankBulletIndent = isBullet ? indent : -1;
            }
        }

        return result.ToString().TrimEnd('\n').Replace("\n", System.Environment.NewLine);
    }

    private static string EnsureSpaceAfterInlineHash(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(content.Length + 64);
        bool inCode = false;
        var rxLetterOrDigit = new Regex(@"\b([A-Za-zÁÉÍÓÚÜáéíóúÑñ]+)#([A-Za-zÁÉÍÓÚÜáéíóúÑñ0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var rxHashAtEnd    = new Regex(@"\b([A-Za-zÁÉÍÓÚÜáéíóúÑñ]+)#\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // TODO: Ampliar soporte si se requiere notación musical avanzada.
        var noteRoots = new HashSet<string>(new[]{
            "a","b","c","d","e","f","g",
            "do","re","mi","fa","sol","la","si"
        }, StringComparer.OrdinalIgnoreCase);

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

            line = rxLetterOrDigit.Replace(line, m => {
                var left = m.Groups[1].Value;
                var right = m.Groups[2].Value;
                if (noteRoots.Contains(left)) return m.Value; // no tocar notación musical
                return left + "# " + right;
            });
            line = rxHashAtEnd.Replace(line, m => {
                var left = m.Groups[1].Value;
                return left + "# ";
            });

            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd('\n').Replace("\n", System.Environment.NewLine);
    }
}
