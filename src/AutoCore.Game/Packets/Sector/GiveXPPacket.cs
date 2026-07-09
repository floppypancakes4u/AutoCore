namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// EMSG_Sector_GiveXP (0x205F). Client handler FUN_0080AE70 applies XP and queues a
/// type-3 combat floater (purple) on the local player vehicle.
///
/// Wire (opcode written by SendGamePacket at +0x00):
///   +0x04 amount (int32)
///   +0x08 levelHint (sbyte, -1 = none / no level flash)
/// </summary>
public class GiveXPPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.GiveXP;

    public int Amount { get; set; }

    /// <summary>Optional level-related byte; pass -1 when not leveling.</summary>
    public sbyte LevelHint { get; set; } = -1;

    public override void Write(BinaryWriter writer)
    {
        writer.Write(Amount);
        writer.Write(LevelHint);
    }
}
