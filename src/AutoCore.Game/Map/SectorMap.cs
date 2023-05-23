namespace AutoCore.Game.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

public class SectorMap
{
    public int ContinentId { get; }
    public MapData MapData { get; private set; }
    public ContinentObject ContinentObject => MapData.ContinentObject;
    public Dictionary<TFID, ClonedObjectBase> Objects { get; } = new();

    public SectorMap(int continentId)
    {
        ContinentId = continentId;

        MapData = AssetManager.Instance.GetMapData(ContinentId);

        // TODO: create local objects from MapData's templates
    }

    public void Fill(MapInfoPacket packet)
    {
        packet.RegionId = 0;
        packet.RegionType = TilesetType.Universal;
        packet.RegionLevel = 1;
        packet.LayerId = 0;
        packet.ObjectiveIndex = ContinentObject.Objective;
        packet.MapName = ContinentObject.MapFileName;
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

    public void EnterMap(ClonedObjectBase clonedObject)
    {
        if (Objects.ContainsKey(clonedObject.ObjectId))
            Objects.Remove(clonedObject.ObjectId); // TEMP
        //    throw new InvalidOperationException("This object is already on the map!");

        Objects.Add(clonedObject.ObjectId, clonedObject);
    }

    public void LeaveMap(ClonedObjectBase clonedObject)
    {
        if (!Objects.ContainsKey(clonedObject.ObjectId))
            throw new InvalidOperationException("This object is not on the map!");

        Objects.Remove(clonedObject.ObjectId);
    }

    public IEnumerable<GhostObject> ObjectsInRange(GhostObject scopeObject)
    {
        // TODO: proper space partitioning, select entities based on distance!

        return Objects.Select(p => p.Value.Ghost).Where(g => g != null);
    }
}
