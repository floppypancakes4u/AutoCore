namespace AutoCore.Utils.Commands;

public static class CommandProcessor
{
    private static readonly Dictionary<string, Action<string[]>> Commands = new();
    private static bool TrimScope = true;

    public static bool UseScopes() => TrimScope = false;

    public static void ProcessCommand()
    {
        var command = ReadCommand();
        if (string.IsNullOrWhiteSpace(command))
            return;

        var parts = command.Split(' ');
        if (parts.Length < 1)
            return;

        if (TrimScope && parts[0].Contains('.'))
            parts[0] = parts[0][(parts[0].IndexOf(".") + 1)..];

        if (Commands.TryGetValue(parts[0], out var value))
        {
            value(parts);
            return;
        }

        Logger.WriteLog(LogType.Command, $"Invalid command: {command}");
    }

    private static string ReadCommand()
    {
        var command = string.Empty;

        if (Console.KeyAvailable)
        {
            while (true)
            {
                var key = Console.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        return command;

                    case ConsoleKey.Backspace:
                        if (command.Length > 0)
                            command = command[0..^1];
                        break;

                    default:
                        command += key.KeyChar;
                        break;
                }
            }
        }

        return null;
    }

    public static void RegisterCommand(string name, Action<string[]> handler)
    {
        if (TrimScope && name.Contains('.'))
            name = name[(name.IndexOf(".") + 1)..];

        Commands.Add(name, handler);
    }

    public static void RemoveCommand(string name)
    {
        if (TrimScope && name.Contains('.'))
            name = name[(name.IndexOf(".") + 1)..];

        Commands.Remove(name);
    }
}
