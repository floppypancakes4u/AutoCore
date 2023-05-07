namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public class OutpostTemplate : ObjectTemplate
{
    public Vector4 Location { get; set; }
    public float Unk1 { get; set; }
    public string Name { get; set; }
    public float XPScalar { get; set; }
    public float CaptureRadius { get; set; }
    public int VarTotalBeacons { get; set; }
    public bool IsOutpost { get; set; }
    public List<OutpostInformation> OutpostInformations { get; } = new();

    public override void Read(BinaryReader reader, int mapVersion)
    {
        Location = Vector4.ReadNew(reader);
        Unk1 = reader.ReadSingle();
        Name = reader.ReadUTF8StringOn(65);
        XPScalar = reader.ReadSingle();

        if (mapVersion >= 56)
        {
            CaptureRadius = reader.ReadSingle();
            VarTotalBeacons = reader.ReadInt32();
        }

        if (mapVersion >= 59)
            IsOutpost = reader.ReadBoolean();

        for (var i = 0; i < (mapVersion < 57 ? 2 : 4); ++i)
            OutpostInformations.Add(OutpostInformation.Read(reader, mapVersion));
    }

    public override ClonedObjectBase Create()
    {
        return null;
    }

    public class OutpostInformation
    {
        public uint BeaconVar { get; set; }
        public List<long> Objects { get; } = new();
        public List<OutpostSkill> OutpostSkills { get; } = new();
        public List<long> Reactions { get; } = new();
        public List<long> Spawns { get; } = new();

        public static OutpostInformation Read(BinaryReader reader, int mapVersion)
        {
            var ouputInformation = new OutpostInformation
            {
                BeaconVar = reader.ReadUInt32()
            };

            var spawnCount = reader.ReadInt32();
            for (var i = 0; i < spawnCount; ++i)
                ouputInformation.Spawns.Add(reader.ReadInt64());

            var objectCount = reader.ReadInt32();
            for (var i = 0; i < objectCount; ++i)
                ouputInformation.Objects.Add(reader.ReadInt64());

            var skillCount = reader.ReadInt32();
            for (var i = 0; i < skillCount; ++i)
                ouputInformation.OutpostSkills.Add(OutpostSkill.Read(reader));

            if (mapVersion >= 58)
            {
                var reactionCount = reader.ReadInt32();
                for (var i = 0; i < reactionCount; ++i)
                    ouputInformation.Reactions.Add(reader.ReadInt64());
            }

            return ouputInformation;
        }
    }

    public class OutpostSkill
    {
        public int SkillId { get; set; }
        public int SkillLevel { get; set; }
        public float RequiredBeaconPercantage { get; set; }
        public bool Player { get; set; }

        public static OutpostSkill Read(BinaryReader reader)
        {
            var os = new OutpostSkill
            {
                SkillId = reader.ReadInt32(),
                SkillLevel = reader.ReadInt32(),
                RequiredBeaconPercantage = reader.ReadSingle(),
                Player = reader.ReadBoolean()
            };

            reader.BaseStream.Position += 3;

            return os;
        }
    }
}
