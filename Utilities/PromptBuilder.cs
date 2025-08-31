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

    private const string SummariesForChapterBlockPromptTemplate = @"Para el siguiente capítulo principal y su lista de subcapítulos, genera un resumen para cada uno.

Contexto General:
- Título del Documento: ""{title}""
- Público Objetivo: {audience}
- Resumen del Capítulo Principal Anterior:
{previousSummary}

Bloque a resumir:
- Capítulo Principal: {chapterNumber} — {chapterTitle}
- Subcapítulos de este bloque:
{subchapterTitles}

Devuelve un único objeto JSON. Las claves deben ser los números de sección (ej. ""1"", ""1.1"", ""1.2"") y los valores deben ser sus respectivos resúmenes (entre 150 y 300 palabras cada uno).";

    private const string ChapterPromptTemplate = @"Escribe el contenido de la sección **{chapterNumber} — {chapterTitle}**.

Contexto del Documento:
- Título General: ""{title}""
- Tema Principal: {topic}
- Público Objetivo: {audience}
- Tabla de Contenidos Completa:
{fullToc}
- Resumen de la sección actual:
{summary}
- Resumen del capítulo padre (si aplica):
{parentSummary}

Requisitos:
- Escribe contenido técnico claro y preciso, adaptado al público.
- El objetivo de extensión es de {targetWords} palabras.
- La salida debe ser en formato Markdown.
- Empieza directamente con el contenido, no repitas el título del capítulo.";

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
        string subchapterTitles = string.Join("\n", chapter.SubChapters.Select(sc => $"- {sc.Number} {sc.Title}"));
        return SummariesForChapterBlockPromptTemplate
            .Replace("{title}", title)
            .Replace("{audience}", audience)
            .Replace("{previousSummary}", previousSummary)
            .Replace("{chapterNumber}", chapter.Number)
            .Replace("{chapterTitle}", chapter.Title)
            .Replace("{subchapterTitles}", subchapterTitles);
    }

    public static string GetChapterPrompt(string title, string topic, string audience, string fullToc, ChapterNode node, string parentSummary, int targetWords)
    {
        return ChapterPromptTemplate
            .Replace("{chapterNumber}", node.Number)
            .Replace("{chapterTitle}", node.Title)
            .Replace("{title}", title)
            .Replace("{topic}", topic)
            .Replace("{audience}", audience)
            .Replace("{fullToc}", fullToc)
            .Replace("{summary}", node.Summary)
            .Replace("{parentSummary}", parentSummary)
            .Replace("{targetWords}", targetWords.ToString());
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
}