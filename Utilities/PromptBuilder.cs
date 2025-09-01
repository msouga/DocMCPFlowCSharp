using System.Linq;
using System.Text;
using System.Text.Json;

public static class PromptBuilder
{
    public const string SystemPrompt = @"Eres un asistente experto en la creación de documentación técnica, manuales y propuestas.
Generas contenido claro, bien estructurado y adaptado al público objetivo.
Reglas clave:
- La salida debe ser siempre en español neutro.
- Mantén coherencia y sigue la estructura de capítulos y subcapítulos solicitada.
- Cuando se pida formato JSON, devuelve únicamente el JSON, sin texto adicional.";

    private const string IndexPromptTemplate = @"Para un documento técnico con el TÍTULO, TEMA y PÚBLICO OBJETIVO dados, propón una estructura jerárquica de capítulos y subcapítulos.

- Título: ""{title}""
- Tema: {topic}
- Público Objetivo: {audience}

Devuelve un array JSON de objetos. Cada objeto debe tener una clave 'title' (string) y opcionalmente una clave 'subchapters' (un array de objetos como este).
Reglas de estructura:
- Un capítulo puede tener entre 0 y 6 subcapítulos.
- No incluyas más de 2 niveles de anidación (capítulos y subcapítulos).";

    private const string IntroductionPromptTemplate = @"Basado en la siguiente estructura de un documento técnico, escribe un solo párrafo de introducción (entre 100 y 200 palabras) que explique qué aprenderá o encontrará el lector en el documento.

- Título: ""{title}""
- Tema: {topic}
- Público Objetivo: {audience}
- Estructura de Capítulos:
{chapterStructure}

Devuelve únicamente el párrafo de introducción, sin encabezados.";

    private const string SummariesForChapterBlockPromptTemplate = @"Para el siguiente capítulo principal y todas sus subsecciones (en cualquier nivel), genera un resumen para CADA elemento listado. Incluye también el capítulo principal.

Contexto General:
- Título del Documento: ""{title}""
- Público Objetivo: {audience}
- Resumen del Capítulo Principal Anterior:
{previousSummary}

Bloque a resumir:
- Capítulo Principal: {chapterNumber} — {chapterTitle}
- Subcapítulos de este bloque:
{subchapterTitles}

Devuelve un único objeto JSON. Las claves deben ser los números de sección (ej. ""1"", ""1.1"", ""1.2"", ""1.2.1"", etc.) y los valores deben ser sus respectivos resúmenes (entre 150 y 300 palabras cada uno). No añadas texto fuera del JSON.";

    private const string ChapterPromptTemplate = @"Escribe el contenido de la sección **{chapterNumber} — {chapterTitle}**.

Contexto del Documento:
- Título General: ""{title}""
- Tema Principal: {topic}
- Público Objetivo: {audience}
- Resumen de la sección actual:
{summary}
- Resumen del capítulo padre (si aplica):
{parentSummary}

Requisitos:
- Escribe contenido técnico claro y preciso, adaptado al público.
{targetWordsText}
- La salida debe ser en formato Markdown.
 - Empieza directamente con el contenido, sin incluir ningún encabezado que repita el número o el título de la sección (p. ej.: ""#### {chapterNumber} — {chapterTitle}"", ""#### {chapterTitle}"").";

    private const string SubchapterContentPromptTemplate = @"Redacta el contenido del subcapítulo **{sectionNumber} — {sectionTitle}** en español y formato Markdown.

Contexto:
- Título del Documento: ""{title}""
- Tema: {topic}
- Público: {audience}
- Resumen del capítulo anterior:
{prevMainSummary}
- Resumen del capítulo actual:
{currentMainSummary}
- Resúmenes de los subcapítulos de este capítulo:
{subchapterSummaries}
- Contenido del subcapítulo anterior (para continuidad):
{previousSiblingContent}

Requisitos:
{targetWordsText}
- Estructura clara. No incluyas el encabezado principal del subcapítulo (se añadirá externamente).
- Para subsecciones internas usa como máximo encabezados de cuarto nivel (####). No uses niveles 5 o 6.
 - No repitas ni incluyas encabezados con el número o el título del subcapítulo (ej.: ""#### {sectionNumber} — {sectionTitle}"", ""#### {sectionTitle}""). Comienza directamente con el contenido.
- Mantén coherencia y continuidad con el contenido previo si existe.";

    private const string ChapterOverviewPromptTemplate = @"Redacta el contenido del capítulo **{chapterNumber} — {chapterTitle}** en español y formato Markdown.

Contexto del Documento:
- Título General: ""{title}""
- Tema Principal: {topic}
- Público Objetivo: {audience}

Pistas de contenido:
- Resumen del capítulo (si existe):
{chapterSummary}
- Resúmenes de sus subcapítulos:
{subchapterSummaries}
- Resumen del capítulo anterior:
{previousChapterSummary}

Requisitos:
- Construye un texto fluido que introduzca y conecte los subtemas del capítulo.
{targetWordsText}
- La salida debe ser en formato Markdown.
- Empieza directamente con el contenido, no repitas el título del capítulo.
- No listes ni repitas títulos o numeraciones de subcapítulos (evita líneas como ""1.1 ..."", ""1.2 ..."").
- No incluyas encabezados internos, listas, viñetas, bloques de código ni comandos.
- Redacta 2 a 3 párrafos generales (120-200 palabras en total), de carácter conceptual y sin instrucciones paso a paso.

    Ejemplo de estilo (no copiar literalmente): redactar 2 párrafos que enmarquen el capítulo a alto nivel, explicando cómo se relacionan los subtemas y qué decisiones o criterios importan, sin listas ni encabezados internos.";

    public static string GetIndexPrompt(string title, string topic, string audience) 
    {
        return IndexPromptTemplate
            .Replace("{title}", title)
            .Replace("{topic}", topic)
            .Replace("{audience}", audience);
    }

    public static string GetIntroductionPrompt(string title, string topic, string audience, string chapterStructure) 
    {
        return IntroductionPromptTemplate
            .Replace("{title}", title)
            .Replace("{topic}", topic)
            .Replace("{audience}", audience)
            .Replace("{chapterStructure}", chapterStructure);
    }

    public static string GetSummariesForChapterBlockPrompt(string title, string audience, ChapterNode chapter, string previousSummary) 
    {
        // Construye una lista plana con todas las subsecciones (cualquier nivel)
        static void CollectDescendants(ChapterNode node, List<(string num, string title)> acc)
        {
            foreach (var child in node.SubChapters)
            {
                acc.Add((child.Number ?? string.Empty, child.Title ?? string.Empty));
                if (child.SubChapters.Any()) CollectDescendants(child, acc);
            }
        }
        var all = new List<(string num, string title)>();
        CollectDescendants(chapter, all);
        var subchapterTitles = all.Count == 0
            ? "(no hay subsecciones)"
            : string.Join("\n", all.Select(sc => $"- {sc.num} {sc.title}"));

        return SummariesForChapterBlockPromptTemplate
            .Replace("{title}", title)
            .Replace("{audience}", audience)
            .Replace("{previousSummary}", string.IsNullOrWhiteSpace(previousSummary) ? "(ninguno)" : previousSummary)
            .Replace("{chapterNumber}", chapter.Number)
            .Replace("{chapterTitle}", chapter.Title)
            .Replace("{subchapterTitles}", subchapterTitles);
    }

    public static string GetChapterPrompt(string title, string topic, string audience, string fullToc, ChapterNode node, string parentSummary, int targetWords)
    {
        var targetWordsText = targetWords > 0 ? $"- El objetivo de extensión es de {targetWords} palabras." : "";
        return ChapterPromptTemplate
            .Replace("{chapterNumber}", node.Number)
            .Replace("{chapterTitle}", node.Title)
            .Replace("{title}", title)
            .Replace("{topic}", topic)
            .Replace("{audience}", audience)
            .Replace("{summary}", node.Summary)
            .Replace("{parentSummary}", parentSummary)
            .Replace("{targetWordsText}", targetWordsText);
    }

    public static string GetSubchapterContentPrompt(
        string title,
        string topic,
        string audience,
        ChapterNode subchapter,
        ChapterNode parentChapter,
        string previousMainSummary,
        int targetWords)
    {
        var subSummaries = new StringBuilder();
        foreach (var sc in parentChapter.SubChapters)
        {
            var sum = string.IsNullOrWhiteSpace(sc.Summary) ? "(sin resumen)" : sc.Summary;
            subSummaries.AppendLine($"- {sc.Number} {sc.Title}: {sum}");
        }

        var prevSiblingContent = "";
        var idx = parentChapter.SubChapters.FindIndex(s => s.Number == subchapter.Number);
        if (idx > 0)
        {
            prevSiblingContent = parentChapter.SubChapters[idx - 1].Content ?? string.Empty;
        }

        var targetWordsText = targetWords > 0 ? $"- Extensión objetivo: {targetWords} palabras." : "";
        return SubchapterContentPromptTemplate
            .Replace("{sectionNumber}", subchapter.Number)
            .Replace("{sectionTitle}", subchapter.Title)
            .Replace("{title}", title)
            .Replace("{topic}", topic)
            .Replace("{audience}", audience)
            .Replace("{prevMainSummary}", string.IsNullOrWhiteSpace(previousMainSummary) ? "(ninguno)" : previousMainSummary)
            .Replace("{currentMainSummary}", string.IsNullOrWhiteSpace(parentChapter.Summary) ? "(ninguno)" : parentChapter.Summary)
            .Replace("{subchapterSummaries}", subSummaries.ToString())
            .Replace("{previousSiblingContent}", string.IsNullOrWhiteSpace(prevSiblingContent) ? "(sin contenido previo)" : prevSiblingContent)
            .Replace("{targetWordsText}", targetWordsText);
    }

    public static string GetChapterOverviewPrompt(
        string title,
        string topic,
        string audience,
        ChapterNode chapter,
        string previousChapterSummary,
        int targetWords)
    {
        var subSummaries = new StringBuilder();
        foreach (var sc in chapter.SubChapters)
        {
            var sum = string.IsNullOrWhiteSpace(sc.Summary) ? "(sin resumen)" : sc.Summary;
            subSummaries.AppendLine($"- {sc.Number} {sc.Title}: {sum}");
        }
        var targetWordsText = targetWords > 0 ? $"- Extensión objetivo: {targetWords} palabras." : "";
        return ChapterOverviewPromptTemplate
            .Replace("{chapterNumber}", chapter.Number)
            .Replace("{chapterTitle}", chapter.Title)
            .Replace("{title}", title)
            .Replace("{topic}", topic)
            .Replace("{audience}", audience)
            .Replace("{chapterSummary}", string.IsNullOrWhiteSpace(chapter.Summary) ? "(sin resumen)" : chapter.Summary)
            .Replace("{subchapterSummaries}", subSummaries.ToString())
            .Replace("{previousChapterSummary}", string.IsNullOrWhiteSpace(previousChapterSummary) ? "(ninguno)" : previousChapterSummary)
            .Replace("{targetWordsText}", targetWordsText);
    }

    public static object BuildPayload(string system, string user, string model, int maxTokens, string? jsonSchema = null)
    {
        var payload = new Dictionary<string, object?>
        {
            { "model", model },
            { "messages", new object[] { new { role = "system", content = system }, new { role = "user", content = user } } },
            { "max_completion_tokens", maxTokens },
            { "stream", false },
        };

        if (!string.IsNullOrEmpty(jsonSchema))
        {
            payload["response_format"] = new { type = "json_schema", json_schema = new { name = "schema", schema = JsonDocument.Parse(jsonSchema).RootElement } };
        }

        return payload.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    // Sugerencias de diagramas: prioriza PlantUML; si no aplica, usa Mermaid. Usa contenido real para decidir.
    private const string DiagramSuggestionsPromptTemplate = @"Eres un asistente que propone gráficos técnicos para un manual.
Genera una lista de diagramas útiles para ilustrar conceptos clave del documento.

Instrucciones:
- Prioriza formatos con código: PlantUML preferido; si no aplica, usa Mermaid. Si ninguno aplica, describe textualmente.
- Revisa el contenido real de cada sección (extractos incluidos). Sugiere 0–3 diagramas SOLO cuando aporten claridad; no propongas nada si no agrega valor.
- Indica: Sección (número y título), Nombre del diagrama, Objetivo, Ubicación sugerida (antes/después de qué párrafo o al final de la sección), Formato (plantuml/mermaid/texto), y el bloque de código (si aplica).
- Evita repetir o reescribir el contenido; enfócate en qué graficar y cómo.

Contexto del documento:
- Título: ""{title}""
- Tema: {topic}
- Público: {audience}
- Secciones (con extractos):
{sectionSummaries}

Entrega en Markdown. Ordena por número de sección. Usa bloques de código etiquetados (```plantuml o ```mermaid) cuando proporciones código.";

    public static string GetDiagramSuggestionsPrompt(string title, string topic, string audience, IEnumerable<(string num, string title, string summary, string content)> sections)
    {
        var sb = new StringBuilder();
        foreach (var (num, t, sum, content) in sections)
        {
            var s = string.IsNullOrWhiteSpace(sum) ? "(sin resumen)" : sum.Trim();
            var c = string.IsNullOrWhiteSpace(content) ? "(sin contenido)" : content.Trim();
            // Limitar extractos por sección para no exceder tokens
            if (s.Length > 300) s = s.Substring(0, 300) + "…";
            if (c.Length > 900) c = c.Substring(0, 900) + "…";
            sb.AppendLine($"- {num} {t}\n  Resumen: {s}\n  Contenido (extracto): {c}");
        }

        return DiagramSuggestionsPromptTemplate
            .Replace("{title}", title)
            .Replace("{topic}", topic)
            .Replace("{audience}", audience)
            .Replace("{sectionSummaries}", sb.ToString());
    }
}
