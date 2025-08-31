using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenAI.Chat;

public class OpenAiSdkLlmClient : ILlmClient
{
    private readonly IConfiguration _config;
    private readonly IUserInteraction _ui;
    private readonly ChatClient _client;

    public OpenAiSdkLlmClient(IConfiguration config, IUserInteraction ui)
    {
        _config = config;
        _ui = ui;
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY environment variable not set.");
        }
        _client = new ChatClient(config.Model, apiKey);
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
            Logger.Append($"OpenAI request → model={model}, maxTokens={maxTokens}, userPreview=\"{preview.Replace("\n", " ")}\"");
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
                completion = await _client.CompleteChatAsync(messages, options);
            }
            catch
            {
                // Fallback si el tipo de opciones no existe en esta versión
                completion = await _client.CompleteChatAsync(messages);
            }
            try { Logger.Append($"OpenAI response ← len={completion.Content?[0]?.Text?.Length ?? 0}"); } catch { }
            return completion.Content[0].Text;
        }
        catch (Exception ex)
        {
            _ui.WriteLine($"Ocurrió un error al llamar a la API de OpenAI: {ex.Message}", ConsoleColor.Red);
            try { Logger.Append($"OpenAI error: {ex.Message}"); } catch { }
            return string.Empty;
        }
    }

    public void PrintUsage()
    {
        _ui.WriteLine("[Uso de Tokens no disponible con la nueva librería]", ConsoleColor.DarkGray);
    }
}
