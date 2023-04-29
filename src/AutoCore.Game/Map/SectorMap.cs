namespace AutoCore.Game.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

public class SectorMap
{
    public int ContinentId { get; }
    public MapData MapData { get; private set; }
    public ContinentObject ContinentObject => MapData.ContinentObject;
    public Dictionary<TFID, GhostObject> GhostObjects { get; } = new();

    public SectorMap(int continentId)
    {
        ContinentId = continentId;
        MapData = AssetManager.Instance.GetMapData(ContinentId);
    }

    public void Fill(MapInfoPacket packet)
    {
        packet.RegionId = 0;
        packet.RegionType = TilesetType.Universal;
        packet.RegionLevel = 1;
        packet.LayerId = 0;
        packet.ObjectiveIndex = ContinentObject.Objective;
        packet.MapName = $"{ContinentObject.MapFileName}.fam";
        packet.IsTown = ContinentObject.IsTown;
        packet.IsArena = ContinentObject.IsArena;
        packet.OwningFaction = ContinentObject.OwningFaction;
        packet.ContinentObjectId = ContinentId;
        packet.IsPersistent = ContinentObject.IsPersistent;
        packet.MapIterationVersion = MapData.IterationVersion;
        packet.ContestedMissionId = ContinentObject.ContestedMission;
        packet.Coid = ContinentId;
        packet.TemporalRandomSeed = 123456789;
        packet.CoidMap = ContinentId;
        packet.NumModulePlacements = 0;
        packet.PositionX = 0.0f;
        packet.PositionY = 0.0f;
        packet.PositionZ = 0.0f;
        packet.WeatherUpdateSize = 0;
    }
}
