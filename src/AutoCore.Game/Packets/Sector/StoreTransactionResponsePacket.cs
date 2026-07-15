namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// S2C StoreTransactionResponse (0x2028).
/// Client receive: FUN_00810670 via Client_PacketDispatch case 0x2028.
/// Absolute layout (size 0x30 including opcode):
///   +0x00 opcode
///   +0x04 pad / buy helper dword
///   +0x08 item COID i64 (sold item, or buy grant item)
///   +0x10 related COID i64 (buy path)
///   +0x18 related COID i64 (buy path)
///   +0x20 credits i64 (absolute balance written to character+0x720)
///   +0x28 bWasSuccessful
///   +0x29 bIsBuy (1=buy, 0=sell)
///   +0x2c quantity i32 (also used as non-zero destroy flag)
/// Sell success: resolve item@+0x08, set credits, destroy cargo, store UI refresh (type 4);
/// if destroy misses (cursor-only), client falls through to FUN_007fc150 (clear hand).
/// </summary>
public sealed class StoreTransactionResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.StoreTransactionResponse;

    public long ItemCoid { get; set; }
    public long RelatedCoidA { get; set; }
    public long RelatedCoidB { get; set; }
    public long Credits { get; set; }
    public bool WasSuccessful { get; set; }
    public bool IsBuy { get; set; }
    public int Quantity { get; set; } = 1;

    public override void Write(BinaryWriter writer)
    {
        // Opcode already written by SendGamePacket; absolute +0x04 pad.
        writer.BaseStream.Position += 4;
        writer.Write(ItemCoid);
        writer.Write(RelatedCoidA);
        writer.Write(RelatedCoidB);
        writer.Write(Credits);
        writer.Write(WasSuccessful);
        writer.Write(IsBuy);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(Quantity);
    }
}
