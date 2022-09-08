namespace AutoCore.Game.Mission;

using AutoCore.Utils.Extensions;

public class MissionString
{
    public int OwnerId { get; private set; }
    public int StringId { get; private set; }
    public string Text { get; private set; }
    public byte Type { get; private set; }

    public static MissionString Read(BinaryReader br, int mapVersion)
    {
        return new MissionString
        {
            StringId = br.ReadInt32(),
            OwnerId = br.ReadInt32(),
            Type = mapVersion >= 18 ? br.ReadByte() : (byte)0,
            Text = br.ReadLengthedString()
        };
    }
}
