using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class OpenAiResponsesLlmClient : ILlmClient
{
    private readonly IConfiguration _config;
    private readonly IUserInteraction _ui;
    private readonly ILogger<OpenAiResponsesLlmClient> _logger;
    private readonly HttpClient _http;

    public OpenAiResponsesLlmClient(IConfiguration config, IUserInteraction ui, ILogger<OpenAiResponsesLlmClient> logger)
    {
        _config = config;
        _ui = ui;
        _logger = logger;
        _http = new HttpClient { Timeout = config.HttpTimeout };
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("OPENAI_API_KEY not set");
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> AskAsync(string system, string user, string model, int maxTokens, string? jsonSchema = null)
    {
        _ui.WriteLine($"[OpenAI/Responses] Enviando petición al modelo {model}...", ConsoleColor.DarkGray);
        try
        {
            var inputArray = new JsonArray();

            // 1) System (cacheable opcional)
            var systemContent = new JsonArray
            {
                BuildInputText(system, cacheable: _config.CacheSystemInput)
            };
            inputArray.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemContent
            });

            // 2) Book context por corrida (cacheable opcional)
            if (_config.CacheBookContext && !string.IsNullOrWhiteSpace(RunContext.BookContextStable))
            {
                var ctxContent = new JsonArray
                {
                    BuildInputText(RunContext.BookContextStable!, cacheable: true)
                };
                inputArray.Add(new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = ctxContent
                });
            }

            // 3) User actual (no cacheable)
            var userContent = new JsonArray
            {
                BuildInputText(user, cacheable: false)
            };
            inputArray.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = userContent
            });

            var root = new JsonObject
            {
                ["model"] = model,
                ["input"] = inputArray,
                ["max_output_tokens"] = maxTokens
            };

            if (!string.IsNullOrEmpty(jsonSchema))
            {
                try
                {
                    var schemaElement = JsonNode.Parse(jsonSchema) as JsonNode;
                    if (schemaElement != null)
                    {
                        root["response_format"] = new JsonObject
                        {
                            ["type"] = "json_schema",
                            ["json_schema"] = new JsonObject
                            {
                                ["name"] = "schema",
                                ["schema"] = schemaElement
                            }
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo parsear jsonSchema; enviando sin response_format");
                }
            }

            var payload = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            var preview = user.Length > 120 ? user.Substring(0, 120) + "…" : user;
            _logger.LogInformation("Responses request → model={Model}, maxTokens={Max}, userPreview=\"{Preview}\"", model, maxTokens, preview.Replace("\n", " "));

            var resp = await _http.PostAsync("https://api.openai.com/v1/responses", new StringContent(payload, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Responses API error: {Status} {Body}", resp.StatusCode, body);
                return string.Empty;
            }

            // La estructura de salida de Responses incluye 'output' con items; tomar el primer textual.
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("output", out var outputArr) && outputArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in outputArr.EnumerateArray())
                    {
                        if (item.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var c in contentArr.EnumerateArray())
                            {
                                if (c.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "output_text")
                                {
                                    var text = c.GetProperty("text").GetString() ?? string.Empty;
                                    _logger.LogInformation("Responses response ← len={Len}", text.Length);
                                    return text;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo parsear la respuesta; devolviendo body");
            }
            return body;
        }
        catch (Exception ex)
        {
            _ui.WriteLine($"Ocurrió un error al llamar a Responses API: {ex.Message}", ConsoleColor.Red);
            _logger.LogError(ex, "Responses API call failed");
            return string.Empty;
        }
    }

    private static JsonObject BuildInputText(string text, bool cacheable)
    {
        var obj = new JsonObject
        {
            ["type"] = "input_text",
            ["text"] = text
        };
        if (cacheable)
        {
            obj["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
        }
        return obj;
    }

    public void PrintUsage()
    {
        _ui.WriteLine("[Uso de Tokens — Responses API: no disponible]", ConsoleColor.DarkGray);
    }
}

