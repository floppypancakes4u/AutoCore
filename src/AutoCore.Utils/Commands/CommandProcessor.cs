namespace AutoCore.Utils.Commands;

public static class CommandProcessor
{
    private static readonly Dictionary<string, Action<string[]>> Commands = new();

    public static void ProcessCommand()
    {
        var command = ReadCommand();
        if (string.IsNullOrWhiteSpace(command))
            return;

        var parts = command.Split(' ');
        if (parts.Length < 1)
            return;

        if (Commands.ContainsKey(parts[0]))
        {
            Commands[parts[0]](parts);
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
        Commands.Add(name, handler);
    }

    public static void RemoveCommand(string name)
    {
        if (Commands.ContainsKey(name))
            Commands.Remove(name);
    }
}
