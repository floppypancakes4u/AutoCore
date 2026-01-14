namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

// NOTE: This packet's wire format is not fully reverse-engineered yet.
// We are using it experimentally to discover what the client expects for floating combat text.
public class DamagePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.Damage;

    public TFID Target { get; set; } = new();
    public TFID Source { get; set; } = new();
    public int Damage { get; set; }
    public byte Flags { get; set; } = 0;
    public byte DamageType { get; set; } = 0;

    public override void Write(BinaryWriter writer)
    {
        // Many sector packets start with 4 reserved bytes.
        writer.BaseStream.Position += 4;

        writer.WriteTFID(Target);
        writer.WriteTFID(Source);

        writer.Write(Damage);
        writer.Write(DamageType);
        writer.Write(Flags);

        // Align to 4 bytes (common in other packets)
        writer.BaseStream.Position += 2;
    }
}






