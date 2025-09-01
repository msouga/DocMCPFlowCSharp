using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        _logger.LogInformation("Entradas iniciales — Título: '{Title}', Público: '{Audience}', Tema: '{Topic}'", _spec.Title, _spec.TargetAudience, _spec.Topic);
        await GenerateTableOfContents();
        if (!_tocLoadedFromFile)
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
                foreach (var raw in System.IO.File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("# "))
                    {
                        _spec.Title = line.Substring(2).Trim();
                        _logger.LogInformation("Título precargado desde INDEX_MD_PATH: {Title}", _spec.Title);
                        break;
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
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.*\S)\s*$");
                if (!m.Success) continue;
                var level = m.Groups[1].Value.Length; // 1..6
                var text = m.Groups[2].Value.Trim();
                // Limpiar prefijos numéricos del texto del encabezado para evitar duplicación
                // p.ej. "1.1 Introducción" -> "Introducción". En H2 solemos permitir "Capítulo N: ...".
                text = CleanHeadingText(text, level);
                if (level == 1)
                {
                    if (string.IsNullOrWhiteSpace(_spec.Title)) _spec.Title = text;
                    continue; // H1 es título global
                }

                var node = new ChapterNode { Title = text };
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
                else if (level >= 5)
                {
                    if (stack.TryGetValue(4, out var parent)) parent.SubChapters.Add(node);
                    stack[5] = node;
                }
            }
            if (toc.Count > 0)
            {
                _spec.TableOfContents = toc;
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
            _ui.WriteLine($"{indent}{node.Number} {node.Title}");
            if (node.SubChapters.Any())
            {
                DisplayToc(node.SubChapters, indent + "  ");
            }
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
                        node.Summary = prop.Value.GetString() ?? "";
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
                    var strictPrompt = overviewPrompt + "\n\nInstrucción final: escribe 3 párrafos (80-120 palabras cada uno), sin listas, sin encabezados ni numeraciones de secciones.";
                    _logger.LogInformation("Overview débil para nodo {Number}; reintentando con prompt estricto", node.Number);
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
        var sections = new List<(string num, string title, string summary)>();
        void Collect(List<ChapterNode> nodes)
        {
            foreach (var n in nodes)
            {
                sections.Add((n.Number, n.Title, n.Summary ?? string.Empty));
                if (n.SubChapters.Any()) Collect(n.SubChapters);
            }
        }
        Collect(_spec.TableOfContents);

        var prompt = PromptBuilder.GetDiagramSuggestionsPrompt(
            _spec.Title,
            _spec.Topic,
            _spec.TargetAudience,
            sections.OrderBy(s => s.num, StringComparer.Ordinal)
        );
        var suggestions = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall);
        if (string.IsNullOrWhiteSpace(suggestions)) suggestions = "(No se generaron sugerencias de gráficos)";

        if (!string.IsNullOrEmpty(RunContext.BackRunDirectory))
        {
            var path = System.IO.Path.Combine(RunContext.BackRunDirectory, "graficos_sugeridos.md");
            await System.IO.File.WriteAllTextAsync(path, suggestions, Encoding.UTF8);
            _logger.LogInformation("Sugerencias de gráficos guardadas en: {Path}", path);
        }
    }
}
