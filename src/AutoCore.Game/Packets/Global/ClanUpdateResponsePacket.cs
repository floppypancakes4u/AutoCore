namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

public enum ClanUpdateResponse
{
    Ok           = 0,
    ClanNotFound = 1,
    BadRanks     = 2,
    BadMOTD      = 3,
    BadDues      = 4
}

public class ClanUpdateResponsePacket(ClanUpdateResponse result) : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.ClanUpdateResponse;

    public ClanUpdateResponse Result { get; set; } = result;

    public override void Write(BinaryWriter writer) => writer.Write((int)Result);
}
