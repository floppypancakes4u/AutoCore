namespace AutoCore.Dev;

public sealed class LiveInventoryAddOptions
{
    public string ApiBaseUrl { get; private set; } = "http://127.0.0.1:27999";
    public string ProcessName { get; private set; } = "autoassault";
    public string? Character { get; private set; }
    public int[] ItemCbids { get; private set; } = [9333, 16503];

    public static LiveInventoryAddOptions Parse(string[] args)
    {
        var options = new LiveInventoryAddOptions();

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

                case "--items":
                    options.ItemCbids = RequireValue(args, ref i, "--items")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(int.Parse)
                        .ToArray();
                    break;

                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        if (options.ItemCbids.Length != 2)
            throw new ArgumentException("inventory-add-live expects exactly two item CBIDs.");

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
