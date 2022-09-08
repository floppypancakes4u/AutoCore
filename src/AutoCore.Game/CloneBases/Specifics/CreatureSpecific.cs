namespace AutoCore.Game.CloneBases.Specifics;

using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public class CreatureSpecific
{
    public int AIBehavior;
    public short AttributeCombat;
    public short AttributePerception;
    public short AttributeTech;
    public short AttributeTheory;
    public short BaseLevel;
    public byte BaseLootChance;
    public byte BossType;
    public int Color1;
    public int Color2;
    public int Color3;
    public short DefensiveBonus;
    public short DifficultyAdjust;
    public byte Flags;
    public float FlyingHeight;
    public int HasTurret;
    public float HearingRange;
    public int IsNPC;
    public int LootTableId;
    public int NPCCanGamble;
    public string NPCIntro;
    public short OffensiveBonus;
    public float PhysicsScale;
    public float RotationSpeed;
    public Dictionary<byte, List<SkillSet>> Skills;
    public float Speed;
    public short TransformTime;
    public float VisionArc;
    public float VisionRange;
    public float XPPercent;

    public static CreatureSpecific ReadNew(BinaryReader reader)
    {
        var c = new CreatureSpecific
        {
            Speed = reader.ReadSingle(),
            VisionArc = reader.ReadSingle(),
            VisionRange = reader.ReadSingle(),
            HearingRange = reader.ReadSingle(),
            RotationSpeed = reader.ReadSingle(),
            PhysicsScale = reader.ReadSingle(),
            FlyingHeight = reader.ReadSingle(),
            AIBehavior = reader.ReadInt32(),
            IsNPC = reader.ReadInt32(),
            NPCCanGamble = reader.ReadInt32(),
            HasTurret = reader.ReadInt32(),
            TransformTime = reader.ReadInt16(),
            BaseLevel = reader.ReadInt16(),
            AttributeCombat = reader.ReadInt16(),
            AttributeTech = reader.ReadInt16(),
            AttributeTheory = reader.ReadInt16(),
            AttributePerception = reader.ReadInt16(),
            Flags = reader.ReadByte(),
            BossType = reader.ReadByte(),
            DifficultyAdjust = reader.ReadInt16(),
            BaseLootChance = reader.ReadByte(),
            Skills = new Dictionary<byte, List<SkillSet>>()
        };

        reader.ReadBytes(3);

        c.XPPercent = reader.ReadSingle();
        c.Color1 = reader.ReadInt32();
        c.Color2 = reader.ReadInt32();
        c.Color3 = reader.ReadInt32();
        c.OffensiveBonus = reader.ReadInt16();
        c.DefensiveBonus = reader.ReadInt16();
        c.LootTableId = reader.ReadInt32();

        var asd = reader.ReadInt32();

        var introLen = reader.ReadInt32();
        c.NPCIntro = reader.ReadUTF16StringOn(introLen);

        var aiCount = reader.ReadInt32();
        for (var i = 0; i < aiCount; ++i)
        {
            var b = reader.ReadByte();

            c.Skills.Add(b, new List<SkillSet>(reader.ReadInt32()));

            for (var j = 0; j < c.Skills[b].Capacity; ++j)
                c.Skills[b].Add(SkillSet.Read(reader));
        }

        return c;
    }
}
