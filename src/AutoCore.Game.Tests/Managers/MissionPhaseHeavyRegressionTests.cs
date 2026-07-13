using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Dense regression for mission-phase deliver, UseObject resolve, AutoPatrol idempotency,
/// force-complete, and giver suppress rules.
/// </summary>
[TestClass]
public class MissionPhaseHeavyRegressionTests
{
    private const int ContId = 8950;
    private const int MissionId = 98500;
    private const int KillObjId = 98501;
    private const int DeliverObjId = 98502;
    private const int GiverCbid = 12447;
    private const int DeliverCbid = 12448;
    private const int KillTemplateId = 580;
    private const long DialogSpawn = 34090;
    private const long PadSpawn = 35820;
    private const long CreatePadRx = 35819;
    private const long Waypoint = 33939;
    private const long DialogCreature = 99002;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCreatureCloneBase(DeliverCbid, maxHitPoint: 80);
        var cb = AssetManager.Instance.GetCloneBase(DeliverCbid) as AutoCore.Game.CloneBases.CloneBaseCreature;
        if (cb != null)
            cb.CreatureSpecific.IsNPC = 1;
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
        MissionClientSoftPedal.ResetForTests();
        MissionClientSoftPedal.GroupReactionSuppressMs = 0;
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        MissionClientSoftPedal.ResetForTests();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void AutoPatrol_WhenDeliverReady_NoLogSpamOrPackets()
    {
        SeedFinalExamShape();
        var (character, vehicle, map) = CreatePlayer();
        SeedPadGraph(map);
        character.CurrentQuests.Add(MakeQuest(1));
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        PlaceWaypoint(map);

        map.EnsureDeliverTurnInNpc(vehicle, DeliverCbid);
        _sent.Clear();

        for (var i = 0; i < 30; i++)
        {
            NpcInteractHandler.HandleAutoPatrol(character.OwningConnection, new AutoPatrolPacket
            {
                Target = new TFID(Waypoint, false),
            });
        }

        Assert.AreEqual(0, _sent.OfType<CreateCreaturePacket>().Count());
        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count());
        Assert.AreEqual(1, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void UseObject_SuppressedResolvedNpc_Rejected()
    {
        SeedDeliverOnly();
        var (character, vehicle, map) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);
        character.CurrentQuests.Add(MakeQuest(0));

        var npc = new Creature { Position = new Vector3(0, 0, 0) };
        npc.SetCoid(MapNpcIdentity.CoidBase + 1, true);
        npc.LoadCloneBase(DeliverCbid);
        npc.SetupCBFields();
        npc.SetMap(map);
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(npc.ObjectId.Coid);

        _sent.Clear();
        NpcInteractHandler.HandleUseObject(character.OwningConnection, new UseObjectPacket
        {
            Target = new TFID(99999, false), // missing — resolve by CBID then suppress check
            ObjectiveId = DeliverObjId,
        });
        Assert.IsFalse(_sent.OfType<NpcMissionDialogPacket>().Any());
    }

    [TestMethod]
    public void UseObject_OutOfRangeResolvedNpc_Rejected()
    {
        SeedDeliverOnly();
        var (character, vehicle, map) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        character.CurrentQuests.Add(MakeQuest(0));

        var npc = new Creature { Position = new Vector3(500, 0, 500) };
        npc.SetCoid(MapNpcIdentity.CoidBase + 2, true);
        npc.LoadCloneBase(DeliverCbid);
        npc.SetupCBFields();
        npc.SetMap(map);

        _sent.Clear();
        NpcInteractHandler.HandleUseObject(character.OwningConnection, new UseObjectPacket
        {
            Target = new TFID(88888, false),
            ObjectiveId = DeliverObjId,
        });
        Assert.IsFalse(_sent.OfType<NpcMissionDialogPacket>().Any());
    }

    [TestMethod]
    public void TryResolveNearbyDeliver_IgnoresSuppressedAndWrongCbid()
    {
        SeedDeliverOnly();
        var (character, vehicle, map) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);
        character.CurrentQuests.Add(MakeQuest(0));

        var wrong = new Creature { Position = new Vector3(0, 0, 0) };
        wrong.SetCoid(MapNpcIdentity.CoidBase + 3, true);
        wrong.SetCbidForTests(999);
        wrong.SetMap(map);

        var suppressed = new Creature { Position = new Vector3(1, 0, 0) };
        suppressed.SetCoid(MapNpcIdentity.CoidBase + 4, true);
        suppressed.LoadCloneBase(DeliverCbid);
        suppressed.SetupCBFields();
        suppressed.SetMap(map);
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(suppressed.ObjectId.Coid);

        Assert.IsNull(NpcInteractHandler.TryResolveNearbyDeliverNpc(
            character, DeliverObjId, new Vector3(0, 0, 0), 1));
    }

    [TestMethod]
    public void SameNpcDeliver_NoCreateErrorAndNoSuppress()
    {
        const int same = 2468;
        var obj = MissionObjective.CreateForTests(DeliverObjId, 0, MissionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = same,
            NPCTargetCompletes = true,
        });
        var mission = Mission.CreateForTests(MissionId, obj);
        mission.NPC = same;
        AssetManager.Instance.SetTestMission(mission);

        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, same);
        PlaceDialog(map, same);
        character.CurrentQuests.Add(MakeQuest(0));
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawn));
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogCreature));
    }

    [TestMethod]
    public void CompletedAlternateForm_SuppressesGiver_CreatesPadDeliver()
    {
        SeedFinalExamShape();
        var (character, vehicle, map) = CreatePlayer();
        SeedPadGraph(map);
        PlaceDialog(map, GiverCbid);
        character.CompletedMissionIds.Add(MissionId);
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsTrue(
            character.MapPresence.IsSuppressed(DialogSpawn)
            || character.MapPresence.IsSuppressed(DialogCreature));
    }

    [TestMethod]
    public void EnsureDeliverTurnIn_InvalidArgs_NoThrow()
    {
        var (character, vehicle, map) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);
        Assert.AreEqual(0, map.EnsureDeliverTurnInNpc(null, DeliverCbid));
        Assert.AreEqual(0, map.EnsureDeliverTurnInNpc(vehicle, 0));
        Assert.AreEqual(0, map.EnsureDeliverTurnInNpc(vehicle, -1));
    }

    [TestMethod]
    public void FireMatchingCreates_FromMapDataTemplate_WhenReactionNotLive()
    {
        SeedFinalExamShape();
        var (character, vehicle, map) = CreatePlayer();
        // Template only — no live Reaction entity yet.
        map.MapData.Templates[PadSpawn] = MakeSpawn(PadSpawn, false, DeliverCbid);
        map.MapData.Templates[CreatePadRx] = new ReactionTemplate
        {
            COID = (int)CreatePadRx,
            ReactionType = ReactionType.Create,
            Objects = { PadSpawn },
        };
        character.CurrentQuests.Add(MakeQuest(1));
        character.SetMap(map);
        vehicle.SetMap(map);

        var fired = map.EnsureDeliverTurnInNpc(vehicle, DeliverCbid);
        Assert.IsTrue(fired >= 0);
        Assert.IsNotNull(map.GetObjectByCoid(CreatePadRx) as Reaction
            ?? map.GetObjectByCoid(PadSpawn));
    }

    [TestMethod]
    public void ObjectiveHelpers_BlockingAndForceComplete()
    {
        var solo = MissionObjective.CreateForTests(1, 0, MissionId, 1);
        solo.Requirements.Add(new ObjectiveRequirementDeliver(solo)
        {
            NPCTargetCBID = 1,
            NPCTargetCompletes = true,
        });
        Assert.IsFalse(NpcInteractHandler.ObjectiveHasBlockingSiblingRequirements(
            solo, RequirementType.Patrol));
        Assert.IsFalse(NpcInteractHandler.ObjectiveNeedsForceClientCompleteAfterDeliver(solo));
        Assert.IsFalse(NpcInteractHandler.ObjectiveNeedsForceClientCompleteAfterDeliver(null));

        var multi = MissionObjective.CreateForTests(2, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(multi) { AutoComplete = true };
        patrol.GenericTargets[0] = 1;
        patrol.TargetCount = 1;
        multi.Requirements.Add(patrol);
        multi.Requirements.Add(new ObjectiveRequirementDeliver(multi)
        {
            NPCTargetCBID = 2,
            NPCTargetCompletes = true,
        });
        Assert.IsTrue(NpcInteractHandler.ObjectiveHasBlockingSiblingRequirements(
            multi, RequirementType.Patrol));
        Assert.IsTrue(NpcInteractHandler.ObjectiveNeedsForceClientCompleteAfterDeliver(multi));
    }

    [TestMethod]
    public void DeliverTurnIn_ForceComplete_SendsJournalAnd2070()
    {
        SeedPatrolPlusDeliverSingleObjective();
        var (character, vehicle, map) = CreatePlayer();
        PlaceLiveDeliverNpc(map);
        character.CurrentQuests.Add(MakeQuest(0));
        character.SetMap(map);
        vehicle.SetMap(map);

        Action pending = null;
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 50;
        MissionClientSoftPedal.GroupReactionSuppressMs = 100;
        NpcInteractHandler.ScheduleDelayedWork = (a, d, _) =>
        {
            Assert.AreEqual(100, d);
            pending = a;
        };

        NpcInteractHandler.HandleMissionDialogResponse(character.OwningConnection,
            new MissionDialogResponsePacket
            {
                MissionId = MissionId,
                Accepted = true,
                MissionGiver = new TFID(MapNpcIdentity.CoidBase + 50, false),
            });

        Assert.IsNotNull(pending);
        _sent.Clear();
        pending();
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void ReactionCreate_Personal_SpawnsDialogNpcChild()
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(DeliverCbid, maxHitPoint: 50);
        var cb = AssetManager.Instance.GetCloneBase(DeliverCbid) as AutoCore.Game.CloneBases.CloneBaseCreature;
        if (cb != null)
            cb.CreatureSpecific.IsNPC = 1;

        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId + 1,
            MapFileName = "tm_rx",
            DisplayName = "t",
            IsPersistent = true,
        }, new Vector4());

        map.MapData.Templates[PadSpawn] = MakeSpawn(PadSpawn, false, DeliverCbid);
        var spawn = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[PadSpawn]);
        spawn.SetCoid(PadSpawn, false);
        spawn.SetMap(map);

        var rxTpl = new ReactionTemplate
        {
            COID = (int)CreatePadRx,
            ReactionType = ReactionType.Create,
        };
        rxTpl.Objects.Add(PadSpawn);
        var rx = new Reaction(rxTpl);
        rx.SetCoid(CreatePadRx, false);
        rx.SetMap(map);

        var (character, vehicle, _) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);

        Assert.IsTrue(rx.TriggerIfPossible(vehicle));
        // Spawn may succeed if clonebase registered
        Assert.IsTrue(character.MapPresence.IsMaterialized(PadSpawn)
            || spawn.HasLiveSpawn());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void SeedFinalExamShape()
    {
        var kill = MissionObjective.CreateForTests(KillObjId, 0, MissionId, 1);
        kill.Requirements.Add(new ObjectiveRequirementKill(kill)
        {
            TargetCBID = KillTemplateId,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
        });
        var del = MissionObjective.CreateForTests(DeliverObjId, 1, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(del)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
        };
        patrol.GenericTargets[0] = Waypoint;
        patrol.TargetCount = 1;
        del.Requirements.Add(patrol);
        del.Requirements.Add(new ObjectiveRequirementDeliver(del)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var m = Mission.CreateForTests(MissionId, kill, del);
        m.NPC = GiverCbid;
        AssetManager.Instance.SetTestMission(m);
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private void SeedDeliverOnly()
    {
        var del = MissionObjective.CreateForTests(DeliverObjId, 0, MissionId, 1);
        del.Requirements.Add(new ObjectiveRequirementDeliver(del)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var m = Mission.CreateForTests(MissionId, del);
        m.NPC = GiverCbid;
        AssetManager.Instance.SetTestMission(m);
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private void SeedPatrolPlusDeliverSingleObjective()
    {
        var del = MissionObjective.CreateForTests(DeliverObjId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(del) { AutoComplete = true, AutoCompleteDistance = 25 };
        patrol.GenericTargets[0] = Waypoint;
        patrol.TargetCount = 1;
        del.Requirements.Add(patrol);
        del.Requirements.Add(new ObjectiveRequirementDeliver(del)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var m = Mission.CreateForTests(MissionId, del);
        m.NPC = DeliverCbid;
        AssetManager.Instance.SetTestMission(m);
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private static CharacterQuest MakeQuest(byte seq)
    {
        var q = new CharacterQuest(MissionId, seq);
        q.PopulateFromAssets();
        return q;
    }

    private static SpawnPointTemplate MakeSpawn(long coid, bool active, int spawnType)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)coid,
            IsActive = active,
            OriginalIsActive = active,
            CBID = 0,
            Location = new Vector4(0, 0, 0, 0),
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = spawnType,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        return tpl;
    }

    private void SeedPadGraph(SectorMap map)
    {
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, GiverCbid);
        map.MapData.Templates[PadSpawn] = MakeSpawn(PadSpawn, false, DeliverCbid);
        var rxTpl = new ReactionTemplate
        {
            COID = (int)CreatePadRx,
            ReactionType = ReactionType.Create,
        };
        rxTpl.Objects.Add(PadSpawn);
        var rx = new Reaction(rxTpl);
        rx.SetCoid(CreatePadRx, false);
        rx.SetMap(map);
        var pad = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[PadSpawn]);
        pad.SetCoid(PadSpawn, false);
        pad.SetMap(map);
    }

    private void PlaceDialog(SectorMap map, int cbid)
    {
        if (map.GetObjectByCoid(DialogSpawn) is not SpawnPoint)
        {
            if (!map.MapData.Templates.ContainsKey(DialogSpawn))
                map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, cbid);
            var sp = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[DialogSpawn]);
            sp.SetCoid(DialogSpawn, false);
            sp.SetMap(map);
            sp.SetLastSpawnedCoidForTests(DialogCreature);
        }
        var c = new Creature();
        c.SetCoid(DialogCreature, true);
        c.SetCbidForTests(cbid);
        c.SpawnOwner = DialogSpawn;
        c.IsMissionGiver = true;
        c.SetMap(map);
    }

    private void PlaceLiveDeliverNpc(SectorMap map)
    {
        var npc = new Creature { Position = new Vector3(0, 0, 0) };
        npc.SetCoid(MapNpcIdentity.CoidBase + 50, true);
        npc.LoadCloneBase(DeliverCbid);
        npc.SetupCBFields();
        npc.IsMissionGiver = true;
        npc.SetMap(map);
    }

    private void PlaceWaypoint(SectorMap map)
    {
        var wp = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        wp.SetCoid(Waypoint, false);
        wp.Position = new Vector3(0, 0, 0);
        wp.SetMap(map);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_heavy_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.ActivateGhosting();
        var character = new Character();
        character.SetCoid(1800, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        vehicle.SetCoid(1801, true);
        character.SetCurrentVehicleForTests(vehicle);
        return (character, vehicle, map);
    }
}
