using System;

public interface IUserInteraction
{
    void Write(string message, ConsoleColor? color = null);
    void WriteLine(string message, ConsoleColor? color = null);
    string ReadLine(string prompt);
    bool Confirm(string prompt);
}
