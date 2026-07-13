namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// C2S spend one attribute point (0x205A).
/// Wire body after opcode: uint32 attribute mask (retail UI @ 0x008F92E0).
/// </summary>
public sealed class AttributeIncrementPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.AttributeIncrement;

    public uint AttributeMask { get; private set; }

    public CharacterAttributeKind Attribute => CharacterAttributeMasks.FromMask(AttributeMask);

    public override void Read(BinaryReader reader)
    {
        if (reader.BaseStream.Length - reader.BaseStream.Position < 4)
        {
            AttributeMask = 0;
            return;
        }

        AttributeMask = reader.ReadUInt32();
    }
}
