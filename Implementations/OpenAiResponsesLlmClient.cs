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
    private bool _disableCacheControl;

    public OpenAiResponsesLlmClient(IConfiguration config, IUserInteraction ui, ILogger<OpenAiResponsesLlmClient> logger)
    {
        _config = config;
        _ui = ui;
        _logger = logger;
        // Use HttpClientHandler with cookies disabled to avoid CookieContainer initialization issues
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _http = new HttpClient(handler) { Timeout = config.HttpTimeout };
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
            bool isOverview = user.StartsWith("Redacta el contenido del capítulo", StringComparison.OrdinalIgnoreCase);

            // 1) System (cacheable opcional)
            var systemContent = new JsonArray
            {
                BuildInputText(system, cacheable: _config.CacheSystemInput && !_disableCacheControl)
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
                    BuildInputText(RunContext.BookContextStable!, cacheable: !_disableCacheControl)
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
                ["max_output_tokens"] = maxTokens,
                ["temperature"] = isOverview ? 0.3 : 0.7
            };

            if (_config.ResponsesStrictJson && !string.IsNullOrWhiteSpace(jsonSchema))
            {
                var trimmed = jsonSchema.Trim();
                var textObj = new JsonObject();
                if (trimmed == "{{}}" || trimmed == "{}")
                {
                    textObj["format"] = "json_object";
                }
                else
                {
                    try
                    {
                        var schemaElement = JsonNode.Parse(jsonSchema) as JsonNode;
                        if (schemaElement != null)
                        {
                            textObj["format"] = "json_schema";
                            textObj["json_schema"] = new JsonObject
                            {
                                ["name"] = "schema",
                                ["schema"] = schemaElement
                            };
                        }
                        else
                        {
                            textObj["format"] = "json_object";
                        }
                    }
                    catch
                    {
                        textObj["format"] = "json_object";
                    }
                }
                root["text"] = textObj;
            }

            var payload = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            var preview = user.Length > 120 ? user.Substring(0, 120) + "…" : user;
            _logger.LogInformation("Responses request → model={Model}, maxTokens={Max}, userPreview=\"{Preview}\"", model, maxTokens, preview.Replace("\n", " "));

            var resp = await _http.PostAsync("https://api.openai.com/v1/responses", new StringContent(payload, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                // Fallback: si el servidor no acepta cache_control, reintentar sin esos campos
                if ((int)resp.StatusCode == 400 && body.Contains("cache_control", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Responses API rechazó cache_control; reintentando sin caché de input y deshabilitando cache_control para esta sesión");
                    _disableCacheControl = true;
                    var inputNoCache = new JsonArray();
                    inputNoCache.Add(new JsonObject
                    {
                        ["role"] = "system",
                        ["content"] = new JsonArray { BuildInputText(system, cacheable: false) }
                    });
                    if (!string.IsNullOrWhiteSpace(RunContext.BookContextStable))
                    {
                        inputNoCache.Add(new JsonObject
                        {
                            ["role"] = "system",
                            ["content"] = new JsonArray { BuildInputText(RunContext.BookContextStable!, cacheable: false) }
                        });
                    }
                    inputNoCache.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonArray { BuildInputText(user, cacheable: false) }
                    });
                    root["input"] = inputNoCache;
                    var payload2 = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    var resp2 = await _http.PostAsync("https://api.openai.com/v1/responses", new StringContent(payload2, Encoding.UTF8, "application/json"));
                    var body2 = await resp2.Content.ReadAsStringAsync();
                    if (!resp2.IsSuccessStatusCode)
                    {
                        _logger.LogError("Responses API error (retry): {Status} {Body}", resp2.StatusCode, body2);
                        return string.Empty;
                    }
                    body = body2;
                }
                else if ((int)resp.StatusCode == 400 && body.Contains("response_format", StringComparison.OrdinalIgnoreCase))
                {
                    // Si llega aquí por una versión intermedia, rehacer sin root["text"]
                    _logger.LogInformation("Responses API rechazó response_format/text.format; reintentando sin formato JSON estricto");
                    root.Remove("text");
                    var payload3 = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    var resp3 = await _http.PostAsync("https://api.openai.com/v1/responses", new StringContent(payload3, Encoding.UTF8, "application/json"));
                    var body3 = await resp3.Content.ReadAsStringAsync();
                    if (!resp3.IsSuccessStatusCode)
                    {
                        _logger.LogError("Responses API error (retry-2): {Status} {Body}", resp3.StatusCode, body3);
                        return string.Empty;
                    }
                    body = body3;
                }
                else
                {
                    _logger.LogError("Responses API error: {Status} {Body}", resp.StatusCode, body);
                    return string.Empty;
                }
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
