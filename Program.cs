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
            // Construir ILogger (consola + archivo back + archivo root)
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddProvider(new FileLoggerProvider(backLogPath, rootLogPath));
            });
            var log = loggerFactory.CreateLogger<Program>();
            log.LogInformation($"Inicio de sesión — {DateTime.Now:O}");

            var llmClient = new OpenAiSdkLlmClient(config, ui, loggerFactory.CreateLogger<OpenAiSdkLlmClient>());
            var manuscriptWriter = new MarkdownManuscriptWriter(loggerFactory.CreateLogger<MarkdownManuscriptWriter>());
            
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
        catch { /* si no se puede escribir, seguimos sin romper */ }
    }
}
