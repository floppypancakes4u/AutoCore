namespace AutoCore.Game.Packets.Sector;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Server → client local player create (opcode <see cref="GameOpcode.CreateCharacterExtended"/> = 0x2016).
/// Fixed body size is 0x1358 bytes including opcode when variable tails are empty.
/// First-time tips/hints flags are four uint32s at absolute packet offset <c>0x8EC</c>
/// (client FUN_00534bd0 → character object +0xD30).
/// Continent slots are 50 × 12 bytes (int id + byte flag + 3 pad + uint explored bits).
/// </summary>
public class CreateCharacterExtendedPacket : CreateCharacterPacket
{
    /// <summary>Absolute offset of FirstTimeFlags[0] from start of packet (including opcode).</summary>
    public const int FirstTimeFlagsPacketOffset = 0x8EC;

    /// <summary>Fixed CreateCharacterExtended size including opcode when all variable counts are 0.</summary>
    public const int FixedPacketSizeIncludingOpcode = 0x1358;

    public override GameOpcode Opcode => GameOpcode.CreateCharacterExtended;

    public int NumCompletedQuests { get; set; }
    public int NumCurrentQuests { get; set; }
    public List<CharacterQuest> CurrentQuests { get; set; } = new();
    public short NumAchievements { get; set; }
    public short NumDisciplines { get; set; }
    public byte NumSkills { get; set; }
    public CharacterExploration[] ContinentUnlocked { get; set; } = new CharacterExploration[50];
    public long[] QuickBarItemCoids { get; set; } = new long[100];
    public int[] QuickBarSkills { get; set; } = new int[100];
    public long Credits { get; set; }
    public long CreditDebt { get; set; }
    public int XP { get; set; }
    public short CurrentMana { get; set; }
    public short AttributePoints { get; set; }
    public short AttributeTech { get; set; }
    public short AttributeCombat { get; set; }
    public short AttributeTheory { get; set; }
    public short AttributePerception { get; set; }
    public short DisciplinePoints { get; set; }
    public short SkillPoints { get; set; }
    public byte ReverseEngineeringRank { get; set; }
    public byte ExperimentingRank { get; set; }
    public byte MemorizationRank { get; set; }
    public byte GadgetingRank { get; set; }
    public uint[] FirstTimeFlags { get; set; } = new uint[4];
    public long[] MemorizedList { get; set; } = new long[8];
    public int[] ArenaRanks { get; set; } = new int[7];
    public long[] InventoryCoids { get; set; } = new long[312];
    public float KMTravelled { get; set; }
    public float HazardModeCount { get; set; }
    public int RespecsBought { get; set; }
    public long LastRespecTime { get; set; }
    public int FreeRespecs { get; set; }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);

        writer.Write(NumCompletedQuests);
        writer.Write(NumCurrentQuests);
        writer.Write(NumAchievements);
        writer.Write(NumDisciplines);
        writer.Write(NumSkills);

        writer.WriteZeros(3);

        // 50 × 12-byte continent entries (client zeros 0x96 dwords at 0x1B8).
        for (var i = 0; i < 50; ++i)
        {
            if (ContinentUnlocked[i] == null)
            {
                writer.Write(0);
                writer.WriteZeros(8);
                continue;
            }

            writer.Write(ContinentUnlocked[i].ContinentId);
            writer.Write((byte)1); // unlocked flag (client uses non-zero id as presence)
            writer.WriteZeros(3);
            writer.Write(ContinentUnlocked[i].ExploredBits);
        }

        for (var i = 0; i < 100; ++i)
            writer.Write(QuickBarItemCoids[i]);

        for (var i = 0; i < 100; ++i)
            writer.Write(QuickBarSkills[i]);

        writer.Write(Credits);
        writer.Write(CreditDebt);
        writer.Write(XP);
        writer.Write(CurrentMana);
        // Extended layout uses int16 HP here (base CreateSimpleObject.CurrentHealth is int32).
        writer.Write((short)CurrentHealth);
        writer.Write(AttributePoints);
        writer.Write(AttributeTech);
        writer.Write(AttributeCombat);
        writer.Write(AttributeTheory);
        writer.Write(AttributePerception);
        writer.Write(DisciplinePoints);
        writer.Write(SkillPoints);
        writer.Write(ReverseEngineeringRank);
        writer.Write(ExperimentingRank);
        writer.Write(MemorizationRank);
        writer.Write(GadgetingRank);

        writer.WriteZeros(2);

        // Absolute offset 0x8EC when opcode is written before this body (SendGamePacket).
        for (var i = 0; i < 4; ++i)
            writer.Write(FirstTimeFlags[i]);

        writer.WriteZeros(4);

        for (var i = 0; i < 8; ++i)
            writer.Write(MemorizedList[i]);

        for (var i = 0; i < 7; ++i)
            writer.Write(ArenaRanks[i]);

        writer.WriteZeros(4);

        for (var i = 0; i < 312; ++i)
            writer.Write(InventoryCoids[i]);

        writer.Write(KMTravelled);
        writer.Write(HazardModeCount);
        writer.Write(RespecsBought);

        writer.WriteZeros(4);

        writer.Write(LastRespecTime);
        writer.Write(FreeRespecs);

        writer.WriteZeros(28);

        if (NumSkills > 0)
        {
            // TODO: write skills ({skillid, skilllevel, 2B padding}[])
            writer.WriteZeros(8 * NumSkills);
        }

        if (NumCompletedQuests > 0)
        {
            // TODO: write completed quests (int id[])
            writer.WriteZeros(4 * NumCompletedQuests);
        }

        if (NumAchievements > 0)
        {
            // TODO: write achievements (int id[])
            writer.WriteZeros(4 * NumAchievements);
        }

        if (NumDisciplines > 0)
        {
            // TODO: write disciplines (int id[], int unk[], int unk[])
            writer.WriteZeros(12 * NumDisciplines);
        }

        if (NumCurrentQuests > 0)
        {
            // Write current quest objectives (SVOGCharacterObjective[] - 72 bytes each)
            foreach (var quest in CurrentQuests)
            {
                quest.Write(writer);
            }
        }
    }
}
