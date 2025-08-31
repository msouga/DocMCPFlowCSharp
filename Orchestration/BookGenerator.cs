using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class BookGenerator : IBookFlowOrchestrator
{
    private readonly IConfiguration _config;
    private readonly IUserInteraction _ui;
    private readonly ILlmClient _llm;
    private readonly IManuscriptWriter _writer;
    private readonly BookSpecification _spec = new();

    public BookGenerator(IConfiguration config, IUserInteraction ui, ILlmClient llm, IManuscriptWriter writer)
    {
        _config = config;
        _ui = ui;
        _llm = llm;
        _writer = writer;
    }

    public async Task RunAsync()
    {
        CollectInitialInputs();
        await GenerateTableOfContents();
        DisplayAndEditTableOfContents();
        NumberNodes(_spec.TableOfContents, "");

        await GenerateIntroduction();

        _ui.WriteLine("\n[Proceso] Generando resúmenes para la estructura final...", ConsoleColor.Green);
        await GenerateSummaries();
        DisplaySummaries();
        await _writer.SaveAsync(_spec);

        if (_config.IsDryRun)
        {
            _ui.WriteLine("\n[DRY_RUN] Finalizando sin generar contenido. Revisa la estructura y los resúmenes.", ConsoleColor.Yellow);
            return;
        }

        _ui.WriteLine("\n[Proceso] Generando contenido completo del documento...", ConsoleColor.Green);
        await GenerateContent(_spec.TableOfContents, null);
        await _writer.SaveAsync(_spec, final: true);
        
        _ui.WriteLine("\nListo. Archivo generado: manuscrito.md\n", ConsoleColor.Green);
    }

    private void CollectInitialInputs()
    {
        _spec.Title = _ui.ReadLine("Título del documento: ").Trim();
        while (string.IsNullOrWhiteSpace(_spec.Title))
            _spec.Title = _ui.ReadLine("Por favor, ingresa un título: ").Trim();

        _spec.TargetAudience = _ui.ReadLine("Público Objetivo (ej. Principiante, Experto): ").Trim();
        while (string.IsNullOrWhiteSpace(_spec.TargetAudience))
            _spec.TargetAudience = _ui.ReadLine("Por favor, ingresa el público objetivo: ").Trim();

        _spec.Topic = _ui.ReadLine("Tema (ej. Azure Functions, Patrones de Diseño): ").Trim();
        while (string.IsNullOrWhiteSpace(_spec.Topic))
            _spec.Topic = _ui.ReadLine("Por favor, ingresa un tema: ").Trim();
    }

    private async Task GenerateTableOfContents()
    {
        if (_config.DemoMode)
        {
            _ui.WriteLine("\n[Demo] Modo demo activo: usando índice mínimo (2 capítulos × 2 subcapítulos).", ConsoleColor.Yellow);
            _spec.TableOfContents = BuildDemoToc();
            return;
        }

        _ui.WriteLine("\n[Proceso] Generando propuesta de estructura de capítulos...", ConsoleColor.Green);
        var prompt = PromptBuilder.GetIndexPrompt(_spec.Title, _spec.Topic, _spec.TargetAudience);

        var jsonResponse = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall);

        try
        {
            _spec.TableOfContents = ParseToc(jsonResponse);
        }
        catch (JsonException ex)
        {
            _ui.WriteLine($"Error al procesar la estructura JSON: {ex.Message}", ConsoleColor.Red);
            _ui.WriteLine("No se pudo generar una estructura. Saliendo.", ConsoleColor.Red);
            Environment.Exit(1);
        }
    }

    private List<ChapterNode> BuildDemoToc()
    {
        return new List<ChapterNode>
        {
            new ChapterNode
            {
                Title = "Capítulo 1: Conceptos básicos",
                SubChapters =
                {
                    new ChapterNode{ Title = "1.1 Introducción" },
                    new ChapterNode{ Title = "1.2 Primeros pasos" }
                }
            },
            new ChapterNode
            {
                Title = "Capítulo 2: Aplicación práctica",
                SubChapters =
                {
                    new ChapterNode{ Title = "2.1 Ejemplo guiado" },
                    new ChapterNode{ Title = "2.2 Buenas prácticas" }
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
            NumberNodes(_spec.TableOfContents, "");
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

    private void NumberNodes(List<ChapterNode> nodes, string prefix)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var currentNumber = $"{prefix}{i + 1}";
            node.Number = currentNumber;
            if (node.SubChapters.Any())
            {
                NumberNodes(node.SubChapters, currentNumber + ".");
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
            
            var summariesJson = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall, jsonSchema: "{{}}"); // Request JSON output

            try
            {
                using var doc = JsonDocument.Parse(summariesJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var (node, _) = FindNode(_spec.TableOfContents, prop.Name);
                    if (node != null)
                    {
                        node.Summary = prop.Value.GetString() ?? "";
                    }
                }
            }
            catch (JsonException ex)
            {
                _ui.WriteLine($"Error al procesar resúmenes JSON para el capítulo {chapter.Number}: {ex.Message}.", ConsoleColor.Yellow);
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
            var prompt = PromptBuilder.GetChapterPrompt(_spec.Title, _spec.Topic, _spec.TargetAudience, fullToc, node, parentSummary, _config.TargetWordsPerChapter);
            node.Content = await _llm.AskAsync(PromptBuilder.SystemPrompt, prompt, _config.Model, _config.MaxTokensPerCall);
            await _writer.SaveAsync(_spec);

            if (node.SubChapters.Any())
            {
                await GenerateContent(node.SubChapters, node);
            }
        }
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
}
