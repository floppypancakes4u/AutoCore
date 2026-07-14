namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// C2S StoreTransactionRequest (0x2027).
/// Client build (autoassault 0x00860be2 / 0x00860cbd): stack packet size 0x40 including opcode.
/// Layout (absolute, with opcode):
///   +0x00 opcode i32
///   +0x18 TFID 16B (selected item — Coid often CBID for store catalog goods)
///   +0x38 byte IsBuy (1=buy from store, 0=sell to store)
///   +0x3c i32 Quantity
/// </summary>
public sealed class StoreTransactionRequestPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.StoreTransactionRequest;

    /// <summary>Full buffer including opcode for debug dumps.</summary>
    public byte[] RawBytes { get; private set; } = [];

    public TFID Item { get; set; } = new();
    public bool IsBuy { get; set; }
    public int Quantity { get; set; } = 1;

    public override void Read(BinaryReader reader)
    {
        // HandlePacket already consumed opcode; rewind 4 to capture full frame when possible.
        var stream = reader.BaseStream;
        var afterOpcode = stream.Position;
        var start = Math.Max(0, afterOpcode - sizeof(uint));
        var remaining = Math.Max(0, stream.Length - start);
        stream.Position = start;
        RawBytes = reader.ReadBytes((int)remaining);

        // Prefer absolute layout including opcode (matches client stack packet).
        if (RawBytes.Length >= 0x40)
        {
            Item = ReadTfidAt(RawBytes, 0x18);
            IsBuy = RawBytes[0x38] != 0;
            Quantity = Math.Max(1, BitConverter.ToInt32(RawBytes, 0x3c));
            return;
        }

        // Body-only (opcode already consumed): offsets shifted by -4.
        if (RawBytes.Length >= 0x3c)
        {
            // If first dword is opcode, treat as full frame with short length.
            var maybeOpcode = BitConverter.ToInt32(RawBytes, 0);
            if (maybeOpcode == (int)GameOpcode.StoreTransactionRequest && RawBytes.Length >= 0x3c)
            {
                Item = RawBytes.Length >= 0x28 ? ReadTfidAt(RawBytes, 0x18) : new TFID();
                if (RawBytes.Length > 0x38)
                    IsBuy = RawBytes[0x38] != 0;
                if (RawBytes.Length >= 0x40)
                    Quantity = Math.Max(1, BitConverter.ToInt32(RawBytes, 0x3c));
                return;
            }

            Item = ReadTfidAt(RawBytes, 0x14);
            if (RawBytes.Length > 0x34)
                IsBuy = RawBytes[0x34] != 0;
            if (RawBytes.Length >= 0x3c)
                Quantity = Math.Max(1, BitConverter.ToInt32(RawBytes, 0x38));
        }
    }

    static TFID ReadTfidAt(byte[] raw, int offset)
    {
        if (raw == null || offset + 16 > raw.Length)
            return new TFID();

        using var ms = new MemoryStream(raw, offset, 16, writable: false);
        using var br = new BinaryReader(ms);
        return br.ReadTFID();
    }
}
