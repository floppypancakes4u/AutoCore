namespace AutoCore.Dev;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("inventory-add-live", StringComparison.OrdinalIgnoreCase))
            return await LiveInventoryAddRunner.RunAsync(LiveInventoryAddOptions.Parse(args.Skip(args.Length == 0 ? 0 : 1).ToArray()));

        if (args[0].Equals("inventory-grab-live", StringComparison.OrdinalIgnoreCase))
            return await LiveInventoryGrabRunner.RunAsync(LiveInventoryGrabOptions.Parse(args.Skip(1).ToArray()));

        if (args[0].Equals("inventory-drop-live", StringComparison.OrdinalIgnoreCase))
            return await LiveInventoryDropRunner.RunAsync(LiveInventoryDropOptions.Parse(args.Skip(1).ToArray()));

        if (args[0].Equals("export-inventory-catalog", StringComparison.OrdinalIgnoreCase))
            return InventoryCatalogExportRunner.Run(args.Skip(1).ToArray());

        Console.Error.WriteLine("Unknown command.");
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  AutoCore.Dev inventory-add-live [--character <name>] [--api <url>] [--process <name>] [--items <cbid,cbid>]");
        Console.Error.WriteLine("  AutoCore.Dev inventory-grab-live [--character <name>] [--api <url>] [--process <name>] [--timeout <seconds>] [--breakpoint]");
        Console.Error.WriteLine("  AutoCore.Dev inventory-drop-live [--character <name>] [--api <url>] [--process <name>] [--timeout <seconds>]");
        Console.Error.WriteLine("  AutoCore.Dev export-inventory-catalog [--game-path <path>] [--output <json-path>]");
        return 2;
    }
}
