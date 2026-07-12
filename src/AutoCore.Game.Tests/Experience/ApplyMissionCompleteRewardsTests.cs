using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Experience;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Experience.Fakes;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GameMission = AutoCore.Game.Mission.Mission;
using GameMissionObjective = AutoCore.Game.Mission.MissionObjective;

namespace AutoCore.Game.Tests.Experience;

[TestClass]
public class ApplyMissionCompleteRewardsTests
{
    private ExperienceService _svc = null!;
    private RecordingProgressPersistence _persist = null!;

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
        _svc.ResolveQuestFrac = ExperienceService.DefaultQuestFrac;
        _svc.ResolveQuestCreditsFrac = ExperienceService.DefaultQuestCreditsFrac;
        _svc.ResolveQuestBaseCredits = ExperienceService.DefaultQuestBaseCredits;
        _svc.ResolveLevelRow = level => new ExperienceLevel
        {
            Level = level,
            Experience = ExperienceService.DefaultRetailThreshold(level),
            SkillPoints = 1,
            AttributePoints = 2,
            ResearchPoints = 0
        };
    }

    [TestCleanup]
    public void Cleanup() => _svc.ResetForTests();

    private static Character MakeCharacter(long coid, int xp = 0, byte level = 1, InventoryManager inventory = null)
    {
        var c = new Character();
        c.SetCoid(coid, true);
        c.AttachTestDataForTests($"MissionXp{coid}");
        c.SetExperience(xp);
        c.SetLevel(level);
        if (inventory != null)
            c.AttachInventoryForTests(inventory);
        return c;
    }

    private static void SetObjectiveFields(
        GameMissionObjective objective,
        short xpIndex,
        float xpScaler = 1f,
        float balance = 1f,
        int skill = 0,
        int attrib = 0,
        int staticXp = 0,
        short creditsIndex = 0,
        float creditScaler = 1f,
        int staticCredits = 0)
    {
        var t = typeof(GameMissionObjective);
        t.GetProperty(nameof(GameMissionObjective.XPIndex))!.SetValue(objective, xpIndex);
        t.GetProperty(nameof(GameMissionObjective.XPScaler))!.SetValue(objective, xpScaler);
        t.GetProperty(nameof(GameMissionObjective.XPBalanceScaler))!.SetValue(objective, balance);
        t.GetProperty(nameof(GameMissionObjective.SkillPoints))!.SetValue(objective, skill);
        t.GetProperty(nameof(GameMissionObjective.AttribPoints))!.SetValue(objective, attrib);
        t.GetProperty(nameof(GameMissionObjective.XP))!.SetValue(objective, staticXp);
        t.GetProperty(nameof(GameMissionObjective.CreditsIndex))!.SetValue(objective, creditsIndex);
        t.GetProperty(nameof(GameMissionObjective.CreditScaler))!.SetValue(objective, creditScaler);
        t.GetProperty(nameof(GameMissionObjective.Credits))!.SetValue(objective, staticCredits);
    }

    [TestMethod]
    public void Apply_NullCharacter_NoThrow()
    {
        NpcInteractHandler.ApplyMissionCompleteRewards(null, null, null);
    }

    [TestMethod]
    public void Apply_MissingMissionAndObjective_NoXp()
    {
        var character = MakeCharacter(6001);
        NpcInteractHandler.ApplyMissionCompleteRewards(character, null, null);
        Assert.AreEqual(0, character.Experience);
        Assert.AreEqual(0, _persist.Saves.Count);
    }

    [TestMethod]
    public void Apply_MissionXp_PersistsAndGrants()
    {
        var mission = GameMission.CreateForTests(200);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 200);
        SetObjectiveFields(objective, xpIndex: 5);

        var character = MakeCharacter(6002);
        NpcInteractHandler.ApplyMissionCompleteRewards(
            character, mission, objective, source: "Test", notifyClient: true);

        Assert.AreEqual(320, character.Experience);
        Assert.AreEqual(1, _persist.Saves.Count);
        Assert.AreEqual(320, _persist.Saves[0].Progress.Experience);
    }

    [TestMethod]
    public void Apply_NotifyClientFalse_NoLevelUp_DoesNotBuildClientPacketsViaService()
    {
        // Service builds packets into GiveXpResult; with notify false and no level-up, packets null.
        // ApplyMissionCompleteRewards doesn't capture result packets onto connection here
        // (SendPacketsOnGrant=false). Assert memory+persist only.
        var mission = GameMission.CreateForTests(201);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 201);
        SetObjectiveFields(objective, xpIndex: 5);

        var character = MakeCharacter(6003, xp: 0, level: 1);
        NpcInteractHandler.ApplyMissionCompleteRewards(
            character, mission, objective, notifyClient: false);

        Assert.AreEqual(320, character.Experience);
        Assert.AreEqual(1, character.Level);
    }

    [TestMethod]
    public void Apply_NotifyClientFalse_WhenLevels_StillUpdatesLevelInMemory()
    {
        var mission = GameMission.CreateForTests(202);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 202);
        SetObjectiveFields(objective, xpIndex: 5); // 320 XP

        var character = MakeCharacter(6004, xp: 900, level: 1);
        NpcInteractHandler.ApplyMissionCompleteRewards(
            character, mission, objective, notifyClient: false);

        Assert.AreEqual(2, character.Level);
        Assert.AreEqual(1220, character.Experience);
    }

    [TestMethod]
    public void Apply_ZeroXp_StillGrantsSkillAndAttribPools_AndPersists()
    {
        var mission = GameMission.CreateForTests(203);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 203);
        SetObjectiveFields(objective, xpIndex: 0, skill: 3, attrib: 4, staticXp: 0);

        var character = MakeCharacter(6005, xp: 10, level: 1);
        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective);

        Assert.AreEqual(10, character.Experience, "no XP granted");
        Assert.AreEqual(3, character.SkillPoints);
        Assert.AreEqual(4, character.AttributePoints);
        Assert.AreEqual(1, _persist.Saves.Count, "pool-only path must persist via injectable Persistence");
        Assert.AreEqual(3, _persist.Saves[0].Progress.SkillPoints);
        Assert.AreEqual(4, _persist.Saves[0].Progress.AttributePoints);
        Assert.AreEqual(10, _persist.Saves[0].Progress.Experience);
    }

    [TestMethod]
    public void Apply_NullObjective_UsesMaxSequenceFromMission()
    {
        var obj1 = GameMissionObjective.CreateForTests(1, 0, 204);
        SetObjectiveFields(obj1, xpIndex: 1); // 2% of span(5)=3200 → 64
        var obj2 = GameMissionObjective.CreateForTests(2, 1, 204);
        SetObjectiveFields(obj2, xpIndex: 5); // 10% → 320

        var mission = GameMission.CreateForTests(204, obj1, obj2);
        mission.TargetLevel = 5;

        var character = MakeCharacter(6006);
        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective: null);

        Assert.AreEqual(320, character.Experience, "should use max-sequence objective (seq 1)");
    }

    [TestMethod]
    public void Apply_ZeroXp_NoPools_NoPersist()
    {
        var mission = GameMission.CreateForTests(205);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 205);
        SetObjectiveFields(objective, xpIndex: 0);

        var character = MakeCharacter(6007);
        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective);

        Assert.AreEqual(0, _persist.Saves.Count);
    }

    [TestMethod]
    public void Apply_MissionCredits_PersistsAbsoluteBalance()
    {
        // TargetLevel 2 base=10, CreditsIndex 4 frac=0.8 → ceil(8)=8 clink
        var mission = GameMission.CreateForTests(210);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 210);
        SetObjectiveFields(objective, xpIndex: 0, creditsIndex: 4, creditScaler: 1f);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6010, inventory: inventory);

        NpcInteractHandler.ApplyMissionCompleteRewards(
            character, mission, objective, source: "TestCredits", notifyClient: true);

        Assert.AreEqual(8L, character.Credits);
        Assert.AreEqual(1, invPersist.CreditsSaves.Count);
        Assert.AreEqual((6010L, 8L), invPersist.CreditsSaves[0]);
    }

    [TestMethod]
    public void Apply_MissionCredits_NotifyClientFalse_StillPersists()
    {
        var mission = GameMission.CreateForTests(211);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 211);
        SetObjectiveFields(objective, xpIndex: 0, creditsIndex: 4, creditScaler: 1f);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6011, inventory: inventory);

        // Dialog turn-in: client already applied money; server must still write DB without double-send.
        NpcInteractHandler.ApplyMissionCompleteRewards(
            character, mission, objective, notifyClient: false);

        Assert.AreEqual(8L, character.Credits);
        Assert.AreEqual(1, invPersist.CreditsSaves.Count);
    }

    [TestMethod]
    public void Apply_MissionCredits_AddsToExistingBalance()
    {
        var mission = GameMission.CreateForTests(212);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 212);
        SetObjectiveFields(objective, xpIndex: 0, creditsIndex: 4, creditScaler: 1f);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6012, inventory: inventory);
        character.SetCredits(100);

        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective);

        Assert.AreEqual(108L, character.Credits);
        Assert.AreEqual((6012L, 108L), invPersist.CreditsSaves[0]);
    }

    [TestMethod]
    public void Apply_ZeroCredits_DoesNotSaveCredits()
    {
        var mission = GameMission.CreateForTests(213);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 213);
        SetObjectiveFields(objective, xpIndex: 0, creditsIndex: 0);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6013, inventory: inventory);

        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective);

        Assert.AreEqual(0L, character.Credits);
        Assert.AreEqual(0, invPersist.CreditsSaves.Count);
    }

    [TestMethod]
    public void Apply_MissionCredits_AlwaysSendsAbsoluteCharacterLevel_NotGiveCredits()
    {
        // Dialog turn-in uses notifyClient:false; client still needs absolute money UI sync.
        // Must not send GiveCredits (would double if CompleteObjective / 0x2070 already applied).
        var mission = GameMission.CreateForTests(214);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 214);
        SetObjectiveFields(objective, xpIndex: 0, creditsIndex: 4, creditScaler: 1f);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6014, inventory: inventory);

        var sent = new List<BasePacket>();
        var previous = TNLConnection.TestPacketSink;
        try
        {
            TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
            var conn = new TNLConnection();
            character.SetOwningConnection(conn);

            NpcInteractHandler.ApplyMissionCompleteRewards(
                character, mission, objective, notifyClient: false);

            Assert.AreEqual(8L, character.Credits);
            Assert.AreEqual(0, sent.OfType<GiveCreditsPacket>().Count(), "no additive GiveCredits");
            var level = sent.OfType<CharacterLevelPacket>().SingleOrDefault();
            Assert.IsNotNull(level, "must send absolute CharacterLevel for money HUD");
            Assert.AreEqual(8L, level.Currency);
        }
        finally
        {
            TNLConnection.TestPacketSink = previous;
        }
    }

    [TestMethod]
    public void SyncMissionCreditsToClient_NoConnection_NoThrow()
    {
        var character = MakeCharacter(6015);
        NpcInteractHandler.SyncMissionCreditsToClient(character, 99);
        NpcInteractHandler.SyncMissionCreditsToClient(null, 1);
    }

    [TestMethod]
    public void Apply_MissionCredits_NotifyClientTrue_StillNoGiveCredits_OnlyAbsolute()
    {
        // Server-driven complete also must not send additive GiveCredits (0x2070 client path
        // already applied CompleteObjective money). Absolute CharacterLevel only.
        var mission = GameMission.CreateForTests(215);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 215);
        SetObjectiveFields(objective, xpIndex: 0, creditsIndex: 4, creditScaler: 1f);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6016, inventory: inventory);

        var sent = new List<BasePacket>();
        var previous = TNLConnection.TestPacketSink;
        try
        {
            TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
            character.SetOwningConnection(new TNLConnection());

            NpcInteractHandler.ApplyMissionCompleteRewards(
                character, mission, objective, notifyClient: true);

            Assert.AreEqual(8L, character.Credits);
            Assert.AreEqual(0, sent.OfType<GiveCreditsPacket>().Count());
            Assert.AreEqual(1, sent.OfType<CharacterLevelPacket>().Count());
            Assert.AreEqual(8L, sent.OfType<CharacterLevelPacket>().Single().Currency);
        }
        finally
        {
            TNLConnection.TestPacketSink = previous;
        }
    }

    [TestMethod]
    public void Apply_MissionCredits_AbsolutePacket_PreservesExperienceAndLevel()
    {
        // Regression: CharacterLevel money sync must not wipe XP/level (client absolute apply).
        var mission = GameMission.CreateForTests(216);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 216);
        SetObjectiveFields(objective, xpIndex: 0, creditsIndex: 4, creditScaler: 1f);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6017, xp: 55_000, level: 7, inventory: inventory);
        character.SetSkillPoints(9);

        var sent = new List<BasePacket>();
        var previous = TNLConnection.TestPacketSink;
        try
        {
            TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
            character.SetOwningConnection(new TNLConnection());

            NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective);

            var level = sent.OfType<CharacterLevelPacket>().Single();
            Assert.AreEqual(8L, level.Currency);
            Assert.AreEqual(55_000, level.Experience);
            Assert.AreEqual((byte)7, level.Level);
            Assert.AreEqual((short)9, level.SkillPoints);
        }
        finally
        {
            TNLConnection.TestPacketSink = previous;
        }
    }

    [TestMethod]
    public void Apply_MissionCredits_StaticFallback_WhenIndexZero()
    {
        var mission = GameMission.CreateForTests(217);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 217);
        SetObjectiveFields(objective, xpIndex: 0, creditsIndex: 0, staticCredits: 17);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6018, inventory: inventory);

        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective);

        Assert.AreEqual(17L, character.Credits);
        Assert.AreEqual((6018L, 17L), invPersist.CreditsSaves[0]);
    }

    [TestMethod]
    public void Apply_MissionCreditsAndXp_Together_PersistBoth()
    {
        var mission = GameMission.CreateForTests(218);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 218);
        // XP: index 5 → 320; credits: use level-2 style via custom resolvers already set;
        // TargetLevel 5 base=34, index 4 frac=0.8 → ceil(27.2)=28
        SetObjectiveFields(objective, xpIndex: 5, creditsIndex: 4, creditScaler: 1f);

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6019, inventory: inventory);

        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective);

        Assert.AreEqual(320, character.Experience);
        Assert.AreEqual(28L, character.Credits);
        Assert.AreEqual(1, _persist.Saves.Count);
        Assert.AreEqual(1, invPersist.CreditsSaves.Count);
    }

    [TestMethod]
    public void Apply_MissionCredits_NullObjective_UsesMaxSequenceCredits()
    {
        var obj1 = GameMissionObjective.CreateForTests(1, 0, 219);
        SetObjectiveFields(obj1, xpIndex: 0, creditsIndex: 1, creditScaler: 1f); // L2 base10 * 0.2 = 2
        var obj2 = GameMissionObjective.CreateForTests(2, 1, 219);
        SetObjectiveFields(obj2, xpIndex: 0, creditsIndex: 4, creditScaler: 1f); // 8

        var mission = GameMission.CreateForTests(219, obj1, obj2);
        mission.TargetLevel = 2;

        var invPersist = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(invPersist);
        var character = MakeCharacter(6020, inventory: inventory);

        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective: null);

        Assert.AreEqual(8L, character.Credits, "max-sequence objective credits");
    }

    [TestMethod]
    public void Apply_MissionCredits_SaveThrows_DoesNotThrowOut_XpStillGranted()
    {
        var mission = GameMission.CreateForTests(220);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 220);
        SetObjectiveFields(objective, xpIndex: 5, creditsIndex: 4, creditScaler: 1f);

        // Throwing inventory persistence: AddCredits → SaveCredits throws; XP path independent.
        var inventory = new InventoryManager(new ThrowingCreditsSavePersistence());
        var character = MakeCharacter(6021, inventory: inventory);

        NpcInteractHandler.ApplyMissionCompleteRewards(character, mission, objective);

        Assert.AreEqual(320, character.Experience, "XP grant must not depend on credit save");
        Assert.AreEqual(1, _persist.Saves.Count);
    }

    [TestMethod]
    public void SyncMissionCreditsToClient_SendsAbsoluteCurrencyPacket()
    {
        var character = MakeCharacter(6022, xp: 100, level: 3);
        character.SetCredits(42);
        var sent = new List<BasePacket>();
        var previous = TNLConnection.TestPacketSink;
        try
        {
            TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
            character.SetOwningConnection(new TNLConnection());

            NpcInteractHandler.SyncMissionCreditsToClient(character, 999);

            var pkt = sent.OfType<CharacterLevelPacket>().Single();
            Assert.AreEqual(999L, pkt.Currency);
            Assert.AreEqual(100, pkt.Experience);
            Assert.AreEqual((byte)3, pkt.Level);
            Assert.AreEqual(0, sent.OfType<GiveCreditsPacket>().Count());
        }
        finally
        {
            TNLConnection.TestPacketSink = previous;
        }
    }

    /// <summary>SaveCredits always throws (credit path failure isolation).</summary>
    private sealed class ThrowingCreditsSavePersistence : IInventoryPersistence
    {
        public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) =>
            Array.Empty<CharacterInventoryItem>();

        public void UpsertCargo(long characterCoid, CharacterInventoryItem item) { }
        public void MoveCargo(long characterCoid, CharacterInventoryItem item) { }
        public void DeleteCargo(long characterCoid, long itemCoid) { }
        public void ClearCargo(long characterCoid) { }
        public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0) { }
        public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot) { }
        public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount) { }
        public long LoadCredits(long characterCoid) => 0;
        public void SaveCredits(long characterCoid, long credits) =>
            throw new InvalidOperationException("credits db down");
    }
}
