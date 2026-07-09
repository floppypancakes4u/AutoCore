namespace AutoCore.Dev;

public sealed class LiveInventoryGrabOptions
{
    public string ApiBaseUrl { get; private set; } = "http://127.0.0.1:27999";
    public string ProcessName { get; private set; } = "autoassault";
    public string? Character { get; private set; }
    public int TimeoutSeconds { get; private set; } = 120;
    public bool UseBreakpoint { get; private set; }

    public static LiveInventoryGrabOptions Parse(string[] args)
    {
        var options = new LiveInventoryGrabOptions();

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

                case "--breakpoint":
                    options.UseBreakpoint = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        if (options.TimeoutSeconds <= 0)
            throw new ArgumentException("--timeout must be greater than zero.");

        return options;
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{option} requires a value.");

        index++;
        return args[index];
    }
}
