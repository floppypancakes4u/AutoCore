namespace AutoCore.Dev;

using System.Text;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers.Asset;

public static class InventoryCatalogExportRunner
{
    private const string DefaultGamePath = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    public static int Run(string[] args)
    {
        if (!TryParseOptions(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return 2;
        }

        var wadPath = Path.Combine(options.GamePath, "clonebase.wad");
        if (!File.Exists(wadPath))
        {
            Console.Error.WriteLine($"clonebase.wad not found at: {wadPath}");
            Console.Error.WriteLine("Pass --game-path <Auto Assault install directory>.");
            return 1;
        }

        var loader = new WADLoader();
        if (!loader.Load(wadPath))
        {
            Console.Error.WriteLine($"Failed to load clonebase.wad from: {wadPath}");
            return 1;
        }

        var document = InventoryItemExporter.ExportFromCloneBases(loader.CloneBases, wadPath);
        var json = InventoryItemExporter.Serialize(document);
        var standaloneHtml = InventoryCatalogHtmlGenerator.GenerateStandalone(document);

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
        File.WriteAllText(options.OutputPath, json);

        var standalonePath = Path.Combine(
            Path.GetDirectoryName(options.OutputPath)!,
            "inventory-catalog-standalone.html");
        File.WriteAllText(standalonePath, standaloneHtml, Encoding.UTF8);

        Console.WriteLine($"Exported {document.ItemCount} inventory items to {options.OutputPath}");
        Console.WriteLine($"Exported standalone catalog to {standalonePath}");
        return 0;
    }

    private static bool TryParseOptions(string[] args, out ExportOptions options, out string error)
    {
        options = new ExportOptions(
            DefaultGamePath,
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tools", "inventory-catalog", "inventory-items.json")));

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game-path" when i + 1 < args.Length:
                    options = options with { GamePath = args[++i] };
                    break;
                case "--output" when i + 1 < args.Length:
                    options = options with { OutputPath = Path.GetFullPath(args[++i]) };
                    break;
                default:
                    error = $"Unknown or incomplete argument: {args[i]}";
                    return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  AutoCore.Dev export-inventory-catalog [--game-path <path>] [--output <json-path>]");
    }

    private sealed record ExportOptions(string GamePath, string OutputPath);
}
