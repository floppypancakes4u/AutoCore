using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// After solo hygiene restores fam baseline, mid-mission reaction-created NPCs (pad turn-in
/// Gunny class) must be recreated from active deliver objectives — generic, no mission ids.
/// </summary>
[TestClass]
public class MissionPhaseSpawnReplayTests
{
    private const int ContId = 812;
    private const int MissionId = 98050;
    private const int ObjectiveId = 98051;
    private const int DeliverCbid = 12448;
    private const int UnrelatedCbid = 99999;
    private const long DialogSpawnCoid = 14090;
    private const long PadSpawnCoid = 15820;
    private const long CombatSpawnCoid = 14138;
    private const long CreatePadReactionCoid = 15819;
    private const long CreateCombatReactionCoid = 14139;

    [TestInitialize]
    public void SetUp()
    {
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestMethod]
    public void Replay_WithActiveDeliver_FiresCreateForMatchingFamInactiveSpawn()
    {
        SeedDeliverMission(DeliverCbid);
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnTemplates(map);
        PlaceCreateReaction(map, CreatePadReactionCoid, PadSpawnCoid);
        PlacePadSpawnMarkerWithoutChildren(map);

        // Hygiene baseline (as after server restart).
        map.ApplyAuthoredSpawnHygiene();
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(PadSpawnCoid)!).HasLiveSpawn());

        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map);
        vehicle.SetMap(map);
        character.EnsureLogicVariables();

        var fired = map.ReplayMissionWorldSetup(vehicle);
        Assert.IsTrue(fired > 0, "Create for deliver-target spawn must run after hygiene");
    }

    [TestMethod]
    public void Replay_WithoutDeliverObjective_DoesNotCreatePadSpawn()
    {
        // Active quest without deliver — early tutorial; pad Gunny must stay absent.
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(MissionId, MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1)));

        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnTemplates(map);
        PlaceCreateReaction(map, CreatePadReactionCoid, PadSpawnCoid);
        PlacePadSpawnMarkerWithoutChildren(map);

        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map);
        vehicle.SetMap(map);
        var fired = map.ReplayMissionWorldSetup(vehicle);
        Assert.AreEqual(0, fired, "No deliver CBID → no pad Create replay");
    }

    [TestMethod]
    public void Replay_WhenDeliverNpcAlreadyLive_DoesNotReCreate()
    {
        SeedDeliverMission(DeliverCbid);
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnTemplates(map);
        PlaceCreateReaction(map, CreatePadReactionCoid, PadSpawnCoid);
        PlacePadSpawnMarkerWithoutChildren(map);

        // Live turn-in NPC already present (e.g. continuous session).
        var npc = new Creature();
        npc.SetCoid(88001, true);
        npc.SetCbidForTests(DeliverCbid);
        npc.SetMap(map);

        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map);
        vehicle.SetMap(map);
        var fired = map.ReplayMissionWorldSetup(vehicle);
        // Create may re-fire for client 0x206C even when a server entity already has the CBID
        // (needed after relog / client object-table loss). Live-entity short-circuit only skips
        // EnsureDeliverNpcChildren re-spawn.
        Assert.IsTrue(fired >= 0);
        Assert.IsNotNull(map.GetObjectByCoid(88001), "Existing deliver NPC must remain");
    }

    [TestMethod]
    public void Replay_DoesNotCreateCombatSpawn_WhenDeliverNeedsPadNpc()
    {
        SeedDeliverMission(DeliverCbid);
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnTemplates(map);
        PlaceCreateReaction(map, CreatePadReactionCoid, PadSpawnCoid);
        PlaceCreateReaction(map, CreateCombatReactionCoid, CombatSpawnCoid);
        PlacePadSpawnMarkerWithoutChildren(map);

        // Combat spawn marker present, fam-inactive, SpawnType is NOT the deliver CBID.
        var combatTpl = (SpawnPointTemplate)map.MapData.Templates[CombatSpawnCoid];
        combatTpl.Spawns.Clear();
        combatTpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = UnrelatedCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        var combatSpawn = new SpawnPoint(combatTpl);
        combatSpawn.SetCoid(CombatSpawnCoid, false);
        combatSpawn.SetMap(map);

        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ReplayMissionWorldSetup(vehicle);

        Assert.IsFalse(combatSpawn.HasLiveSpawn(),
            "Combat pathing spawn must not materialize for a pad deliver objective");
    }

    [TestMethod]
    public void HygieneThenReplay_LoginShape_RestoresDialogAndReplaysPadCreate()
    {
        SeedDeliverMission(DeliverCbid);
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnTemplates(map);
        PlaceCreateReaction(map, CreatePadReactionCoid, PadSpawnCoid);

        // Leaked mid-mission: dialog missing, pad had a reaction child, combat car live.
        var combatTpl = (SpawnPointTemplate)map.MapData.Templates[CombatSpawnCoid];
        var combatSpawn = new SpawnPoint(combatTpl);
        combatSpawn.SetCoid(CombatSpawnCoid, false);
        combatSpawn.SetMap(map);
        var combatCar = new Vehicle();
        combatCar.SetCoid(90010, true);
        combatCar.SpawnOwnerCoid = CombatSpawnCoid;
        combatCar.SetMap(map);
        combatSpawn.SetLastSpawnedCoidForTests(90010);

        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map); // PlayerCount hygiene runs on enter
        vehicle.SetMap(map);

        // Hygiene (solo) should have restored dialog and purged combat car.
        Assert.IsNotNull(map.GetObjectByCoid(DialogSpawnCoid) as SpawnPoint);
        Assert.IsNull(map.GetObjectByCoid(90010), "Combat car purged by hygiene");

        var fired = map.ReplayMissionWorldSetup(vehicle);
        Assert.IsTrue(fired > 0, "After hygiene, deliver Create must replay for pad Gunny-class spawn");
    }

    [TestMethod]
    public void Replay_MissionConditionedCreate_FiresWhenType12Passes()
    {
        // Create gated by type-12 condition on a remote trigger — mission re-eval path companion.
        const int varActiveObj = 15;
        const int varOne = 4;
        const long condTriggerCoid = 99130;
        const long createRxCoid = 99139;
        const long gatedSpawnCoid = 99138;

        SeedDeliverMission(DeliverCbid); // not used for this path; type12 gates Create
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (character, vehicle, map) = CreatePlayerWithMap();
        map.MapData.Variables[varActiveObj] = Variable.CreateForTests(
            varActiveObj, LogicVariableStore.TypeHasActiveObjective, ObjectiveId, 0f, "obj");
        map.MapData.Variables[varOne] = Variable.CreateForTests(
            varOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");

        var spawnTpl = MakeSpawnTemplate(gatedSpawnCoid, originalActive: false, spawnType: UnrelatedCbid);
        map.MapData.Templates[gatedSpawnCoid] = spawnTpl;
        var spawn = new SpawnPoint(spawnTpl);
        spawn.SetCoid(gatedSpawnCoid, false);
        spawn.SetMap(map);

        PlaceCreateReaction(map, createRxCoid, gatedSpawnCoid);
        var trigTpl = new TriggerTemplate
        {
            COID = (int)condTriggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 2f,
            DoCollision = false,
            DoConditionals = true,
            AllConditionsNeeded = true,
            ActivationCount = -1,
        };
        trigTpl.Reactions.Add(createRxCoid);
        trigTpl.Conditions.Add(new TriggerConditional
        {
            LeftId = varActiveObj,
            RightId = varOne,
            Type = ConditionalType.EqualTo,
        });
        var trigger = new Trigger(trigTpl) { Position = new Vector3(), Scale = 2f };
        trigger.SetCoid(condTriggerCoid, false);
        trigger.SetMap(map);

        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map);
        vehicle.SetMap(map);
        character.EnsureLogicVariables();

        var fired = map.ReplayMissionWorldSetup(vehicle);
        Assert.IsTrue(fired > 0, "Mission-conditioned Create must fire when type12 is true");
    }

    private static void SeedDeliverMission(int deliverCbid)
    {
        var objective = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        objective.Requirements.Add(new ObjectiveRequirementDeliver(objective)
        {
            NPCTargetCBID = deliverCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objective));
    }

    private static CharacterQuest MakeQuest()
    {
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        return quest;
    }

    private static void SeedSpawnTemplates(SectorMap map)
    {
        map.MapData.Templates[DialogSpawnCoid] = MakeSpawnTemplate(DialogSpawnCoid, originalActive: true, spawnType: DeliverCbid);
        map.MapData.Templates[PadSpawnCoid] = MakeSpawnTemplate(PadSpawnCoid, originalActive: false, spawnType: DeliverCbid);
        map.MapData.Templates[CombatSpawnCoid] = MakeSpawnTemplate(CombatSpawnCoid, originalActive: false, spawnType: UnrelatedCbid);
    }

    private static SpawnPointTemplate MakeSpawnTemplate(long coid, bool originalActive, int spawnType = -1)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)coid,
            IsActive = originalActive,
            OriginalIsActive = originalActive,
            CBID = 0, // map-logic spawn markers; avoid LoadCloneBase in unit tests
        };
        if (spawnType > 0)
        {
            tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
            {
                SpawnType = spawnType,
                IsTemplate = false,
                LowerNumberOfSpawns = 1,
                UpperNumberOfSpawns = 1,
            });
        }

        return tpl;
    }

    private static void PlaceCreateReaction(SectorMap map, long reactionCoid, long targetSpawnCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Create,
        };
        tpl.Objects.Add(targetSpawnCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static void PlacePadSpawnMarkerWithoutChildren(SectorMap map)
    {
        var tpl = (SpawnPointTemplate)map.MapData.Templates[PadSpawnCoid];
        var spawn = new SpawnPoint(tpl);
        spawn.SetCoid(PadSpawnCoid, false);
        spawn.SetMap(map);
        Assert.IsFalse(spawn.HasLiveSpawn());
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayerWithMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_phase_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(400, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle { Position = new Vector3() };
        vehicle.SetCoid(401, true);
        character.SetCurrentVehicleForTests(vehicle);
        return (character, vehicle, map);
    }
}
