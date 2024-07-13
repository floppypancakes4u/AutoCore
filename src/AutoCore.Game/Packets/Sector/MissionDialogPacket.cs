namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

public class MissionDialogPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.MissionDialog;

    public TFID Creature { get; set; }
    private List<MissionInfo> Missions { get; } = new();

    public bool AddMission(MissionInfo info)
    {
        if (Missions.Count == 8)
            return false;

        Missions.Add(info);

        return true;
    }

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;

        writer.WriteTFID(Creature);
        writer.Write((byte)(Missions.Count & 0xFF));

        writer.BaseStream.Position += 7;

        foreach (var mission in Missions)
            mission.Write(writer);
    }

    public class MissionInfo
    {
        public int Id { get; set; }
        public long[] PossibleItemCoids { get; set; } = new long[4];

        public void Write(BinaryWriter writer)
        {
            writer.Write(Id);

            writer.BaseStream.Position += 4;

            for (var i = 0; i < 4; ++i)
                writer.Write(PossibleItemCoids[i]);
        }
    }
}
