namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class MapInfoPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.MapInfo;

    public int RegionId { get; set; }
    public TilesetType RegionType { get; set; }
    public byte RegionLevel { get; set; }
    public int LayerId { get; set; }
    public int ObjectiveIndex { get; set; }
    public string MapName { get; set; }
    public bool IsTown { get; set; }
    public bool IsArena { get; set; }
    public int OwningFaction { get; set; }
    public int ContinentObjectId { get; set; }
    public bool IsPersistent { get; set; }
    public int MapIterationVersion { get; set; }
    public int ContestedMissionId { get; set; }
    public long Coid { get; set; }

    public int TemporalRandomSeed { get; set; }
    public long CoidMap { get; set; }
    public short NumModulePlacements { get; set; }
    public List<MapInfoModulePlacement> ModulePlacements { get; } = new();
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public short WeatherUpdateSize { get; set; }

    // Form A skipOpcode bitstream. Layout matches client FUN_00637990 (Pass 2/16).
    public override void Read(BinaryReader reader)
    {
        RegionId = reader.ReadInt32();
        RegionType = (TilesetType)reader.ReadInt32();
        RegionLevel = reader.ReadByte();

        reader.BaseStream.Position += 3;

        LayerId = reader.ReadInt32();
        ObjectiveIndex = reader.ReadInt32();
        MapName = reader.ReadUTF8StringOn(65);
        IsTown = reader.ReadBoolean();
        IsArena = reader.ReadBoolean();

        reader.BaseStream.Position += 1;

        OwningFaction = reader.ReadInt32();
        ContinentObjectId = reader.ReadInt32();
        IsPersistent = reader.ReadBoolean();

        reader.BaseStream.Position += 3;

        MapIterationVersion = reader.ReadInt32();
        ContestedMissionId = reader.ReadInt32();

        reader.BaseStream.Position += 4;

        Coid = reader.ReadInt64();

        TemporalRandomSeed = reader.ReadInt32();
        CoidMap = reader.ReadInt64();
        NumModulePlacements = reader.ReadInt16();

        ModulePlacements.Clear();
        for (var i = 0; i < NumModulePlacements; ++i)
        {
            ModulePlacements.Add(new MapInfoModulePlacement
            {
                PlacementCoidLow = reader.ReadInt32(),
                PlacementCoidHigh = reader.ReadInt32(),
                RebaseCoidLow = reader.ReadInt32(),
                RebaseCoidHigh = reader.ReadInt32(),
                ModuleId = reader.ReadInt32(),
                Unknown14 = reader.ReadInt32(),
            });
        }

        PositionX = reader.ReadSingle();
        PositionY = reader.ReadSingle();
        PositionZ = reader.ReadSingle();

        WeatherUpdateSize = reader.ReadInt16();

        reader.BaseStream.Position += WeatherUpdateSize;
    }

    // Form A skipOpcode bitstream. Layout matches client FUN_00637990 (Pass 2/16).
    public override void Write(BinaryWriter writer)
    {
        writer.Write(RegionId);
        writer.Write((int)RegionType);
        writer.Write(RegionLevel);

        writer.BaseStream.Position += 3;

        writer.Write(LayerId);
        writer.Write(ObjectiveIndex);
        writer.WriteUtf8StringOn(MapName, 65);
        writer.Write(IsTown);
        writer.Write(IsArena);

        writer.BaseStream.Position += 1;

        writer.Write(OwningFaction);
        writer.Write(ContinentObjectId);
        writer.Write(IsPersistent);

        writer.BaseStream.Position += 3;

        writer.Write(MapIterationVersion);
        writer.Write(ContestedMissionId);

        writer.BaseStream.Position += 4;

        writer.Write(Coid);

        writer.Write(TemporalRandomSeed);
        writer.Write(CoidMap);
        NumModulePlacements = (short)ModulePlacements.Count;
        writer.Write(NumModulePlacements);

        foreach (var placement in ModulePlacements)
        {
            writer.Write(placement.PlacementCoidLow);
            writer.Write(placement.PlacementCoidHigh);
            writer.Write(placement.RebaseCoidLow);
            writer.Write(placement.RebaseCoidHigh);
            writer.Write(placement.ModuleId);
            writer.Write(placement.Unknown14);
        }

        writer.Write(PositionX);
        writer.Write(PositionY);
        writer.Write(PositionZ);

        writer.Write(WeatherUpdateSize);

        writer.BaseStream.Position += WeatherUpdateSize;
    }
}
