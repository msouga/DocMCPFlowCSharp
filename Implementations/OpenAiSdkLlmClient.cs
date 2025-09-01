using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenAI.Chat;
using Microsoft.Extensions.Logging;

public class OpenAiSdkLlmClient : ILlmClient
{
    private readonly IConfiguration _config;
    private readonly IUserInteraction _ui;
    private readonly ChatClient _client;
    private readonly ILogger<OpenAiSdkLlmClient> _logger;

    public OpenAiSdkLlmClient(IConfiguration config, IUserInteraction ui, ILogger<OpenAiSdkLlmClient> logger)
    {
        _config = config;
        _ui = ui;
        _logger = logger;
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY environment variable not set.");
        }
        _client = new ChatClient(config.Model, apiKey);

        if (_config.EnableWebSearch)
        {
            _ui.WriteLine("[Aviso] ENABLE_WEB_SEARCH está activo pero el cliente actual es Chat (sin herramientas). Activa USE_RESPONSES_API=true para habilitar búsquedas web.", ConsoleColor.Yellow);
            try { _logger.LogWarning("ENABLE_WEB_SEARCH ignorado con ChatClient. UseResponsesApi=false"); } catch { }
        }
    }

    public async Task<string> AskAsync(string system, string user, string model, int maxTokens, string? jsonSchema = null)
    {
        _ui.WriteLine($"[OpenAI] Enviando petición al modelo {model}...", ConsoleColor.DarkGray);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(system),
            new UserChatMessage(user)
        };

        try
        {
            var preview = user.Length > 120 ? user.Substring(0, 120) + "…" : user;
            _logger.LogInformation("OpenAI request → model={model}, maxTokens={maxTokens}, userPreview=\"{preview}\"", model, maxTokens, preview.Replace("\n", " "));
        }
        catch { /* best-effort logging */ }

        try
        {
            // Intentar configurar límite de tokens si la versión del SDK lo soporta
            // Algunas versiones exponen un objeto de opciones para límites
            ChatCompletion completion;
            try
            {
                var options = new ChatCompletionOptions();
                // Asignación defensiva: algunas versiones usan MaxOutputTokens y otras MaxTokens
                // Usamos reflexión ligera para establecer la propiedad disponible.
                var prop = options.GetType().GetProperty("MaxOutputTokens") ?? options.GetType().GetProperty("MaxTokens");
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(options, maxTokens);
                }
                // Bajar la temperatura para overviews de capítulo (best-effort via reflexión)
                if (user.StartsWith("Redacta el contenido del capítulo", StringComparison.OrdinalIgnoreCase))
                {
                    var tempProp = options.GetType().GetProperty("Temperature");
                    if (tempProp != null && tempProp.CanWrite)
                    {
                        tempProp.SetValue(options, 0.3);
                    }
                }
                completion = await _client.CompleteChatAsync(messages, options);
            }
            catch
            {
                // Fallback si el tipo de opciones no existe en esta versión
                completion = await _client.CompleteChatAsync(messages);
            }
            try { _logger.LogInformation("OpenAI response ← len={Len}", completion?.Content?[0]?.Text?.Length ?? 0); } catch { }
            if (completion?.Content is null || completion.Content.Count == 0)
            {
                return string.Empty;
            }
            var first = completion.Content[0]?.Text;
            return first ?? string.Empty;
        }
        catch (Exception ex)
        {
            _ui.WriteLine($"Ocurrió un error al llamar a la API de OpenAI: {ex.Message}", ConsoleColor.Red);
            try { _logger.LogError(ex, "OpenAI error: {Message}", ex.Message); } catch { }
            return string.Empty;
        }
    }

    public void PrintUsage()
    {
        _ui.WriteLine("[Uso de Tokens no disponible con la nueva librería]", ConsoleColor.DarkGray);
    }
}
