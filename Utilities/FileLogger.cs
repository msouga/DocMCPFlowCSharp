using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _backLogPath;
    private readonly string _rootLogPath;
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(string backLogPath, string rootLogPath, LogLevel minLevel)
    {
        _backLogPath = backLogPath;
        _rootLogPath = rootLogPath;
        _minLevel = minLevel;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_backLogPath)!);
            // Truncar/crear ambos logs al inicio de la ejecución
            File.WriteAllText(_backLogPath, string.Empty, Encoding.UTF8);
            File.WriteAllText(_rootLogPath, string.Empty, Encoding.UTF8);
        }
        catch { /* best effort */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _backLogPath, _rootLogPath, _minLevel);

    public void Dispose() { }
}

internal class FileLogger : ILogger
{
    private readonly string _category;
    private readonly string _backLogPath;
    private readonly string _rootLogPath;
    private readonly LogLevel _minLevel;
    private static readonly object _sync = new();

    public FileLogger(string category, string backLogPath, string rootLogPath, LogLevel minLevel)
    {
        _category = category;
        _backLogPath = backLogPath;
        _rootLogPath = rootLogPath;
        _minLevel = minLevel;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var line = $"[{DateTime.Now:O}] [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception != null) line += $" | EX: {exception}";

            lock (_sync)
            {
                File.AppendAllText(_backLogPath, line + Environment.NewLine, Encoding.UTF8);
                File.AppendAllText(_rootLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* no romper ejecución por fallos de log */ }
    }
}

internal sealed class NoopDisposable : IDisposable
{
    public static readonly NoopDisposable Instance = new NoopDisposable();
    private NoopDisposable() { }
    public void Dispose() { }
}
