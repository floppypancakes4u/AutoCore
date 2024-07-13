namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class ClanUpdatePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.ClanUpdate;

    public int ClanId { get; set; }
    public string ClanName { get; set; }
    public string ClanMOTD { get; set; }
    public string RankOne { get; set; }
    public string RankTwo { get; set; }
    public string RankThree { get; set; }
    public int MonthlyDues { get; set; }
    public int MonthlyUpkeep { get; set; }
    public long ClanOwnerCoid { get; set; }
    public int NumMembers { get; set; }

    public override void Read(BinaryReader reader)
    {
        ClanId = reader.ReadInt32();
        ClanName = reader.ReadUTF8StringOn(51);
        ClanMOTD = reader.ReadUTF8StringOn(251);
        RankOne = reader.ReadUTF8StringOn(51);
        RankTwo = reader.ReadUTF8StringOn(51);
        RankThree = reader.ReadUTF8StringOn(51);

        reader.BaseStream.Position += 1;

        MonthlyDues = reader.ReadInt32();
        MonthlyUpkeep = reader.ReadInt32();
        ClanOwnerCoid = reader.ReadInt64();
        NumMembers = reader.ReadInt32();

        reader.BaseStream.Position += 4;
    }
}
