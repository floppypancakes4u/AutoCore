namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Client→server request for full create payloads of missing objects (opcode 0x2011).
/// Ghidra FUN_0091da70: after opcode = u8 count + 3 pad + count × TFID16
/// (size = count×0x10 + 8 including opcode). Batches up to 255 per send.
/// </summary>
public class RequestObjectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.RequestObject;

    /// <summary>World TFIDs the client could not resolve locally.</summary>
    public List<TFID> Objects { get; } = new();

    public override void Read(BinaryReader reader)
    {
        Objects.Clear();

        if (reader.BaseStream.Position >= reader.BaseStream.Length)
            return;

        var count = reader.ReadByte();
        if (reader.BaseStream.Position + 3 <= reader.BaseStream.Length)
            reader.BaseStream.Position += 3; // pad
        else
            return;

        for (var i = 0; i < count; i++)
        {
            if (reader.BaseStream.Position + 16 > reader.BaseStream.Length)
                break;

            Objects.Add(reader.ReadTFID());
        }
    }
}
