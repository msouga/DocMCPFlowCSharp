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

        var logFilePath = $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-log.txt";
        try
        {
            await System.IO.File.WriteAllTextAsync(logFilePath, $"# Log de sesión — {DateTime.Now:O}\n\n");
        }
        catch { /* si no se puede escribir, seguimos sin romper */ }

        var llmClient = new OpenAiSdkLlmClient(config, ui);
        var manuscriptWriter = new MarkdownManuscriptWriter();
        
        var orchestrator = new BookGenerator(config, ui, llmClient, manuscriptWriter);

        var flowSw = Stopwatch.StartNew();
        try
        {
            await orchestrator.RunAsync();
        }
        catch (Exception ex)
        {
            ui.WriteLine($"\n[ERROR FATAL] {ex.Message}", ConsoleColor.Red);
            ui.WriteLine(ex.ToString(), ConsoleColor.DarkRed);
        }
        finally
        {
            flowSw.Stop();
            llmClient.PrintUsage();
            ui.WriteLine($"Tiempo total: {flowSw.Elapsed}", ConsoleColor.Magenta);
        }
    }
}