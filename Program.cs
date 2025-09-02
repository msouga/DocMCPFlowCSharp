using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

internal class Program
{
    private static async Task Main(string[]? args)
    {
        TryLoadExecutionParameters(args);
        var config = new EnvironmentConfiguration();
        var ui = new ConsoleUserInteraction();

        if (string.IsNullOrWhiteSpace(config.OpenApiKey))
        {
            ui.WriteLine("[Aviso] Define OPENAI_API_KEY antes de ejecutar.", ConsoleColor.Yellow);
            return;
        }

        var runId = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        var runDir = System.IO.Path.Combine("back", runId);
        var backLogPath = System.IO.Path.Combine(runDir, $"{runId}-log.txt");
        var rootLogPath = System.IO.Path.Combine(Environment.CurrentDirectory, "ultimo-log.txt");
        try
        {
            System.IO.Directory.CreateDirectory(runDir);
            RunContext.BackRunDirectory = runDir;
            RunContext.RootLogPath = rootLogPath;
            // Higiene: eliminar manuscritos previos en raíz si existen
            var rootManuscript = System.IO.Path.Combine(Environment.CurrentDirectory, "manuscrito.md");
            var rootChapters = System.IO.Path.Combine(Environment.CurrentDirectory, "manuscrito_capitulos.md");
            if (System.IO.File.Exists(rootManuscript)) System.IO.File.Delete(rootManuscript);
            if (System.IO.File.Exists(rootChapters)) System.IO.File.Delete(rootChapters);
            // Construir ILogger (consola + archivo back + archivo root)
            var minLevel = config.DebugLogging ? LogLevel.Debug : LogLevel.Warning;
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(minLevel);
                builder.AddConsole();
                builder.AddProvider(new FileLoggerProvider(backLogPath, rootLogPath, minLevel));
            });
            var log = loggerFactory.CreateLogger<Program>();
            log.LogInformation($"Inicio de sesión — {DateTime.Now:O}");
            PrintResolvedConfiguration(config, log, ui);

            ILlmClient llmClient = config.UseResponsesApi
                ? new OpenAiResponsesLlmClient(config, ui, loggerFactory.CreateLogger<OpenAiResponsesLlmClient>())
                : new OpenAiSdkLlmClient(config, ui, loggerFactory.CreateLogger<OpenAiSdkLlmClient>());
            var manuscriptWriter = new MarkdownManuscriptWriter(loggerFactory.CreateLogger<MarkdownManuscriptWriter>(), config);
            
            var orchestrator = new BookGenerator(config, ui, llmClient, manuscriptWriter, loggerFactory.CreateLogger<BookGenerator>());

            var flowSw = Stopwatch.StartNew();
            try
            {
                log.LogInformation("Arrancando orquestador de flujo (BookGenerator.RunAsync)");
                await orchestrator.RunAsync();
                log.LogInformation("Ejecución de orquestador completada");
            }
            catch (Exception ex)
            {
                ui.WriteLine($"\n[ERROR FATAL] {ex.Message}", ConsoleColor.Red);
                ui.WriteLine(ex.ToString(), ConsoleColor.DarkRed);
                log.LogError(ex, "ERROR FATAL en ejecución");
            }
            finally
            {
                flowSw.Stop();
                llmClient.PrintUsage();
                ui.WriteLine($"Tiempo total: {flowSw.Elapsed}", ConsoleColor.Magenta);
                log.LogInformation($"Tiempo total: {flowSw.Elapsed}");
            }
        }
        catch (Exception ex)
        {
            // Fallback: si falla la inicialización del logger/archivos, seguimos con consola solamente
            ui.WriteLine("[Aviso] No se pudo inicializar el logging en archivos. Continuando solo con consola.", ConsoleColor.Yellow);
            ui.WriteLine(ex.Message, ConsoleColor.DarkYellow);

            var minLevel = config.DebugLogging ? LogLevel.Debug : LogLevel.Warning;
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(minLevel);
                builder.AddConsole();
            });
            var log = loggerFactory.CreateLogger<Program>();
            var llmClient = new OpenAiSdkLlmClient(config, ui, loggerFactory.CreateLogger<OpenAiSdkLlmClient>());
            var manuscriptWriter = new MarkdownManuscriptWriter(loggerFactory.CreateLogger<MarkdownManuscriptWriter>(), config);
            var orchestrator = new BookGenerator(config, ui, llmClient, manuscriptWriter, loggerFactory.CreateLogger<BookGenerator>());

            var flowSw = Stopwatch.StartNew();
            try
            {
                log.LogInformation("Arrancando orquestador de flujo (BookGenerator.RunAsync) [fallback]");
                await orchestrator.RunAsync();
                log.LogInformation("Ejecución de orquestador completada [fallback]");
            }
            catch (Exception ex2)
            {
                ui.WriteLine($"\n[ERROR FATAL] {ex2.Message}", ConsoleColor.Red);
                ui.WriteLine(ex2.ToString(), ConsoleColor.DarkRed);
                log.LogError(ex2, "ERROR FATAL en ejecución [fallback]");
            }
            finally
            {
                flowSw.Stop();
                llmClient.PrintUsage();
                ui.WriteLine($"Tiempo total: {flowSw.Elapsed}", ConsoleColor.Magenta);
                log.LogInformation($"Tiempo total: {flowSw.Elapsed}");
            }
        }
    }

    private static void PrintResolvedConfiguration(IConfiguration config, ILogger log, IUserInteraction ui)
    {
        string mark(string envName, string? overrideValue = null)
        {
            var isSet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envName));
            return isSet ? (overrideValue ?? string.Empty) : $"{overrideValue ?? string.Empty} (Default)";
        }

        string valBool(bool v) => v ? "true" : "false";

        // Sensibles: no mostrar el valor de la API key
        var apiKeySet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        var lines = new System.Collections.Generic.List<string>();
        lines.Add($"OPENAI_API_KEY: {(apiKeySet ? "(present)" : "(not set)")}");
        lines.Add($"OPENAI_MODEL: {mark("OPENAI_MODEL", config.Model)}");
        lines.Add($"OPENAI_MAX_COMPLETION_TOKENS: {mark("OPENAI_MAX_COMPLETION_TOKENS", config.MaxTokensPerCall.ToString())}");
        lines.Add($"OPENAI_HTTP_TIMEOUT_SECONDS: {mark("OPENAI_HTTP_TIMEOUT_SECONDS", ((int)config.HttpTimeout.TotalSeconds).ToString())}");
        lines.Add($"DRY_RUN: {mark("DRY_RUN", valBool(config.IsDryRun))}");
        lines.Add($"SHOW_USAGE: {mark("SHOW_USAGE", valBool(config.ShowUsage))}");
        lines.Add($"TREAT_REFUSAL_AS_ERROR: {mark("TREAT_REFUSAL_AS_ERROR", valBool(config.TreatRefusalAsError))}");
        lines.Add($"DEMO_MODE: {mark("DEMO_MODE", valBool(config.DemoMode))}");
        lines.Add($"NODE_DETAIL_WORDS: {mark("NODE_DETAIL_WORDS", config.NodeDetailWords.ToString())}");
        lines.Add($"NODE_SUMMARY_WORDS: {mark("NODE_SUMMARY_WORDS", config.NodeSummaryWords.ToString())}");
        lines.Add($"DEBUG: {mark("DEBUG", valBool(config.DebugLogging))}");
        lines.Add($"USE_RESPONSES_API: {mark("USE_RESPONSES_API", valBool(config.UseResponsesApi))}");
        lines.Add($"ENABLE_WEB_SEARCH: {mark("ENABLE_WEB_SEARCH", valBool(config.EnableWebSearch))}");
        lines.Add($"CACHE_SYSTEM_INPUT: {mark("CACHE_SYSTEM_INPUT", valBool(config.CacheSystemInput))}");
        lines.Add($"CACHE_BOOK_CONTEXT: {mark("CACHE_BOOK_CONTEXT", valBool(config.CacheBookContext))}");
        lines.Add($"RESPONSES_STRICT_JSON: {mark("RESPONSES_STRICT_JSON", valBool(config.ResponsesStrictJson))}");
        var idx = string.IsNullOrWhiteSpace(config.IndexMdPath) ? "(none)" : config.IndexMdPath!;
        lines.Add($"INDEX_MD_PATH: {mark("INDEX_MD_PATH", idx)}");
        lines.Add($"CUSTOM_MD_BEAUTIFY: {mark("CUSTOM_MD_BEAUTIFY", valBool(config.CustomBeautifyEnabled))}");
        lines.Add($"PrevChapterTailChars: {config.PrevChapterTailChars} (Default)");
        lines.Add($"STRIP_LINKS: {mark("STRIP_LINKS", config.StripLinks ? "true" : "false")}");
        var ta = Environment.GetEnvironmentVariable("TARGET_AUDIENCE");
        var tp = Environment.GetEnvironmentVariable("TOPIC");
        var dt = Environment.GetEnvironmentVariable("DOC_TITLE");
        var beta = Environment.GetEnvironmentVariable("OPENAI_BETA_HEADER");
        lines.Add($"TARGET_AUDIENCE: {(string.IsNullOrWhiteSpace(ta) ? "(not set) (Default)" : ta)}");
        lines.Add($"TOPIC: {(string.IsNullOrWhiteSpace(tp) ? "(not set) (Default)" : tp)}");
        lines.Add($"DOC_TITLE: {(string.IsNullOrWhiteSpace(dt) ? "(not set) (Default)" : dt)}");
        lines.Add($"OPENAI_BETA_HEADER: {(string.IsNullOrWhiteSpace(beta) ? "(not set) (Default)" : "(present)")}");

        log.LogInformation("— Configuration Resolved —");
        ui.WriteLine("— Configuration Resolved —", ConsoleColor.Yellow);
        foreach (var line in lines)
        {
            log.LogInformation(line);
            ui.WriteLine(line, ConsoleColor.DarkGray);
        }
    }

    private static void TryLoadExecutionParameters(string[]? args)
    {
        // Orden de búsqueda: arg --params/-p, env EXECUTION_PARAMETERS_PATH, archivos por defecto en cwd
        string? path = null;
        var a = args ?? Array.Empty<string>();
        for (int i = 0; i < a.Length; i++)
        {
            if ((a[i] == "--params" || a[i] == "-p") && i + 1 < a.Length)
            {
                path = a[i + 1];
                break;
            }
        }
        path ??= Environment.GetEnvironmentVariable("EXECUTION_PARAMETERS_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            var cwd = Environment.CurrentDirectory;
            var cand1 = System.IO.Path.Combine(cwd, "executionparameters.config");
            var cand2 = System.IO.Path.Combine(cwd, "executionparameters.json");
            if (System.IO.File.Exists(cand1)) path = cand1;
            else if (System.IO.File.Exists(cand2)) path = cand2;
        }
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;

        try
        {
            var json = System.IO.File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            string S(string name) => root.TryGetProperty(name, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.String ? (el.GetString() ?? "") : string.Empty;
            bool? B(string name)
            {
                if (!root.TryGetProperty(name, out var el)) return null;
                return el.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.String => bool.TryParse(el.GetString(), out var v) ? v : (bool?)null,
                    _ => null
                };
            }
            int? I(string name)
            {
                if (!root.TryGetProperty(name, out var el)) return null;
                if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetInt32(out var vi)) return vi;
                if (el.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(el.GetString(), out var vs)) return vs;
                return null;
            }

            void Set(string env, string? val) { if (!string.IsNullOrWhiteSpace(val)) Environment.SetEnvironmentVariable(env, val); }
            void SetB(string env, bool? val) { if (val.HasValue) Environment.SetEnvironmentVariable(env, val.Value ? "true" : "false"); }
            void SetI(string env, int? val) { if (val.HasValue) Environment.SetEnvironmentVariable(env, val.Value.ToString()); }

            Set("OPENAI_API_KEY", S("openai_api_key"));
            Set("OPENAI_MODEL", S("openai_model"));
            SetI("OPENAI_MAX_COMPLETION_TOKENS", I("max_tokens"));
            SetI("OPENAI_HTTP_TIMEOUT_SECONDS", I("http_timeout_seconds"));

            SetB("DRY_RUN", B("dry_run"));
            SetB("SHOW_USAGE", B("show_usage"));
            SetB("TREAT_REFUSAL_AS_ERROR", B("treat_refusal_as_error"));
            SetB("DEMO_MODE", B("demo_mode"));
            SetI("NODE_DETAIL_WORDS", I("node_detail_words"));
            SetI("NODE_SUMMARY_WORDS", I("node_summary_words"));
            SetB("DEBUG", B("debug"));
            SetB("USE_RESPONSES_API", B("use_responses_api"));
            SetB("ENABLE_WEB_SEARCH", B("enable_web_search"));
            SetB("CACHE_SYSTEM_INPUT", B("cache_system_input"));
            SetB("CACHE_BOOK_CONTEXT", B("cache_book_context"));
            SetB("RESPONSES_STRICT_JSON", B("responses_strict_json"));
            Set("OPENAI_BETA_HEADER", S("openai_beta_header"));

            Set("INDEX_MD_PATH", S("index_md_path"));
            SetB("CUSTOM_MD_BEAUTIFY", B("custom_md_beautify"));
            SetB("STRIP_LINKS", B("strip_links"));

            // Respuestas de inputs
            Set("TARGET_AUDIENCE", S("target_audience"));
            Set("TOPIC", S("topic"));
            Set("DOC_TITLE", S("doc_title"));
        }
        catch
        {
            // Ignorar errores de parseo para no bloquear ejecución interactiva
        }
    }
}
