namespace AutoCore.Game.Managers.Asset;

using AutoCore.Database.World;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Utils;

public class WorldDBLoader
{
    public IDictionary<Tuple<byte, byte>, ConfigNewCharacter> ConfigNewCharacters { get; set; }
    public IDictionary<Tuple<int, byte>, ContinentArea> ContinentAreas { get; set; }
    public IDictionary<int, ContinentObject> ContinentObjects { get; set; }
    public IDictionary<byte, ExperienceLevel> ExperienceLevels { get; set; }
    public IDictionary<int, LootTable> LootTables { get; set; }

    public bool Load()
    {
        using var worldContext = new WorldContext();

        ContinentObjects = worldContext.ContinentObjects.Where(ContinentObjectValidator).ToDictionary(co => co.Id);

        if (AssetManager.Instance.ServerType == ServerType.Global || AssetManager.Instance.ServerType == ServerType.Both)
        {
            ConfigNewCharacters = worldContext.ConfigNewCharacters.ToDictionary(cnc => Tuple.Create(cnc.Race, cnc.Class));
        }

        if (AssetManager.Instance.ServerType == ServerType.Sector || AssetManager.Instance.ServerType == ServerType.Both)
        {
            ContinentAreas = worldContext.ContinentAreas.ToDictionary(ca => Tuple.Create(ca.ContinentObjectId, ca.Area));
            ExperienceLevels = worldContext.ExperienceLevels.ToDictionary(el => el.Level);
        }

        // If the World DB is empty (common for fresh setups), bootstrap core world data from `wad.xml` in GamePath.
        // AA-Server’s `wad.xml` contains authoritative tables like `tConfigNewCharacters`, `tContinentObject`, etc.
        try
        {
            var wadXmlPath = Path.Combine(AssetManager.Instance.GamePath, "wad.xml");
            if (File.Exists(wadXmlPath))
            {
                if (ContinentObjects == null || ContinentObjects.Count == 0)
                {
                    var fromWad = WadXmlWorldDataLoader.LoadContinentObjects(wadXmlPath)
                        .Where(kvp => ContinentObjectValidator(kvp.Value))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    ContinentObjects = fromWad;
                    Logger.WriteLog(LogType.Initialize, $"WorldDBLoader: Loaded {ContinentObjects.Count} ContinentObjects from wad.xml");
                }

                if ((AssetManager.Instance.ServerType == ServerType.Global || AssetManager.Instance.ServerType == ServerType.Both) &&
                    (ConfigNewCharacters == null || ConfigNewCharacters.Count == 0))
                {
                    ConfigNewCharacters = WadXmlWorldDataLoader.LoadConfigNewCharacters(wadXmlPath);
                    Logger.WriteLog(LogType.Initialize, $"WorldDBLoader: Loaded {ConfigNewCharacters.Count} ConfigNewCharacters from wad.xml");
                }

                if ((AssetManager.Instance.ServerType == ServerType.Sector || AssetManager.Instance.ServerType == ServerType.Both))
                {
                    if (ContinentAreas == null || ContinentAreas.Count == 0)
                    {
                        ContinentAreas = WadXmlWorldDataLoader.LoadContinentAreas(wadXmlPath);
                        Logger.WriteLog(LogType.Initialize, $"WorldDBLoader: Loaded {ContinentAreas.Count} ContinentAreas from wad.xml");
                    }

                    if (ExperienceLevels == null || ExperienceLevels.Count == 0)
                    {
                        ExperienceLevels = WadXmlWorldDataLoader.LoadExperienceLevels(wadXmlPath);
                        Logger.WriteLog(LogType.Initialize, $"WorldDBLoader: Loaded {ExperienceLevels.Count} ExperienceLevels from wad.xml");
                    }

                    if (LootTables == null || LootTables.Count == 0)
                    {
                        LootTables = WadXmlWorldDataLoader.LoadLootTables(wadXmlPath);
                        Logger.WriteLog(LogType.Initialize, $"WorldDBLoader: Loaded {LootTables.Count} LootTables from wad.xml");
                    }
                }
            }
            else
            {
                Logger.WriteLog(LogType.Error, $"WorldDBLoader: wad.xml not found at '{wadXmlPath}'. World DB bootstrap is unavailable.");
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"WorldDBLoader: Failed to bootstrap from wad.xml: {ex}");
        }

        return true;
    }

    private static bool ContinentObjectValidator(ContinentObject continentObject)
    {
        if (continentObject == null)
            return false;

        return AssetManager.Instance.HasFileInGLMs($"{continentObject.MapFileName}.fam");
    }
}
