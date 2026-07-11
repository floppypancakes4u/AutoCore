namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Utils.Extensions;

public class CreateWheelSetPacket : CreateSimpleObjectPacket
{
    public override GameOpcode Opcode => GameOpcode.CreateWheelSet;

    public float FrictionGravel { get; set; }
    public float FrictionIce { get; set; }
    public float FrictionMud { get; set; }
    public float FrictionPaved { get; set; }
    public float FrictionPlains { get; set; }
    public float FrictionSand { get; set; }
    public bool IsDefault { get; set; }
    public string Name { get; set; }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);

        writer.Write(FrictionGravel);
        writer.Write(FrictionIce);
        writer.Write(FrictionMud);
        writer.Write(FrictionPaved);
        writer.Write(FrictionPlains);
        writer.Write(FrictionSand);
        writer.Write(IsDefault);
        writer.WriteUtf8StringOn(Name, 100);

        writer.WriteZeros(3);
    }

    /// <summary>
    /// Client fixed nested body is simple-object empty (212) + 128 wheelset-specific bytes
    /// (6×float friction + IsDefault + name[100] + pad3) so WheelSet→Armor stays 0x158.
    /// Without this pad, empty CreateWheelSet was 128 bytes short and desynced CreateVehicle.
    /// </summary>
    public new static void WriteEmptyPacket(BinaryWriter writer)
    {
        CreateSimpleObjectPacket.WriteEmptyPacket(writer);

        writer.WriteZeros(128);
    }
}
