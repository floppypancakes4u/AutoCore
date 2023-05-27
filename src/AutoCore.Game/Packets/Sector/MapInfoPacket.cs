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
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public short WeatherUpdateSize { get; set; }

    // NOTE: this is not the real layout of the packing, but it is manually packed!
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

        for (var i = 0; i < NumModulePlacements; ++i)
            reader.BaseStream.Position += 24;

        PositionX = reader.ReadSingle();
        PositionY = reader.ReadSingle();
        PositionZ = reader.ReadSingle();

        WeatherUpdateSize = reader.ReadInt16();

        reader.BaseStream.Position += WeatherUpdateSize;
    }

    // NOTE: this is not the real layout of the packing, but it is manually packed!
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
        writer.Write(NumModulePlacements);

        for (var i = 0; i < NumModulePlacements; ++i)
            writer.BaseStream.Position += 24;

        writer.Write(PositionX);
        writer.Write(PositionY);
        writer.Write(PositionZ);

        writer.Write(WeatherUpdateSize);

        writer.BaseStream.Position += WeatherUpdateSize;
    }
}
