namespace AutoCore.Game.Managers;

using System.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

public class AssetManager : Singleton<AssetManager>
{
    private bool DataLoaded { get; set; }
    private WADLoader WADLoader { get; } = new();
    private GLMLoader GLMLoader { get; } = new();
    private MapDataLoader MapDataLoader { get; } = new();
    private WorldDBLoader WorldDBLoader { get; } = new();

    public string GamePath { get; private set; }
    public ServerType ServerType { get; private set; }
    public bool AllowMissingCBID { get; set; } = false;

    #region Initialize
    public bool Initialize(string gamePath, ServerType serverType, bool allowMissingCBID = false)
    {
        Logger.WriteLog(LogType.Initialize, $"Initializing Asset Manager for {serverType}...");

        GamePath = gamePath;
        ServerType = serverType;
        AllowMissingCBID = allowMissingCBID;

        if (!Directory.Exists(GamePath) || !File.Exists(Path.Combine(GamePath, "exe", "autoassault.exe")))
        {
            Logger.WriteLog(LogType.Error, "Invalid GamePath is set in the config!");
            return false;
        }

        return true;
    }

    public bool LoadAllData()
    {
        if (DataLoaded)
            return false;

        var loadWadTask = Task<bool>.Factory.StartNew(() =>
        {
            try
            {
                var wadPath = Path.Combine(GamePath, "clonebase.wad");
                Logger.WriteLog(LogType.Initialize, $"Loading WAD file from: {wadPath}");
                
                if (!File.Exists(wadPath))
                {
                    Logger.WriteLog(LogType.Error, $"WAD file not found at: {wadPath}");
                    return false;
                }
                
                var result = WADLoader.Load(wadPath);
                if (result)
                {
                    Logger.WriteLog(LogType.Initialize, $"WAD loaded successfully. Total CloneBases: {WADLoader.CloneBases.Count}");
                    var characterCount = WADLoader.CloneBases.Values.Count(cb => cb is CloneBaseCharacter);
                    Logger.WriteLog(LogType.Initialize, $"Character CloneBases found: {characterCount}");
                }
                else
                {
                    Logger.WriteLog(LogType.Error, "WAD file failed to load");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"Exception while loading WAD: {ex}");
                Logger.WriteLog(LogType.Error, $"Stack trace: {ex.StackTrace}");
                return false;
            }
        });

        var loadGLMTask = Task<bool>.Factory.StartNew(() =>
        {
            try
            {
                return GLMLoader.Load(GamePath);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"Exception while loading GLM: {ex}");
                return false;
            }
        });

        var loadWorldDBTask = loadGLMTask.ContinueWith((prevTask) =>
        {
            try
            {
                if (!prevTask.Result)
                    return false;

                return WorldDBLoader.Load();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"Exception while loading WorldDB: {ex}");
                return false;
            }
        });

        var loadMapDataTask = Task.WhenAll(loadGLMTask, loadWorldDBTask).ContinueWith((previousValues) =>
        {
            try
            {
                if (previousValues.Result.Any(r => !r))
                    return false;

                return MapDataLoader.Load();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"Exception while loading MapData: {ex}");
                return false;
            }
        });

        Task.WaitAll(loadWadTask, loadGLMTask, loadWorldDBTask, loadMapDataTask);

        var successes = new List<string>();
        var failures = new List<string>();
        
        if (loadWadTask.Result)
            successes.Add("WAD");
        else
            failures.Add("WAD");
            
        if (loadGLMTask.Result)
            successes.Add("GLM");
        else
            failures.Add("GLM");
            
        if (loadWorldDBTask.Result)
            successes.Add("WorldDB");
        else
            failures.Add("WorldDB");
            
        if (loadMapDataTask.Result)
            successes.Add("MapData");
        else
            failures.Add("MapData");

        // Continue if at least WAD or GLM loaded successfully (these are critical)
        var hasCriticalAssets = loadWadTask.Result || loadGLMTask.Result;
        
        if (successes.Count > 0)
        {
            Logger.WriteLog(LogType.Initialize, $"Successfully loaded asset components: {string.Join(", ", successes)}");
        }
        
        if (failures.Count > 0)
        {
            Logger.WriteLog(LogType.Error, $"Failed to load asset components: {string.Join(", ", failures)}. Continuing with available assets.");
        }

        if (!hasCriticalAssets)
        {
            Logger.WriteLog(LogType.Error, "No critical assets (WAD or GLM) were loaded successfully. Cannot continue.");
            return false;
        }

        DataLoaded = true;

        if (failures.Count == 0)
        {
            Logger.WriteLog(LogType.Initialize, "Asset Manager has loaded all data!");
        }
        else
        {
            Logger.WriteLog(LogType.Initialize, $"Asset Manager loaded partial data ({successes.Count}/{successes.Count + failures.Count} components).");
        }

        return true;
    }
    #endregion

    #region WAD
    public Mission.Mission GetMission(int missionId)
    {
        if (WADLoader.Missions.TryGetValue(missionId, out var mission))
            return mission;
        return null;
    }

    public Mission.Mission GetMissionByObjectiveId(int objectiveId)
    {
        foreach (var mission in WADLoader.Missions.Values)
        {
            foreach (var objective in mission.Objectives.Values)
            {
                if (objective.ObjectiveId == objectiveId)
                    return mission;
            }
        }
        return null;
    }

    public IEnumerable<Mission.Mission> GetMissionsForContinent(int continentId)
    {
        return WADLoader.Missions.Values.Where(m => m.Continent == continentId);
    }

    public IEnumerable<Mission.Mission> GetAutoAssignMissions()
    {
        return WADLoader.Missions.Values.Where(m => m.AutoAssign != 0);
    }

    public CloneBase GetCloneBase(int CBID)
    {
        if (WADLoader.CloneBases.TryGetValue(CBID, out CloneBase value))
            return value;

        return null;
    }

    public T GetCloneBase<T>(int CBID) where T : CloneBase
    {
        return GetCloneBase(CBID) as T;
    }

    public List<int> GetAllCharacterCBIDs()
    {
        var characterCBIDs = new List<int>();
        Logger.WriteLog(LogType.Debug, $"GetAllCharacterCBIDs: Total CloneBases loaded: {WADLoader.CloneBases.Count}");

        foreach (var kvp in WADLoader.CloneBases)
        {
            var cloneBaseType = kvp.Value.GetType().Name;
            if (kvp.Value is CloneBaseCharacter)
            {
                characterCBIDs.Add(kvp.Key);
                Logger.WriteLog(LogType.Debug, $"GetAllCharacterCBIDs: Found character CBID {kvp.Key} (Type: {cloneBaseType})");
            }
        }

        Logger.WriteLog(LogType.Debug, $"GetAllCharacterCBIDs: Found {characterCBIDs.Count} character CBIDs out of {WADLoader.CloneBases.Count} total CloneBases");
        return characterCBIDs.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Returns all loaded CloneBases as a dictionary.
    /// </summary>
    public IReadOnlyDictionary<int, CloneBase> GetAllCloneBases()
    {
        return WADLoader.CloneBases;
    }
    #endregion

    #region GLM
    public BinaryReader GetFileReaderFromGLMs(string fileName) => GLMLoader.GetReader(fileName);
    public MemoryStream GetFileStreamFromGLMs(string fileName) => GLMLoader.GetStream(fileName);
    public bool HasFileInGLMs(string fileName) => GLMLoader.CanGetReader(fileName);
    #endregion

    #region WorldDB

    public ContinentObject GetContinentObject(int continentObjectId)
    {
        if (WorldDBLoader.ContinentObjects == null)
            return null;

        if (WorldDBLoader.ContinentObjects.TryGetValue(continentObjectId, out var result))
            return result;

        return null;
    }

    public IEnumerable<ContinentObject> GetContinentObjects()
    {
        if (WorldDBLoader.ContinentObjects == null)
            return Enumerable.Empty<ContinentObject>();

        return WorldDBLoader.ContinentObjects.Values;
    }

    /// <summary>
    /// Looks up a continent object directly from wad.xml, bypassing the filter.
    /// Used for error messages when a map transfer fails because the map file is missing.
    /// </summary>
    public ContinentObject GetContinentObjectFromWad(int continentObjectId)
    {
        try
        {
            var wadXmlPath = Path.Combine(GamePath, "wad.xml");
            if (!File.Exists(wadXmlPath))
                return null;

            var allContinents = WadXmlWorldDataLoader.LoadContinentObjects(wadXmlPath);
            if (allContinents.TryGetValue(continentObjectId, out var result))
                return result;
        }
        catch
        {
            // Ignore errors - this is just for diagnostics
        }
        return null;
    }

    public MapData GetMapData(int mapId)
    {
        if (MapDataLoader.MapDatas.TryGetValue(mapId, out var result))
            return result;

        return null;
    }

    public ConfigNewCharacter GetConfigNewCharacterFor(byte characterRace, byte characterClass)
    {
        if (ServerType != ServerType.Global && ServerType != ServerType.Both)
            throw new Exception("Invalid server type!");

        if (WorldDBLoader.ConfigNewCharacters == null)
            return null;

        if (WorldDBLoader.ConfigNewCharacters.TryGetValue(Tuple.Create(characterRace, characterClass), out var result))
            return result;

        return null;
    }

    public ConfigNewCharacter GetConfigNewCharacterFallback(byte characterRace, byte characterClass)
    {
        if (ServerType != ServerType.Global && ServerType != ServerType.Both)
            throw new Exception("Invalid server type!");

        if (WorldDBLoader.ConfigNewCharacters == null)
            return null;

        // First try: same race, any class
        var sameRaceConfig = WorldDBLoader.ConfigNewCharacters
            .FirstOrDefault(kvp => kvp.Key.Item1 == characterRace)
            .Value;
        if (sameRaceConfig != null)
            return sameRaceConfig;

        // Second try: any race, same class
        var sameClassConfig = WorldDBLoader.ConfigNewCharacters
            .FirstOrDefault(kvp => kvp.Key.Item2 == characterClass)
            .Value;
        if (sameClassConfig != null)
            return sameClassConfig;

        // Third try: any available config
        return WorldDBLoader.ConfigNewCharacters.Values.FirstOrDefault();
    }

    public List<string> GetAllAvailableConfigs()
    {
        if (WorldDBLoader.ConfigNewCharacters == null)
            return new List<string>();

        return WorldDBLoader.ConfigNewCharacters
            .Select(kvp => $"Race {kvp.Key.Item1}, Class {kvp.Key.Item2}")
            .OrderBy(x => x)
            .ToList();
    }

    public ConfigNewCharacter GenerateConfigFromGameData(byte characterRace, byte characterClass)
    {
        if (WADLoader.CloneBases == null || WADLoader.CloneBases.Count == 0)
        {
            Logger.WriteLog(LogType.Error, "GenerateConfigFromGameData: No CloneBases loaded");
            return null;
        }

        // Find first available vehicle
        var vehicle = WADLoader.CloneBases.Values
            .FirstOrDefault(cb => cb.Type == CloneBaseObjectType.Vehicle);
        if (vehicle == null)
        {
            Logger.WriteLog(LogType.Error, "GenerateConfigFromGameData: No vehicles found in game data");
            return null;
        }

        // Find first available powerplant
        var powerPlant = WADLoader.CloneBases.Values
            .FirstOrDefault(cb => cb.Type == CloneBaseObjectType.PowerPlant);
        if (powerPlant == null)
        {
            Logger.WriteLog(LogType.Error, "GenerateConfigFromGameData: No powerplants found in game data");
            return null;
        }

        // Find first available armor
        var armor = WADLoader.CloneBases.Values
            .FirstOrDefault(cb => cb.Type == CloneBaseObjectType.Armor);
        if (armor == null)
        {
            Logger.WriteLog(LogType.Error, "GenerateConfigFromGameData: No armor found in game data");
            return null;
        }

        // Find first available weapon
        var weapon = WADLoader.CloneBases.Values
            .FirstOrDefault(cb => cb.Type == CloneBaseObjectType.Weapon);
        if (weapon == null)
        {
            Logger.WriteLog(LogType.Error, "GenerateConfigFromGameData: No weapons found in game data");
            return null;
        }

        // `wad.xml` uses CBIDRaceItem that points to a CloneBase with `intType=6` (Item).
        // AutoCore previously looked for CloneBaseObjectType.RaceItem (70), but those clonebases don't exist in `clonebase.wad`.
        // So, for a last-resort generated config, just pick the first Item.
        var raceItem = WADLoader.CloneBases.Values
            .FirstOrDefault(cb => cb.Type == CloneBaseObjectType.Item);
        if (raceItem == null)
        {
            Logger.WriteLog(LogType.Error, "GenerateConfigFromGameData: No items found in game data (needed for RaceItem slot)");
            return null;
        }

        // Find first available map/town
        var startTown = -1;
        if (MapDataLoader.MapDatas != null && MapDataLoader.MapDatas.Count > 0)
        {
            startTown = MapDataLoader.MapDatas.Keys.First();
        }
        else
        {
            Logger.WriteLog(LogType.Error, "GenerateConfigFromGameData: No maps found in game data");
            return null;
        }

        var config = new ConfigNewCharacter
        {
            Race = characterRace,
            Class = characterClass,
            OptionCode = 0,
            PowerPlant = powerPlant.CloneBaseSpecific.CloneBaseId,
            Armor = armor.CloneBaseSpecific.CloneBaseId,
            RaceItem = raceItem.CloneBaseSpecific.CloneBaseId,
            SkillBattleMode1 = 0,
            SkillBattleMode2 = 0,
            SkillBattleMode3 = 0,
            StartSkill = 0,
            StartTown = startTown,
            Trailer = 0,
            Vehicle = vehicle.CloneBaseSpecific.CloneBaseId,
            Weapon = weapon.CloneBaseSpecific.CloneBaseId
        };

        Logger.WriteLog(LogType.Network, $"GenerateConfigFromGameData: Generated config for Race {characterRace}, Class {characterClass} - Vehicle: {config.Vehicle}, PowerPlant: {config.PowerPlant}, Armor: {config.Armor}, Weapon: {config.Weapon}, StartTown: {config.StartTown}");

        return config;
    }

    public LootTable GetLootTable(int lootTableId)
    {
        if (WorldDBLoader.LootTables == null)
            return null;

        if (WorldDBLoader.LootTables.TryGetValue(lootTableId, out var result))
            return result;

        return null;
    }

    public IEnumerable<LootTable> GetAllLootTables()
    {
        if (WorldDBLoader.LootTables == null)
            return Enumerable.Empty<LootTable>();

        return WorldDBLoader.LootTables.Values;
    }
    #endregion
}
