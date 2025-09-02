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
        var betaHeader = Environment.GetEnvironmentVariable("OPENAI_BETA_HEADER");
        if (!string.IsNullOrWhiteSpace(betaHeader))
        {
            try { _http.DefaultRequestHeaders.Add("OpenAI-Beta", betaHeader!); } catch { }
        }
        else if (config.EnableWebSearch)
        {
            // Aviso amistoso: algunos despliegues requieren este header para tools
            _ui.WriteLine("[Aviso] ENABLE_WEB_SEARCH=true sin OPENAI_BETA_HEADER; si ves errores de herramientas, define OPENAI_BETA_HEADER.", ConsoleColor.DarkYellow);
        }
    }

    public async Task<string> AskAsync(string system, string user, string model, int maxTokens, string? jsonSchema = null)
    {
        _ui.WriteLine($"[OpenAI/Responses] Enviando petición al modelo {model}...", ConsoleColor.DarkGray);
        try
        {
            var inputArray = new JsonArray();
            bool isOverview = user.StartsWith("Redacta el contenido del capítulo", StringComparison.OrdinalIgnoreCase);
            bool enableWeb = _config.EnableWebSearch;
            if (enableWeb && !isOverview)
            {
                // Incluir instrucción para citar fuentes cuando proceda
                user += "\n\nSi falta contexto, realiza búsquedas web cuando sea útil y cita 3–5 fuentes relevantes con URL en una sección 'Fuentes' al final.";
            }

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
                ["max_output_tokens"] = maxTokens
            };

            // Si está habilitada la búsqueda web, añadimos la herramienta oficial
            if (enableWeb)
            {
                var tools = new JsonArray
                {
                    new JsonObject { ["type"] = "web_search" }
                };
                root["tools"] = tools;
                root["tool_choice"] = "auto";
            }

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
            _logger.LogInformation("Responses request → model={Model}, maxTokens={Max}", model, maxTokens);
            _logger.LogInformation("Responses SYSTEM prompt:\n{System}", system);
            _logger.LogInformation("Responses USER prompt:\n{User}", user);

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
                else if ((int)resp.StatusCode == 400 && body.Contains("temperature", StringComparison.OrdinalIgnoreCase))
                {
                    // Robustez por si alguna variante incluye 'temperature'
                    _logger.LogInformation("Responses API rechazó 'temperature'; reintentando sin ese parámetro");
                    try { root.Remove("temperature"); } catch { }
                    var payload4 = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    var resp4 = await _http.PostAsync("https://api.openai.com/v1/responses", new StringContent(payload4, Encoding.UTF8, "application/json"));
                    var body4 = await resp4.Content.ReadAsStringAsync();
                    if (!resp4.IsSuccessStatusCode)
                    {
                        _logger.LogError("Responses API error (retry-3): {Status} {Body}", resp4.StatusCode, body4);
                        return string.Empty;
                    }
                    body = body4;
                }
                else
                {
                    _logger.LogError("Responses API error: {Status} {Body}", resp.StatusCode, body);
                    return string.Empty;
                }
            }

            // La estructura de salida de Responses incluye 'output' con items; tomar el primer textual.
            // Antes, intentar registrar señales de uso de herramientas (web_search, etc.).
            try { LogToolSignals(body); } catch { /* best-effort */ }
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
                                    _logger.LogInformation("Responses response (full text) ←\n{Text}", text);
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
            _logger.LogInformation("Responses response (raw body) ←\n{Body}", body);
            return body;
        }
        catch (Exception ex)
        {
            _ui.WriteLine($"Ocurrió un error al llamar a Responses API: {ex.Message}", ConsoleColor.Red);
            _logger.LogError(ex, "Responses API call failed");
            return string.Empty;
        }
    }

    private void LogToolSignals(string body)
    {
        using var doc = JsonDocument.Parse(body);
        int anyTool = 0, webCalls = 0;
        void Scan(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    string? name = null; string? type = null;
                    bool toolish = false;
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (prop.NameEquals("name")) name = prop.Value.GetString();
                        else if (prop.NameEquals("type")) type = prop.Value.GetString();
                        else if (prop.NameEquals("tool") || prop.NameEquals("tool_use") || prop.NameEquals("tool_call") || prop.NameEquals("tool_result")) toolish = true;
                    }
                    if (!string.IsNullOrEmpty(name) && (toolish || type == "tool_use" || type == "tool_call" || type == "tool_result"))
                    {
                        anyTool++;
                        if (string.Equals(name, "web_search", StringComparison.OrdinalIgnoreCase)) webCalls++;
                    }
                    foreach (var prop in el.EnumerateObject()) Scan(prop.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray()) Scan(item);
                    break;
            }
        }
        Scan(doc.RootElement);
        if (anyTool > 0)
        {
            _logger.LogInformation("Responses tools used: total={Any}, web_search={Web}", anyTool, webCalls);
        }
        else if (body.IndexOf("web_search", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _logger.LogInformation("Responses body references 'web_search' (no structured tool signals parsed)");
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
