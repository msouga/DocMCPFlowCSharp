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
        var line = Console.ReadLine();
        if (line is null)
        {
            // Evita loops infinitos cuando la entrada estándar se agota (EOF),
            // por ejemplo al usar pipes y no hay más líneas disponibles.
            throw new System.IO.EndOfStreamException("Se alcanzó el fin de la entrada estándar (EOF) y no hay más datos para leer.");
        }
        return line;
    }

    public bool Confirm(string prompt)
    {
        var response = ReadLine(prompt).Trim().ToLowerInvariant();
        return response == "s" || response == "si" || response == "sí";
    }
}
