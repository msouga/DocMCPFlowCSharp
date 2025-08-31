using System.Threading.Tasks;

public interface ILlmClient
{
    Task<string> AskAsync(string system, string user, string model, int maxTokens, string? jsonSchema = null);
    void PrintUsage();
}
