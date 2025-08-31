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

        // Pass settings directly in the request object
        var chatRequest = new ChatRequest(messages, model: model, maxTokens: maxTokens);

        try
        {
            ChatCompletion completion = await _client.CompleteChatAsync(chatRequest);
            return completion.Content[0].Text;
        }
        catch (Exception ex)
        {
            _ui.WriteLine($"Ocurrió un error al llamar a la API de OpenAI: {ex.Message}", ConsoleColor.Red);
            return string.Empty;
        }
    }

    public void PrintUsage()
    {
        _ui.WriteLine("[Uso de Tokens no disponible con la nueva librería]", ConsoleColor.DarkGray);
    }
}
