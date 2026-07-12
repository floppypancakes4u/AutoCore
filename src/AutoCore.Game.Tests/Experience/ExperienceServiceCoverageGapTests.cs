using System.Reflection;
using System.Runtime.CompilerServices;
using AutoCore.Database.World.Models;
// ContinentArea lives in AutoCore.Database.World.Models
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Entities;
using AutoCore.Game.Experience;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Experience.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GameMission = AutoCore.Game.Mission.Mission;
using GameMissionObjective = AutoCore.Game.Mission.MissionObjective;

namespace AutoCore.Game.Tests.Experience;

/// <summary>Targeted coverage for ExperienceService / KillXpAward branches still under 90%.</summary>
[TestClass]
public class ExperienceServiceCoverageGapTests
{
    private ExperienceService _svc = null!;
    private RecordingProgressPersistence _persist = null!;
    private readonly List<BasePacket> _sent = new();
    private readonly List<long> _registered = new();

    [TestInitialize]
    public void Init()
    {
        _svc = ExperienceService.Instance;
        _svc.ResetForTests();
        _persist = new RecordingProgressPersistence();
        _svc.Persistence = _persist;
        _svc.PersistOnGrant = true;
        _svc.SendPacketsOnGrant = false;
        _svc.ResolveThreshold = ExperienceService.DefaultRetailThreshold;
        _svc.ResolveLevelRow = level => new ExperienceLevel
        {
            Level = level,
            Experience = ExperienceService.DefaultRetailThreshold(level),
            SkillPoints = 1,
            AttributePoints = 2,
            ResearchPoints = 0
        };
        _svc.ResolveCreatureXp = ExperienceService.DefaultCreatureXp;
        _svc.ResolveQuestFrac = ExperienceService.DefaultQuestFrac;
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var coid in _registered)
            ObjectManager.Instance.Remove(coid);
        _registered.Clear();
        TNLConnection.TestPacketSink = null;
        _svc.ResetForTests();
        ClearWorldDbXpTables();
    }

    private static Character MakeCharacter(long coid, int xp = 0, byte level = 1)
    {
        var c = new Character();
        c.SetCoid(coid, true);
        c.AttachTestDataForTests($"Gap{coid}");
        c.SetExperience(xp);
        c.SetLevel(level);
        return c;
    }

    private Character MakeCharacterWithConnection(long coid, int xp = 0, byte level = 1)
    {
        var c = MakeCharacter(coid, xp, level);
        var conn = new TNLConnection();
        c.SetOwningConnection(conn);
        return c;
    }

    // --- Packet send paths (SendPacketsOnGrant + OwningConnection) ---

    [TestMethod]
    public void GiveXp_WithConnection_SendsGiveXpAndCharacterLevel()
    {
        _svc.SendPacketsOnGrant = true;
        var character = MakeCharacterWithConnection(7001, xp: 0, level: 1);
        _sent.Clear();

        var result = _svc.GiveXp(character, 50, XpSource.Kill);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, _sent.OfType<GiveXPPacket>().Count());
        Assert.AreEqual(1, _sent.OfType<CharacterLevelPacket>().Count());
        Assert.AreEqual(50, _sent.OfType<GiveXPPacket>().Single().Amount);
    }

    [TestMethod]
    public void GiveXp_LevelUpWithConnection_SendsLevelHint()
    {
        _svc.SendPacketsOnGrant = true;
        var character = MakeCharacterWithConnection(7002, xp: 990, level: 1);
        _sent.Clear();

        _svc.GiveXp(character, 20, XpSource.Admin);

        var give = _sent.OfType<GiveXPPacket>().Single();
        Assert.AreEqual((sbyte)2, give.LevelHint);
        Assert.AreEqual(2, _sent.OfType<CharacterLevelPacket>().Single().Level);
    }

    [TestMethod]
    public void SetExperienceAbsolute_WithConnection_SendsPackets()
    {
        _svc.SendPacketsOnGrant = true;
        var character = MakeCharacterWithConnection(7003, xp: 0, level: 1);
        _sent.Clear();

        _svc.SetExperienceAbsolute(character, 500);

        Assert.AreEqual(1, _sent.OfType<GiveXPPacket>().Count());
        Assert.AreEqual(500, _sent.OfType<GiveXPPacket>().Single().Amount);
        Assert.AreEqual(1, _sent.OfType<CharacterLevelPacket>().Count());
    }

    [TestMethod]
    public void SendLoginProgressToClient_WithXp_SendsGiveXpSeedThenCharacterLevel()
    {
        var character = MakeCharacterWithConnection(7004, xp: 2500, level: 2);
        _sent.Clear();

        _svc.SendLoginProgressToClient(character);

        Assert.AreEqual(1, _sent.OfType<GiveXPPacket>().Count());
        Assert.AreEqual(2500, _sent.OfType<GiveXPPacket>().Single().Amount);
        Assert.AreEqual((sbyte)-1, _sent.OfType<GiveXPPacket>().Single().LevelHint);
        Assert.AreEqual(1, _sent.OfType<CharacterLevelPacket>().Count());
        Assert.AreEqual(2500, _sent.OfType<CharacterLevelPacket>().Single().Experience);
    }

    [TestMethod]
    public void SendLoginProgressToClient_ZeroXp_OnlyCharacterLevel()
    {
        var character = MakeCharacterWithConnection(7005, xp: 0, level: 1);
        _sent.Clear();

        _svc.SendLoginProgressToClient(character);

        Assert.AreEqual(0, _sent.OfType<GiveXPPacket>().Count());
        Assert.AreEqual(1, _sent.OfType<CharacterLevelPacket>().Count());
    }

    // --- Formula edge branches ---

    [TestMethod]
    public void ComputeMissionXp_NegativeLevelSpan_ReturnsZero()
    {
        // Inverted thresholds → LevelSpan clamps to 0
        _svc.ResolveThreshold = level => level switch
        {
            1 => 5000u,
            2 => 1000u, // less than previous
            _ => 0u
        };
        var mission = GameMission.CreateForTests(70);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 70);
        typeof(GameMissionObjective).GetProperty(nameof(GameMissionObjective.XPIndex))!
            .SetValue(objective, (short)5);
        typeof(GameMissionObjective).GetProperty(nameof(GameMissionObjective.XPScaler))!
            .SetValue(objective, 1f);
        typeof(GameMissionObjective).GetProperty(nameof(GameMissionObjective.XPBalanceScaler))!
            .SetValue(objective, 1f);

        Assert.AreEqual(0, _svc.ComputeMissionXp(mission, objective));
    }

    [TestMethod]
    public void DefaultRetailThreshold_AllSampleLevels()
    {
        for (byte l = 0; l <= 12; l++)
            Assert.IsTrue(ExperienceService.DefaultRetailThreshold(l) >= 0);
        Assert.AreEqual(5600u, ExperienceService.DefaultRetailThreshold(3));
        Assert.AreEqual(8800u, ExperienceService.DefaultRetailThreshold(4));
        Assert.AreEqual(16000u, ExperienceService.DefaultRetailThreshold(6));
        Assert.AreEqual(20000u, ExperienceService.DefaultRetailThreshold(7));
        Assert.AreEqual(26000u, ExperienceService.DefaultRetailThreshold(8));
        Assert.AreEqual(32000u, ExperienceService.DefaultRetailThreshold(9));
    }

    [TestMethod]
    public void DefaultCreatureXp_MidLevels()
    {
        Assert.AreEqual(40, ExperienceService.DefaultCreatureXp(2));
        Assert.AreEqual(41, ExperienceService.DefaultCreatureXp(3));
        Assert.AreEqual(42, ExperienceService.DefaultCreatureXp(4));
        Assert.AreEqual(45, ExperienceService.DefaultCreatureXp(5));
        Assert.AreEqual(50, ExperienceService.DefaultCreatureXp(6));
        Assert.AreEqual(54, ExperienceService.DefaultCreatureXp(7));
    }

    // --- AssetManager-backed resolvers (inject WorldDBLoader tables) ---

    [TestMethod]
    public void GetThreshold_FromAssetManager_WhenInjected()
    {
        SeedWorldDbXpTables(
            experience: new Dictionary<byte, ExperienceLevel>
            {
                [1] = new ExperienceLevel { Level = 1, Experience = 1111 }
            },
            creature: new Dictionary<int, int> { [1] = 77 },
            quest: new Dictionary<int, float> { [3] = 0.42f },
            areas: new Dictionary<Tuple<int, byte>, ContinentArea>
            {
                [Tuple.Create(10, (byte)2)] = new ContinentArea { ContinentObjectId = 10, Area = 2, XPLevel = 1 }
            });

        _svc.ResolveThreshold = null;
        _svc.ResolveCreatureXp = null;
        _svc.ResolveQuestFrac = null;
        _svc.ResolveAreaXpLevel = null;
        _svc.ResolveLevelRow = null;

        Assert.AreEqual(1111u, _svc.GetThreshold(1));
        Assert.AreEqual(77, _svc.GetCreatureXp(1));
        Assert.AreEqual(0.42f, _svc.GetQuestFrac(3), 0.0001f);
        Assert.AreEqual(1, _svc.GetAreaXpLevel(10, 2));
        Assert.AreEqual(77, _svc.ComputeAreaXp(10, 2));

        var row = _svc.GetType()
            .GetMethod("GetLevelRow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_svc, new object[] { (byte)1 }) as ExperienceLevel;
        Assert.IsNotNull(row);
        Assert.AreEqual(1111u, row.Experience);
    }

    // --- KillXpAward gap coverage ---

    [TestMethod]
    public void KillXpAward_GetSuperCharacterThrows_Swallows()
    {
        var boom = new BoomCharacter();
        boom.SetCoid(7100, true);
        boom.AttachTestDataForTests("Boom");
        Assert.IsTrue(ObjectManager.Instance.Add(boom));
        _registered.Add(7100);

        var victim = new Creature { Level = 1 };
        victim.SetCoid(7101, true);
        victim.SetMurderer(new TFID(7100, true));

        KillXpAward.TryAward(victim); // must not throw
        Assert.AreEqual(0, _persist.Saves.Count);
    }

    [TestMethod]
    public void KillXpAward_ComputeThrows_LogsAndContinues()
    {
        var killer = MakeCharacter(7200, xp: 0, level: 1);
        Assert.IsTrue(ObjectManager.Instance.Add(killer));
        _registered.Add(7200);

        _svc.ResolveCreatureXp = _ => throw new InvalidOperationException("table missing");

        var victim = new Creature { Level = 1 };
        victim.SetCoid(7201, true);
        victim.SetMurderer(new TFID(7200, true));

        KillXpAward.TryAward(victim);
        Assert.AreEqual(0, killer.Experience);
    }

    [TestMethod]
    public void KillXpAward_XpPercentFromCloneBase_ScalesGrant()
    {
        var killer = MakeCharacter(7300, xp: 0, level: 1);
        Assert.IsTrue(ObjectManager.Instance.Add(killer));
        _registered.Add(7300);

        var victim = new Creature { Level = 1 };
        victim.SetCoid(7301, true);
        victim.SetMurderer(new TFID(7300, true));

        var cb = (CloneBaseCreature)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseCreature));
        cb.CreatureSpecific = new CreatureSpecific { XPPercent = 0.5f };
        victim.AssignCloneBaseForTests(cb);

        KillXpAward.TryAward(victim);

        // Base 39 * 0.5 → ceil 20 (or 19 depending on ceiling)
        Assert.IsTrue(killer.Experience > 0 && killer.Experience < 39, $"got {killer.Experience}");
    }

    [TestMethod]
    public void KillXpAward_XpPercentZero_FallsBackToFull()
    {
        var killer = MakeCharacter(7400, xp: 0, level: 1);
        Assert.IsTrue(ObjectManager.Instance.Add(killer));
        _registered.Add(7400);

        var victim = new Creature { Level = 1 };
        victim.SetCoid(7401, true);
        victim.SetMurderer(new TFID(7400, true));

        var cb = (CloneBaseCreature)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseCreature));
        cb.CreatureSpecific = new CreatureSpecific { XPPercent = 0f };
        victim.AssignCloneBaseForTests(cb);

        KillXpAward.TryAward(victim);
        Assert.AreEqual(39, killer.Experience);
    }

    private sealed class BoomCharacter : Character
    {
        public override Character GetSuperCharacter(bool includeSummons) =>
            throw new InvalidOperationException("boom");
    }

    private static void SeedWorldDbXpTables(
        IDictionary<byte, ExperienceLevel> experience,
        IDictionary<int, int> creature,
        IDictionary<int, float> quest,
        IDictionary<Tuple<int, byte>, ContinentArea> areas)
    {
        var loader = GetWorldDbLoader();
        loader.ExperienceLevels = experience;
        loader.CreatureExperienceLevels = creature;
        loader.QuestXpLookup = quest;
        loader.ContinentAreas = areas;
    }

    private static void ClearWorldDbXpTables()
    {
        var loader = GetWorldDbLoader();
        loader.ExperienceLevels = null;
        loader.CreatureExperienceLevels = null;
        loader.QuestXpLookup = null;
        loader.ContinentAreas = null;
    }

    private static WorldDBLoader GetWorldDbLoader()
    {
        var prop = typeof(AssetManager).GetProperty(
            "WorldDBLoader",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(prop);
        return (WorldDBLoader)prop!.GetValue(AssetManager.Instance)!;
    }
}
