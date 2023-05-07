using System.Diagnostics;

namespace AutoCore.Game.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Structures;
using AutoCore.Game.Weather;
using AutoCore.Utils;
using AutoCore.Utils.Extensions;

public class MapData
{
    public ContinentObject ContinentObject { get; }
    public Dictionary<long, ObjectTemplate> Templates { get; } = new();

    public int MapVersion { get; private set; }
    public int IterationVersion { get; private set; }
    public float GridSize { get; private set; }
    public byte TileSet { get; private set; }
    public bool UseRoad { get; private set; }
    public short[] Music { get; private set; }
    public bool UseClouds { get; private set; }
    public bool UseTimeOfDay { get; private set; }
    public string SkyBoxName { get; private set; }
    public float CullingScale { get; private set; }
    public int NumOfImports { get; private set; }
    public Vector4 EntryPoint { get; private set; }
    public int NumModulePlacements { get; private set; }
    public int NumOfVOGOs { get; private set; }
    public int NumOfClientVOGOs { get; private set; }
    public int HighestCoid { get; private set; }
    public long PerPlayerLoadTrigger { get; private set; }
    public long CreatorLoadTrigger { get; private set; }
    public long OnKillTrigger { get; private set; }
    public long LastTeamTrigger { get; private set; }
    public string WeatherStrEffect { get; private set; }
    public SeaPlane SeaPlane { get; private set; }
    public int Flags { get; private set; }

    public Dictionary<int, MissionString> MissionStrings { get; } = new();
    public Dictionary<int, VisualWaypoint> VisualWaypoints { get; } = new();
    public Dictionary<int, Variable> Variables { get; } = new();
    public Dictionary<byte, WeatherContainer> WeatherInfos { get; } = new();
    public List<RoadNodeBase> RoadNodes { get; } = new();
    public List<Music> MusicTriggers { get; } = new();

    public MapData(ContinentObject continentObject)
    {
        ContinentObject = continentObject;
    }

    public void Read(BinaryReader reader)
    {
        MapVersion = reader.ReadInt32();

        #region Terrain header
        if (MapVersion < 4 || MapVersion > 62)
            throw new Exception($"Invalid map version {MapVersion} for map {ContinentObject.MapFileName}.fam! Valid from 4 to 62!");

        if (MapVersion >= 27)
            IterationVersion = reader.ReadInt32();

        reader.BaseStream.Position += 8;

        GridSize = reader.ReadSingle();
        TileSet = reader.ReadByte();
        UseRoad = reader.ReadBoolean();
        Music = reader.ReadConstArray(3, reader.ReadInt16);

        if (MapVersion >= 11)
        {
            UseClouds = reader.ReadBoolean();
            UseTimeOfDay = reader.ReadBoolean();
            SkyBoxName = reader.ReadLengthedString();
        }

        if (MapVersion >= 36)
            CullingScale = reader.ReadSingle();

        if (MapVersion >= 45)
            NumOfImports = reader.ReadInt32();
        #endregion

        #region Common data
        EntryPoint = Vector4.ReadNew(reader);

        NumModulePlacements = reader.ReadInt32();
        NumOfVOGOs = reader.ReadInt32();
        NumOfClientVOGOs = reader.ReadInt32();
        HighestCoid = reader.ReadInt32();
        PerPlayerLoadTrigger = reader.ReadInt64();
        CreatorLoadTrigger = reader.ReadInt64();

        if (MapVersion >= 33)
            OnKillTrigger = reader.ReadInt64();

        if (MapVersion >= 34)
            LastTeamTrigger = reader.ReadInt64();

        ReadMissionStrings(reader);
        ReadVisualWaypoints(reader);
        ReadVariables(reader);

        if (MapVersion >= 47)
        {
            WeatherStrEffect = reader.ReadLengthedString();

            var regionCount = reader.ReadUInt32();
            for (var i = 0U; i < regionCount; ++i)
            {
                var regionId = reader.ReadByte();

                if (!WeatherInfos.ContainsKey(regionId))
                    WeatherInfos.Add(regionId, new WeatherContainer());

                var weatherCount = reader.ReadUInt32();
                for (var j = 0U; j < weatherCount; ++j)
                {
                    WeatherInfos[regionId].Weathers.Add(new WeatherInfo
                    {
                        SpecialType = reader.ReadUInt32(),
                        Type = reader.ReadUInt32(),
                        PercentChance = reader.ReadSingle(),
                        SpecialEventSkill = reader.ReadInt32(),
                        EventTimesPerMinute = reader.ReadByte(),
                        MinTimeToLive = reader.ReadUInt32(),
                        MaxTimeToLive = reader.ReadUInt32(),
                        LayerBits = MapVersion >= 54 ? reader.ReadUInt32() : 1,
                        FxName = reader.ReadLengthedString()
                    });
                }

                WeatherInfos[regionId].Effect = reader.ReadLengthedString();

                for (var j = 0; j < 4; ++j)
                    WeatherInfos[regionId].Environments.Add(reader.ReadLengthedString());
            }
        }

        if (MapVersion >= 38)
            ReadSeaPlaneData(reader);
        #endregion

        Debug.Assert(NumModulePlacements == 0, "There are modules???");

        for (var i = 0; i < NumOfClientVOGOs + NumOfVOGOs; ++i)
        {
            var layer = MapVersion > 5 ? reader.ReadByte() : (byte)0;

            var cbid = reader.ReadInt32();
            var coid = reader.ReadInt32();
            var objectSize = reader.ReadInt32();

            var objectStart = reader.BaseStream.Position;

            var obj = ObjectTemplate.AllocateTemplateFromCBID(cbid);
            if (obj == null)
            {
                reader.BaseStream.Position += objectSize;
                Logger.WriteLog(LogType.Error, $"Skip reading object CBID: {cbid} COID: {coid}, because they couldn't be allocated!");
                continue;
            }

            obj.Layer = layer;
            obj.CBID = cbid;
            obj.COID = coid;
            obj.Read(reader, MapVersion);

            if (objectStart + objectSize != reader.BaseStream.Position)
                throw new Exception($"The object was over or under read! Type: {obj.GetType().Name} Start: {objectStart}, Size: {objectSize}, ReadSize: {reader.BaseStream.Position - objectStart}");

            if (Templates.ContainsKey(coid))
                throw new Exception($"An object template with coid {coid} is already in the templates!");

            Templates.Add(coid, obj);
        }

        if (MapVersion >= 43)
            Flags = reader.ReadInt32();

        ReadRoadData(reader, MapVersion);

        if (MapVersion < 38)
            ReadSeaPlaneData(reader);

        if (MapVersion >= 42)
            ReadMusicRegions(reader, MapVersion);

        if (MapVersion >= 30)
        {
            // TODO?
        }

        ReadMiniCatalog(reader);
    }

    private void ReadMissionStrings(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; ++i)
        {
            var missionString = MissionString.Read(reader, MapVersion);

            MissionStrings.Add(missionString.StringId, missionString);
        }
    }

    private void ReadVisualWaypoints(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; ++i)
        {
            var visualWaypoint = VisualWaypoint.Read(reader);

            VisualWaypoints.Add(visualWaypoint.Id, visualWaypoint);
        }
    }

    private void ReadVariables(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; ++i)
        {
            var variable = Variable.Read(reader, MapVersion);

            Variables.Add(variable.Id, variable);
        }
    }

    private void ReadSeaPlaneData(BinaryReader reader)
    {
        // Sea Plane Data
        if (MapVersion >= 35)
        {
            if (reader.ReadByte() != 0)
            {
                var planeCount = reader.ReadInt32();

                SeaPlane = new SeaPlane
                {
                    Coords = Vector4.ReadNew(reader),
                    CoordsList = new List<Vector4>(reader.ReadConstArray(planeCount, Vector4.ReadNew)) // TODO: are thes TFIDs?
                };
            }
        }
    }

    private void ReadRoadData(BinaryReader reader, int mapVersion)
    {
        var roadCount = reader.ReadInt32();
        for (var i = 0; i < roadCount; ++i)
        {
            var roadNodeDataSize = reader.ReadInt32();
            var type = reader.ReadByte();

            var roadNodeStart = reader.BaseStream.Position;

            RoadNodeBase node = type switch
            {
                0 => new RoadNode(),
                1 => new RoadNodeJunction(),
                2 => new RiverNode(),
                _ => throw new NotSupportedException()
            };

            node.Unserialize(reader, mapVersion);

            if (roadNodeStart + roadNodeDataSize != reader.BaseStream.Position)
                throw new Exception("Over or under read RoadNode!");

            RoadNodes.Add(node);
        }
    }

    private void ReadMusicRegions(BinaryReader reader, int mapVersion)
    {
        var musicRegionCount = reader.ReadInt32();
        for (var i = 0; i < musicRegionCount; ++i)
        {
            _ = reader.ReadInt32();

            MusicTriggers.Add(Structures.Music.Read(reader, mapVersion));
        }
    }

    private void ReadMiniCatalog(BinaryReader reader)
    {
        if (MapVersion >= 49)
        {
            if (MapVersion < 52)
            {
                // TODO: do we need this?
            }
            else
            {
                // load external catalog
            }
        }
    }
}
