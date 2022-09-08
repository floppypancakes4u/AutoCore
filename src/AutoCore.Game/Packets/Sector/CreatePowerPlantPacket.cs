namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class CreatePowerPlantPacket : CreateSimpleObjectPacket
{
    public override GameOpcode Opcode => GameOpcode.CreatePowerPlant;

    public PowerPlantSpecific PowerPlantSpecific { get; set; }
    public float Mass { get; set; }
    public float SkillCooldown { get; set; }
    public string Name { get; set; }

    public override void Read(BinaryReader reader)
    {
        throw new NotImplementedException();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);

        PowerPlantSpecific.Write(writer);

        writer.Write(Mass);
        writer.WriteUtf8StringOn(Name, 100);
        writer.Write(SkillCooldown);
    }

    public new static void WriteEmptyPacket(BinaryWriter writer)
    {
        CreateSimpleObjectPacket.WriteEmptyPacket(writer);

        writer.BaseStream.Position += 120;
    }
}
