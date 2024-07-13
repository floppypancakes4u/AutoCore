namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class CreateArmorPacket : CreateSimpleObjectPacket
{
    public override GameOpcode Opcode => GameOpcode.CreateArmor;

    public ArmorSpecific ArmorSpecific { get; set; }
    public float Mass { get; set; }
    public string Name { get; set; }
    public short VarianceDefensiveBonus { get; set; }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);

        ArmorSpecific.Write(writer);

        writer.Write(Mass);
        writer.WriteUtf8StringOn(Name, 100);
        writer.Write(VarianceDefensiveBonus);

        writer.BaseStream.Position += 2;
    }

    public new static void WriteEmptyPacket(BinaryWriter writer)
    {
        CreateSimpleObjectPacket.WriteEmptyPacket(writer);

        writer.BaseStream.Position += 128;
    }
}
