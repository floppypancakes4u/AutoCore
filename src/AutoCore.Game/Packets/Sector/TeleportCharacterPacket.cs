namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// S2C living-player pose snap (opcode <see cref="GameOpcode.TeleportCharacter"/> = 0x8058).
/// </summary>
/// <remarks>
/// Client <c>FUN_00808910</c> (dispatch case 0x8058): reads float4 at packet+0x10, applies
/// <c>Y += 2</c> and <c>Z -= 1</c>, then <c>CVOGReaction_TeleportTarget</c> on the local
/// character. This is the retail GM teleport channel — not SpecialEvent airlift and not
/// create-packet / ghost resync.
/// Layout (opcode written by <c>SendGamePacket</c>):
/// <code>
/// +0x00 opcode
/// +0x04 pad 12
/// +0x10 float X
/// +0x14 float Y  (wire = desiredY - 2)
/// +0x18 float Z  (wire = desiredZ + 1)
/// +0x1c float W  (0)
/// </code>
/// </remarks>
public class TeleportCharacterPacket : BasePacket
{
    /// <summary>Client adds this to wire Y before applying pose.</summary>
    public const float ClientYBias = 2.0f;

    /// <summary>Client subtracts this from wire Z before applying pose.</summary>
    public const float ClientZBias = 1.0f;

    public override GameOpcode Opcode => GameOpcode.TeleportCharacter;

    /// <summary>Desired world position after client bias is applied.</summary>
    public Vector3 Position { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.WriteZeros(12);
        writer.Write(Position.X);
        writer.Write(Position.Y - ClientYBias);
        writer.Write(Position.Z + ClientZBias);
        writer.Write(0f);
    }
}
