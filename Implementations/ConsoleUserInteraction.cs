using System;
using System.Text;

public class ConsoleUserInteraction : IUserInteraction
{
    public ConsoleUserInteraction()
    {
        Console.OutputEncoding = Encoding.UTF8;
    }

    public void Write(string message, ConsoleColor? color = null)
    {
        if (color.HasValue) Console.ForegroundColor = color.Value;
        Console.Write(message);
        if (color.HasValue) Console.ResetColor();
    }

    public void WriteLine(string message, ConsoleColor? color = null)
    {
        if (color.HasValue) Console.ForegroundColor = color.Value;
        Console.WriteLine(message);
        if (color.HasValue) Console.ResetColor();
    }

    public string ReadLine(string prompt)
    {
        Write(prompt);
        return Console.ReadLine() ?? string.Empty;
    }

    public bool Confirm(string prompt)
    {
        var response = ReadLine(prompt).Trim().ToLowerInvariant();
        return response == "s" || response == "si" || response == "sí";
    }
}
