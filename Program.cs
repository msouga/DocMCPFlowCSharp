using System;
using System.Diagnostics;
using System.Threading.Tasks;

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
        var logFilePath = System.IO.Path.Combine(runDir, $"{runId}-log.txt");
        try
        {
            System.IO.Directory.CreateDirectory(runDir);
            await System.IO.File.WriteAllTextAsync(logFilePath, $"# Log de sesión — {DateTime.Now:O}\n\n");
            Logger.Init(logFilePath);
            Logger.Append("Inicio de sesión y preparación de entorno");
        }
        catch { /* si no se puede escribir, seguimos sin romper */ }

        var llmClient = new OpenAiSdkLlmClient(config, ui);
        var manuscriptWriter = new MarkdownManuscriptWriter();
        
        var orchestrator = new BookGenerator(config, ui, llmClient, manuscriptWriter);

        var flowSw = Stopwatch.StartNew();
        try
        {
            Logger.Append("Arrancando orquestador de flujo (BookGenerator.RunAsync)");
            await orchestrator.RunAsync();
            Logger.Append("Ejecución de orquestador completada");
        }
        catch (Exception ex)
        {
            ui.WriteLine($"\n[ERROR FATAL] {ex.Message}", ConsoleColor.Red);
            ui.WriteLine(ex.ToString(), ConsoleColor.DarkRed);
            Logger.Append($"ERROR FATAL: {ex}");
        }
        finally
        {
            flowSw.Stop();
            llmClient.PrintUsage();
            ui.WriteLine($"Tiempo total: {flowSw.Elapsed}", ConsoleColor.Magenta);
            Logger.Append($"Tiempo total: {flowSw.Elapsed}");
        }
    }
}
