namespace AutoCore.Dev;

public enum LiveInventoryDropMode
{
    Toss,
    CargoMove
}

public sealed class LiveInventoryDropOptions
{
    public string ApiBaseUrl { get; private set; } = "http://127.0.0.1:27999";
    public string ProcessName { get; private set; } = "autoassault";
    public string? Character { get; private set; }
    public int TimeoutSeconds { get; private set; } = 120;
    public LiveInventoryDropMode Mode { get; private set; } = LiveInventoryDropMode.Toss;

    public static LiveInventoryDropOptions Parse(string[] args)
    {
        var options = new LiveInventoryDropOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--api":
                    options.ApiBaseUrl = RequireValue(args, ref i, "--api").TrimEnd('/');
                    break;

                case "--process":
                    options.ProcessName = RequireValue(args, ref i, "--process");
                    break;

                case "--character":
                    options.Character = RequireValue(args, ref i, "--character");
                    break;

                case "--timeout":
                    options.TimeoutSeconds = int.Parse(RequireValue(args, ref i, "--timeout"));
                    break;

                case "--mode":
                    options.Mode = ParseMode(RequireValue(args, ref i, "--mode"));
                    break;

                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        if (options.TimeoutSeconds <= 0)
            throw new ArgumentException("--timeout must be greater than zero.");

        return options;
    }

    private static LiveInventoryDropMode ParseMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "toss" => LiveInventoryDropMode.Toss,
            "cargo-move" => LiveInventoryDropMode.CargoMove,
            _ => throw new ArgumentException("--mode must be 'toss' or 'cargo-move'.")
        };

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{option} requires a value.");

        index++;
        return args[index];
    }
}
