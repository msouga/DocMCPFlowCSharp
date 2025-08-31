using System;
using System.IO;
using System.Threading.Tasks;

public static class Logger
{
    private static readonly object _sync = new();
    private static string? _path;
    public static string? RunDirectory { get; private set; }

    public static void Init(string path)
    {
        lock (_sync)
        {
            _path = path;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    RunDirectory = dir;
                }
            }
            catch { /* best-effort */ }
        }
    }

    public static void Append(string message)
    {
        try
        {
            string? path;
            lock (_sync) { path = _path; }
            if (string.IsNullOrEmpty(path)) return;
            var line = $"[{DateTime.Now:O}] {message}\n";
            File.AppendAllText(path!, line);
        }
        catch { /* no romper el flujo por errores de log */ }
    }

    public static Task AppendAsync(string message)
    {
        try
        {
            string? path;
            lock (_sync) { path = _path; }
            if (string.IsNullOrEmpty(path)) return Task.CompletedTask;
            var line = $"[{DateTime.Now:O}] {message}\n";
            return File.AppendAllTextAsync(path!, line);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }
}
