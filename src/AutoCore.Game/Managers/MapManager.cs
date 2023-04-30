namespace AutoCore.Game.Managers;

using AutoCore.Game.Map;
using AutoCore.Utils.Memory;

public class MapManager : Singleton<MapManager>
{
    private Dictionary<int, SectorMap> SectorMaps { get; } = new();

    public bool Initialize()
    {
        foreach (var continentObject in AssetManager.Instance.GetContinentObjects()) // TODO: only load IsPersistent maps (the others are instanceable?)
        {
            // TODO: preload only persistent maps?
            SetupMap(continentObject.Id);
        }

        return true;
    }

    private void SetupMap(int continentId)
    {
        if (SectorMaps.ContainsKey(continentId))
            throw new Exception($"Map {continentId} is already setup!");

        SectorMaps[continentId] = new SectorMap(continentId);
    }

    public SectorMap GetMap(int continentId)
    {
        if (SectorMaps.TryGetValue(continentId, out var sectorMap))
            return sectorMap;

        throw new Exception($"Unknown map ({continentId}) requested!");
    }
}
