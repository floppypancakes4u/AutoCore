namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
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

        writer.BaseStream.Position += 3;
    }
}
