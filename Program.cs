using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

internal class Program
{
    private static async Task Main()
    {
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
            PrintResolvedConfiguration(config, log);

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

    private static void PrintResolvedConfiguration(IConfiguration config, ILogger log)
    {
        string mark(string envName, string? overrideValue = null)
        {
            var isSet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envName));
            return isSet ? (overrideValue ?? string.Empty) : $"{overrideValue ?? string.Empty} (Default)";
        }

        string valBool(bool v) => v ? "true" : "false";

        // Sensibles: no mostrar el valor de la API key
        var apiKeySet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        log.LogInformation("OPENAI_API_KEY: {Val}", apiKeySet ? "(present)" : "(not set)");

        log.LogInformation("OPENAI_MODEL: {Val}", mark("OPENAI_MODEL", config.Model));
        log.LogInformation("OPENAI_MAX_COMPLETION_TOKENS: {Val}", mark("OPENAI_MAX_COMPLETION_TOKENS", config.MaxTokensPerCall.ToString()));
        log.LogInformation("OPENAI_HTTP_TIMEOUT_SECONDS: {Val}", mark("OPENAI_HTTP_TIMEOUT_SECONDS", ((int)config.HttpTimeout.TotalSeconds).ToString()));
        log.LogInformation("DRY_RUN: {Val}", mark("DRY_RUN", valBool(config.IsDryRun)));
        log.LogInformation("SHOW_USAGE: {Val}", mark("SHOW_USAGE", valBool(config.ShowUsage)));
        log.LogInformation("TREAT_REFUSAL_AS_ERROR: {Val}", mark("TREAT_REFUSAL_AS_ERROR", valBool(config.TreatRefusalAsError)));
        log.LogInformation("DEMO_MODE: {Val}", mark("DEMO_MODE", valBool(config.DemoMode)));
        log.LogInformation("NODE_DETAIL_WORDS: {Val}", mark("NODE_DETAIL_WORDS", config.NodeDetailWords.ToString()));
        log.LogInformation("NODE_SUMMARY_WORDS: {Val}", mark("NODE_SUMMARY_WORDS", config.NodeSummaryWords.ToString()));
        log.LogInformation("DEBUG: {Val}", mark("DEBUG", valBool(config.DebugLogging)));
        log.LogInformation("USE_RESPONSES_API: {Val}", mark("USE_RESPONSES_API", valBool(config.UseResponsesApi)));
        log.LogInformation("ENABLE_WEB_SEARCH: {Val}", mark("ENABLE_WEB_SEARCH", valBool(config.EnableWebSearch)));
        log.LogInformation("CACHE_SYSTEM_INPUT: {Val}", mark("CACHE_SYSTEM_INPUT", valBool(config.CacheSystemInput)));
        log.LogInformation("CACHE_BOOK_CONTEXT: {Val}", mark("CACHE_BOOK_CONTEXT", valBool(config.CacheBookContext)));
        log.LogInformation("RESPONSES_STRICT_JSON: {Val}", mark("RESPONSES_STRICT_JSON", valBool(config.ResponsesStrictJson)));
        var idx = string.IsNullOrWhiteSpace(config.IndexMdPath) ? "(none)" : config.IndexMdPath!;
        log.LogInformation("INDEX_MD_PATH: {Val}", mark("INDEX_MD_PATH", idx));
        log.LogInformation("CUSTOM_MD_BEAUTIFY: {Val}", mark("CUSTOM_MD_BEAUTIFY", valBool(config.CustomBeautifyEnabled)));
        log.LogInformation("PrevChapterTailChars: {Val}", $"{config.PrevChapterTailChars} (Default)");

        // Extras usados para modo no interactivo
        var ta = Environment.GetEnvironmentVariable("TARGET_AUDIENCE");
        var tp = Environment.GetEnvironmentVariable("TOPIC");
        var dt = Environment.GetEnvironmentVariable("DOC_TITLE");
        var beta = Environment.GetEnvironmentVariable("OPENAI_BETA_HEADER");
        log.LogInformation("TARGET_AUDIENCE: {Val}", string.IsNullOrWhiteSpace(ta) ? "(not set) (Default)" : ta);
        log.LogInformation("TOPIC: {Val}", string.IsNullOrWhiteSpace(tp) ? "(not set) (Default)" : tp);
        log.LogInformation("DOC_TITLE: {Val}", string.IsNullOrWhiteSpace(dt) ? "(not set) (Default)" : dt);
        log.LogInformation("OPENAI_BETA_HEADER: {Val}", string.IsNullOrWhiteSpace(beta) ? "(not set) (Default)" : "(present)");
    }
}
