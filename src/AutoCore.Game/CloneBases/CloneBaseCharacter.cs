namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Structures;

public class CloneBaseCharacter : CloneBaseCreature
{
    public CharacterSpecific CharacterSpecific { get; set; }
    public List<HeadBody> HashBody { get; set; }
    public List<HeadDetail> HashEyes { get; set; }
    public List<HeadDetail> HashHair { get; set; }
    public List<HeadBody> HashHead { get; set; }
    public List<HeadDetail> HashHeadDetail1 { get; set; }
    public List<HeadDetail> HashHeadDetail2 { get; set; }
    public List<HeadDetail> HashHelmet { get; set; }
    public List<HeadDetail> HashMouthes { get; set; }

    public CloneBaseCharacter(BinaryReader reader)
        : base(reader)
    {
        CharacterSpecific = CharacterSpecific.ReadNew(reader);

        HashHead = new List<HeadBody>(reader.ReadInt32());
        for (var i = 0; i < HashHead.Capacity; ++i)
            HashHead.Add(HeadBody.ReadNew(reader));

        HashBody = new List<HeadBody>(reader.ReadInt32());
        for (var i = 0; i < HashBody.Capacity; ++i)
            HashBody.Add(HeadBody.ReadNew(reader));

        HashHeadDetail1 = new List<HeadDetail>(reader.ReadInt32());
        for (var i = 0; i < HashHeadDetail1.Capacity; ++i)
            HashHeadDetail1.Add(HeadDetail.ReadNew(reader));

        HashHeadDetail2 = new List<HeadDetail>(reader.ReadInt32());
        for (var i = 0; i < HashHeadDetail2.Capacity; ++i)
            HashHeadDetail2.Add(HeadDetail.ReadNew(reader));

        HashHair = new List<HeadDetail>(reader.ReadInt32());
        for (var i = 0; i < HashHair.Capacity; ++i)
            HashHair.Add(HeadDetail.ReadNew(reader));

        HashEyes = new List<HeadDetail>(reader.ReadInt32());
        for (var i = 0; i < HashEyes.Capacity; ++i)
            HashEyes.Add(HeadDetail.ReadNew(reader));

        HashHelmet = new List<HeadDetail>(reader.ReadInt32());
        for (var i = 0; i < HashHelmet.Capacity; ++i)
            HashHelmet.Add(HeadDetail.ReadNew(reader));

        HashMouthes = new List<HeadDetail>(reader.ReadInt32());
        for (var i = 0; i < HashMouthes.Capacity; ++i)
            HashMouthes.Add(HeadDetail.ReadNew(reader));
    }
}
