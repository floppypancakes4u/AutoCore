namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;

/// <summary>
/// Server → client map exploration update (opcode <see cref="GameOpcode.UnlockRegion"/> = 0x205B).
/// Client handler: <c>Client_RecvUnlockRegion</c> (0x00809550). Total size 0x10 including opcode.
/// <c>UnlockFlag == 0</c> relocks the continent; non-zero applies <see cref="ExploredBits"/>.
/// If the client has no continent entry yet, the first packet only creates an empty entry — send twice.
/// </summary>
public class UnlockRegionPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.UnlockRegion;

    /// <summary>Fixed packet size including opcode.</summary>
    public const int PacketSizeIncludingOpcode = 0x10;

    public int ContinentId { get; set; }

    /// <summary>0 = relock continent; non-zero = unlock/update explored bits.</summary>
    public byte UnlockFlag { get; set; } = 1;

    /// <summary>Full explored bitmask after the update (bit N-1 for area id N, 1..32).</summary>
    public uint ExploredBits { get; set; }

    public override void Write(BinaryWriter writer)
    {
        // Opcode written by SendGamePacket.
        writer.Write(ContinentId);
        writer.Write(UnlockFlag);
        writer.WriteZeros(3);
        writer.Write(ExploredBits);
    }
}
