namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

/// <summary>
/// Client → server request for current convoy/mission list (GameOpcode 0x800F).
/// Payload is not yet reverse-engineered; currently treated as empty/ignored.
/// </summary>
public class ConvoyMissionsRequestPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.ConvoyMissionsRequest;

    public override void Read(BinaryReader reader)
    {
        // Unknown payload. Consume nothing for now.
    }
}








