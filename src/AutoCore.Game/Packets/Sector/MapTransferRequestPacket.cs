namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public enum MapTransferType
{
    ContinentObject = 0,
    Highway         = 1,
    Random          = 2,
    Mission         = 3,
    GMTest          = 4,
    RepairStation   = 5,
    Death           = 6,
    Warp            = 7,
    Arena           = 8
}

public class MapTransferRequestPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.MapTransferRequest;

    public MapTransferType Type { get; set; }
    public int Data { get; set; }
    public long OptionalMapCoid { get; set; }
    public bool PreferPVP { get; set; }
    public string GMParameter { get; set; }

    public override void Read(BinaryReader reader)
    {
        Type = (MapTransferType)reader.ReadInt32();
        Data = reader.ReadInt32();

        reader.BaseStream.Position += 4;

        OptionalMapCoid = reader.ReadInt64();
        PreferPVP = reader.ReadBoolean();
        GMParameter = reader.ReadUTF8StringOn(50);

        reader.BaseStream.Position += 5;
    }
}
