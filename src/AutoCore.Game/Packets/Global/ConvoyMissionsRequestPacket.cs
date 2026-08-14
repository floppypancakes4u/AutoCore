namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

/// <summary>
/// Client → server request for convoy member mission IDs (GameOpcode 0x800F).
/// Retail struct is 4 bytes (opcode only). Any leftover bytes are drained by the handler.
/// </summary>
public class ConvoyMissionsRequestPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.ConvoyMissionsRequest;

    public override void Read(BinaryReader reader)
    {
        // Opcode-only on the wire. Handler drains leftover bytes if a client sends extra.
    }
}
