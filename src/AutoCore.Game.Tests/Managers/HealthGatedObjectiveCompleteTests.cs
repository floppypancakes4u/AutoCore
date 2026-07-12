using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Generic health%-gated collision complete (Ark Bay SCAB pad shape):
/// type-7 health percent, CompleteObjective reaction (G1=objective id), recheck after heal.
/// Synthetic ids only — not mission 3050 hardcoding.
/// </summary>
[TestClass]
public class HealthGatedObjectiveCompleteTests
{
    private const int MissionId = 93050;
    private const int ObjectiveId = 95467;
    private const int ContId = 709;
    private const int VarHealthPct = 64;
    private const int VarConstOne = 4;
    private const long CompleteTriggerCoid = 95828;
    private const long CompleteReactionCoid = 95832;
    private const long HealTriggerCoid = 96475;
    private const long HealReactionCoid = 96446;
    private const int HealSkillId = 857;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        AssetManager.Instance.ClearTestSkills();
        TriggerManager.Instance.ClearAllForTests();
        IncompleteHandlerLog.TestSink = null;

        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = HealSkillId,
            Name = "INC Repair station heal",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = 0.15f }
            }
        });
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        AssetManager.Instance.ClearTestSkills();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void CompleteObjective_Reaction_CompletesActiveQuest()
    {
        SeedMission();
        var (character, vehicle, map) = CreatePlayer();
        SeedHealthVars(map);
        character.EnsureLogicVariables();

        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        PlaceCompleteObjectiveReaction(map, CompleteReactionCoid, ObjectiveId);
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetCurrentHP(100, triggerGhostUpdate: false);

        map.TriggerReactions(vehicle, new List<long> { CompleteReactionCoid });

        Assert.AreEqual(0, character.CurrentQuests.Count, "Quest should be removed on final objective complete");
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(
            _sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveId),
            "Server must send 0x2070 CompleteDynamicObjective");
        Assert.IsFalse(
            _sent.OfType<GroupReactionCallPacket>().Any(),
            "CompleteObjective should not also 0x206C-double-apply (0x2070 already force-completes)");
    }

    [TestMethod]
    public void CompleteObjective_WrongObjective_DoesNotComplete()
    {
        SeedMission();
        var (character, vehicle, map) = CreatePlayer();
        character.EnsureLogicVariables();

        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        PlaceCompleteObjectiveReaction(map, CompleteReactionCoid, ObjectiveId + 99);
        Assert.IsFalse(
            ((Reaction)map.GetObjectByCoid(CompleteReactionCoid)!).TriggerIfPossible(vehicle),
            "Mismatched objective id must not complete or broadcast");

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    [TestMethod]
    public void FullHpOnPad_FiresHealthGatedComplete_WithoutPriorDamage()
    {
        SeedMission();
        var (character, vehicle, map) = CreatePlayer();
        SeedHealthVars(map);
        character.EnsureLogicVariables();

        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        PlaceHealthGatedCompleteTrigger(map);
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetCurrentHP(100, triggerGhostUpdate: false);
        vehicle.Position = new Vector3(0, 0, 0);

        TriggerManager.Instance.CheckTriggersFor(vehicle, nowMs: 0);

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void HealToFullOnPad_RechecksAndCompletes_HealthGatedObjective()
    {
        // SCAB shape: enter pad damaged → heal pulses → type-7 becomes 1 → CompleteObjective.
        SeedMission();
        var (character, vehicle, map) = CreatePlayer();
        SeedHealthVars(map);
        character.EnsureLogicVariables();

        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        PlaceHealthGatedCompleteTrigger(map);
        PlaceHealPad(map);

        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetCurrentHP(1, triggerGhostUpdate: false);
        vehicle.Position = new Vector3(0, 0, 0);

        // Enter: heal starts, complete gate fails (health% != 1).
        TriggerManager.Instance.CheckTriggersFor(vehicle, nowMs: 0);
        Assert.AreEqual(1, character.CurrentQuests.Count, "Must not complete while damaged");
        Assert.IsTrue(vehicle.GetCurrentHP() > 1, "Heal pad must restore some HP on enter");

        // Drive HP to full via restore (pad skill path) — must recheck volume conditions.
        vehicle.RestoreHealth(1000);
        Assert.AreEqual(100, vehicle.GetCurrentHP());
        Assert.AreEqual(0, character.CurrentQuests.Count,
            "Heal to full while inside pad must re-eval collision triggers and complete");
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveId));
    }

    [TestMethod]
    public void HealPulse_ToFull_CompletesWhileStandingStill()
    {
        SeedMission();
        var (character, vehicle, map) = CreatePlayer();
        SeedHealthVars(map);
        character.EnsureLogicVariables();

        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        PlaceHealthGatedCompleteTrigger(map);
        PlaceHealPad(map);

        // 15% of 100 = 15/tick. Start at 1 so enter heal leaves us well below full.
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetCurrentHP(1, triggerGhostUpdate: false);
        vehicle.Position = new Vector3(0, 0, 0);

        TriggerManager.Instance.CheckTriggersFor(vehicle, nowMs: 0);
        Assert.AreEqual(1, character.CurrentQuests.Count, "Partial enter heal must not complete");
        Assert.AreEqual(16, vehicle.GetCurrentHP());

        // Cadence pulses until full; OnPlayerHealthChanged opens the complete gate when HP hits max.
        for (long t = 1000; t <= 10000 && character.CurrentQuests.Count > 0; t += 1000)
            TriggerManager.Instance.CheckTriggersFor(vehicle, nowMs: t);

        Assert.AreEqual(100, vehicle.GetCurrentHP(), "Pad should heal to full over pulses");
        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    private static void SeedMission()
    {
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(MissionId, MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1)));
    }

    private static void SeedHealthVars(SectorMap map)
    {
        map.MapData.Variables[VarHealthPct] = Variable.CreateForTests(
            VarHealthPct, LogicVariableStore.TypePlayerHealthPercent, 0f, 0f, "health_pct");
        map.MapData.Variables[VarConstOne] = Variable.CreateForTests(
            VarConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");
    }

    private static void PlaceCompleteObjectiveReaction(SectorMap map, long reactionCoid, int objectiveId)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            Name = "l1_complete_obj",
            ReactionType = ReactionType.CompleteObjective,
            GenericVar1 = objectiveId,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static void PlaceHealthGatedCompleteTrigger(SectorMap map)
    {
        PlaceCompleteObjectiveReaction(map, CompleteReactionCoid, ObjectiveId);

        var tpl = new TriggerTemplate
        {
            COID = (int)CompleteTriggerCoid,
            Name = "l1_coll_scab_complete",
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            DoCollision = true,
            DoConditionals = true,
            AllConditionsNeeded = true,
            ActivationCount = -1,
        };
        tpl.Reactions.Add(CompleteReactionCoid);
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = VarHealthPct,
            RightId = VarConstOne,
            Type = ConditionalType.EqualTo,
        });
        var trigger = new Trigger(tpl) { Position = new Vector3(), Scale = 10f };
        trigger.SetCoid(CompleteTriggerCoid, false);
        trigger.SetMap(map);
    }

    private static void PlaceHealPad(SectorMap map)
    {
        var reaction = new Reaction(new ReactionTemplate
        {
            COID = (int)HealReactionCoid,
            Name = "l1_skillcast_heal",
            ReactionType = ReactionType.SkillCast,
            GenericVar1 = HealSkillId,
            GenericVar3 = 1,
        });
        reaction.SetCoid(HealReactionCoid, false);
        reaction.SetMap(map);

        var tpl = new TriggerTemplate
        {
            COID = (int)HealTriggerCoid,
            Name = "l1_coll_humanrepairpad",
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            DoCollision = true,
            ActivationCount = -1,
        };
        tpl.Reactions.Add(HealReactionCoid);
        var trigger = new Trigger(tpl) { Position = new Vector3(), Scale = 10f };
        trigger.SetCoid(HealTriggerCoid, false);
        trigger.SetMap(map);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_health_gate_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(350, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle { Position = new Vector3() };
        vehicle.SetCoid(351, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}
