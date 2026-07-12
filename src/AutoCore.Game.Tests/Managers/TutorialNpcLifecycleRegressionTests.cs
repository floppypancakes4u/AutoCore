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
/// HEAVY regression suite for tutorial NPC lifecycle bugs (Ark Bay Gunny / Final Exam class).
/// Synthetic mission/object COIDs only — encodes retail trigger flag semantics:
/// <list type="bullet">
/// <item>Collision initiate volume: DoCollision + DoConditionals + HasActiveObjective gate</item>
/// <item>Rem initiator: DoOnActivate only (no coll, no conds) → Activate cascade only</item>
/// <item>Fam-active dialog spawn vs fam-inactive combat pathing spawn</item>
/// <item>Map hygiene / leave-reset after leaked Create/Delete</item>
/// </list>
/// </summary>
[TestClass]
public class TutorialNpcLifecycleRegressionTests
{
    // Synthetic map graph (mirrors 707 / Gunny structure without retail ids).
    private const int ContId = 8707;
    private const int TutorialMissionId = 93036; // prior tutorial (Guns-class)
    private const int TutorialObjectiveId = 94001;
    private const int CombatMissionId = 93037; // Final Exam-class
    private const int CombatObjectiveId = 95422; // kill-objective id for type-12 var
    private const int CombatObjectiveSeq = 0;

    private const int VarHasActiveCombatObjective = 15; // type 12 → CombatObjectiveId
    private const int VarConstOne = 4;
    private const int VarConstZero = 3;
    private const int VarHasDeletedDialog = 11; // latch after dialog delete

    private const long DialogSpawnCoid = 14090;
    private const long CombatSpawnCoid = 14138;
    private const long PadSpawnCoid = 15820;
    private const long InitiateVolumeCoid = 14130;
    private const long RemInitiatorCoid = 14134;
    private const long ActStartCoid = 16283;
    private const long DelDialogCoid = 14133;
    private const long CreateCombatCoid = 14139;
    private const long ActCombatCoid = 14142;
    private const long SetDeletedLatchCoid = 16285;
    private const long CombatVehicleCoid = 88001;
    private const long DialogCreatureCoid = 88002;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    // ─── A. Trigger flag matrix ─────────────────────────────────────────────

    [TestMethod]
    public void A01_CheckTriggersFor_OnlyDoCollision_Fires()
    {
        var (character, vehicle, map) = CreatePlayer();
        SeedMapVars(map);
        PlaceDialogCreature(map);

        // Pure rem (no coll) near player — must not fire on movement.
        // Use a distinct delete reaction so it does not collide with graph COIDs.
        const long remDelRx = 99310;
        PlaceDeleteReaction(map, remDelRx, DialogCreatureCoid);
        var remTpl = new TriggerTemplate
        {
            COID = (int)RemInitiatorCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 50f,
            DoCollision = false,
            DoOnActivate = true,
            ActivationCount = -1,
        };
        remTpl.Reactions.Add(remDelRx);
        var rem = new Trigger(remTpl);
        rem.SetCoid(RemInitiatorCoid, false);
        rem.Position = new Vector3(0, 0, 0);
        rem.Scale = 50f;
        rem.SetMap(map);

        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
        Assert.AreEqual(0, rem.FireCount);

        // Collision volume with always-true conditions — fires.
        PlaceCollisionAlwaysTrue(map, coid: 99001, deleteTarget: DialogCreatureCoid);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "DoCollision volume must fire on enter (personal suppress)");
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "Shared map keeps dialog for other players");
    }

    [TestMethod]
    public void A02_OnMissionStateChanged_DoesNotFire_RemInitiator_WithoutCombatObjective()
    {
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);
        PlaceLeakedCombatVehicle(map);

        // Prior tutorial mission only — same as Guns turn-in / objective progress.
        SeedTutorialMission();
        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        vehicle.Position = new Vector3(0, 0, 0);

        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        TriggerManager.Instance.OnMissionStateChanged(character);

        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "Tutorial objective progress must not delete dialog NPC");
        Assert.AreEqual(0, GetTriggerFireCount(map, RemInitiatorCoid));
        Assert.AreEqual(0, GetTriggerFireCount(map, InitiateVolumeCoid));
    }

    [TestMethod]
    public void A03_OnMissionStateChanged_RepeatedObjectiveAdvances_NeverFiresRemInitiator()
    {
        SeedCombatMission();
        SeedTutorialMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);

        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        vehicle.Position = new Vector3(0, 0, 0);

        for (var i = 0; i < 25; i++)
            TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
        Assert.AreEqual(0, GetTriggerFireCount(map, RemInitiatorCoid));
        Assert.IsNull(map.GetObjectByCoid(CombatVehicleCoid),
            "Combat pathing vehicle must not appear from objective spam");
    }

    [TestMethod]
    public void A04_OnVariableChanged_DoesNotFire_RemInitiator()
    {
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);
        character.EnsureLogicVariables();

        TriggerManager.Instance.OnVariableChanged(vehicle, VarHasDeletedDialog);
        TriggerManager.Instance.OnVariableChanged(vehicle, VarConstZero);
        TriggerManager.Instance.OnVariableChanged(vehicle, VarHasActiveCombatObjective);

        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
        Assert.AreEqual(0, GetTriggerFireCount(map, RemInitiatorCoid));
    }

    [TestMethod]
    public void A05_RemoteConditionalWatcher_StillFires_WhenDoConditionals()
    {
        // Legitimate small-scale mission watcher must keep working.
        SeedTutorialMission();
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Variables[200] = Variable.CreateForTests(
            200, LogicVariableStore.TypeHasActiveMission, TutorialMissionId, 0f);
        map.MapData.Variables[201] = Variable.CreateForTests(
            201, LogicVariableStore.TypeConstant, 1f, 1f);
        character.EnsureLogicVariables();

        const long remote = 99100;
        const long delRx = 99101;
        const long gate = 99102;
        PlaceDeleteReaction(map, delRx, gate);
        PlaceSimpleObject(map, gate, new Vector3(50, 0, 50));

        var tpl = new TriggerTemplate
        {
            COID = (int)remote,
            TargetType = TriggerTargetType.Players,
            Scale = 1f,
            DoCollision = false,
            DoConditionals = true,
            DoOnActivate = false,
            AllConditionsNeeded = true,
            ActivationCount = -1,
        };
        tpl.Reactions.Add(delRx);
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = 200,
            RightId = 201,
            Type = ConditionalType.EqualTo,
        });
        var trigger = new Trigger(tpl);
        trigger.SetCoid(remote, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 1f;
        trigger.SetMap(map);

        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        Assert.IsTrue(character.MapPresence.IsSuppressed(gate),
            "DoConditionals remote watcher must still fire (personal suppress)");
        Assert.IsNotNull(map.GetObjectByCoid(gate),
            "Shared map keeps gate for other players");
    }

    // ─── B. Initiate volume gating (HasActiveObjective) ─────────────────────

    [TestMethod]
    public void B01_InitiateVolume_DoesNotFire_WithoutCombatObjective()
    {
        SeedCombatMission();
        SeedTutorialMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);

        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        // Stand inside initiate volume (scale 50 around origin).
        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);

        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
        Assert.AreEqual(0, GetTriggerFireCount(map, InitiateVolumeCoid));
        Assert.IsNull(map.GetObjectByCoid(CombatVehicleCoid));
    }

    [TestMethod]
    public void B02_InitiateVolume_Fires_OnlyWithCombatObjectiveActive()
    {
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);

        character.CurrentQuests.Add(new CharacterQuest(CombatMissionId, CombatObjectiveSeq));
        var store = character.EnsureLogicVariables();
        Assert.AreEqual(1f, store.Get(VarHasActiveCombatObjective),
            "Type-12 var must be true when combat objective is active");

        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);

        Assert.IsTrue(GetTriggerFireCount(map, InitiateVolumeCoid) >= 1,
            "Initiate collision volume must fire with combat objective");
        // Cascade: Activate rem → delete dialog + create combat spawn marker.
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "Shared map keeps dialog NPC for other players");
        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Initiate cascade suppresses dialog for activator only");
        Assert.IsNotNull(map.GetObjectByCoid(CombatSpawnCoid) as SpawnPoint,
            "Initiate cascade Create places combat spawn");
    }

    [TestMethod]
    public void B03_InitiateVolume_OutOfRange_NoFire_EvenWithObjective()
    {
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);
        character.CurrentQuests.Add(new CharacterQuest(CombatMissionId, CombatObjectiveSeq));

        vehicle.Position = new Vector3(500, 0, 500);
        TriggerManager.Instance.CheckTriggersFor(vehicle);

        Assert.AreEqual(0, GetTriggerFireCount(map, InitiateVolumeCoid));
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
    }

    [TestMethod]
    public void B04_LogicVariable_Type12_OnlyMatchesExactObjectiveId()
    {
        SeedCombatMission();
        SeedTutorialMission();
        var (character, _, map) = CreatePlayer();
        SeedMapVars(map);
        var store = character.EnsureLogicVariables();

        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        Assert.AreEqual(0f, store.Get(VarHasActiveCombatObjective));

        character.CurrentQuests.Clear();
        character.CurrentQuests.Add(new CharacterQuest(CombatMissionId, CombatObjectiveSeq));
        Assert.AreEqual(1f, store.Get(VarHasActiveCombatObjective));
    }

    // ─── C. Activate cascade (legitimate Final Exam start) ──────────────────

    [TestMethod]
    public void C01_Activate_RemInitiator_DeletesDialog_CreatesCombatMarker()
    {
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);

        FireActivateOnRem(map, vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogCreatureCoid));
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
        Assert.IsNotNull(map.GetObjectByCoid(CombatSpawnCoid) as SpawnPoint);
        Assert.IsTrue(GetTriggerFireCount(map, RemInitiatorCoid) >= 1);
    }

    [TestMethod]
    public void C02_Activate_RemInitiator_DoesNotReFire_OnMissionReevalAfter()
    {
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);

        FireActivateOnRem(map, vehicle);
        var fires = GetTriggerFireCount(map, RemInitiatorCoid);

        character.CurrentQuests.Add(new CharacterQuest(CombatMissionId, CombatObjectiveSeq));
        for (var i = 0; i < 10; i++)
            TriggerManager.Instance.OnMissionStateChanged(vehicle);

        // Rem may re-fire only if Activate cascade runs again; pure re-eval must not add fires.
        // After first Activate, latch path may set hasdeleted — re-eval still must not call rem.
        Assert.AreEqual(fires, GetTriggerFireCount(map, RemInitiatorCoid));
    }

    [TestMethod]
    public void C03_RemInitiator_HasDeletedLatch_BlocksSecondActivateCascade()
    {
        // After first initiate, var hasdeleted=1; rem conditions (hasdeleted==0) fail.
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);

        FireActivateOnRem(map, vehicle);
        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogCreatureCoid));
        var firesAfterFirst = GetTriggerFireCount(map, RemInitiatorCoid);

        // Clear personal suppress as if client reloaded fam; latch still blocks rem.
        character.MapPresence.Clear();
        character.MapPresence.EnsureContinent(map.ContinentId);
        FireActivateOnRem(map, vehicle);

        Assert.AreEqual(firesAfterFirst, GetTriggerFireCount(map, RemInitiatorCoid),
            "Latch hasdeleted==1 must block rem FireTriggerReactions on second Activate");
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Second Activate must not re-suppress dialog when latch is set");
    }

    [TestMethod]
    public void C04_InitiateVolume_EdgeLatch_NoRefireWhileStillInside()
    {
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);
        character.CurrentQuests.Add(new CharacterQuest(CombatMissionId, CombatObjectiveSeq));
        vehicle.Position = new Vector3(0, 0, 0);

        TriggerManager.Instance.CheckTriggersFor(vehicle);
        var fires = GetTriggerFireCount(map, InitiateVolumeCoid);
        Assert.IsTrue(fires >= 1);

        // Still inside volume — edge latch must not re-fire.
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.AreEqual(fires, GetTriggerFireCount(map, InitiateVolumeCoid));
    }

    // ─── D. Full user-repro scenarios ───────────────────────────────────────

    [TestMethod]
    public void D01_UserRepro_TutorialProgressNearNpc_DoesNotStripDialogNpc()
    {
        // Fresh character: run tutorial chain, stand near dialog NPC, advance objectives.
        SeedCombatMission();
        SeedTutorialMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);

        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        vehicle.Position = new Vector3(0, 0, 0); // at NPC / initiate area

        for (var step = 0; step < 15; step++)
        {
            TriggerManager.Instance.CheckTriggersFor(vehicle);
            TriggerManager.Instance.OnMissionStateChanged(vehicle);
        }

        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "Dialog NPC must remain through entire pre-combat tutorial chain");
        Assert.IsNull(map.GetObjectByCoid(CombatVehicleCoid),
            "Pathing combat vehicle must not exist before combat mission accept+initiate");
        Assert.AreEqual(0, GetTriggerFireCount(map, RemInitiatorCoid));
        Assert.AreEqual(0, GetTriggerFireCount(map, InitiateVolumeCoid));
    }

    [TestMethod]
    public void D02_UserRepro_RelogHygiene_RestoresDialog_PurgesCombatCar()
    {
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        // Simulate leaked mid-Final-Exam world from a previous false fire:
        // dialog spawn marker missing, combat vehicle live on inactive spawn.
        PlaceLeakedCombatVehicle(map);
        Assert.IsNull(map.GetObjectByCoid(DialogCreatureCoid),
            "Fixture starts without dialog creature");
        Assert.IsNotNull(map.GetObjectByCoid(CombatVehicleCoid));

        // Solo enter hygiene (relog).
        map.ApplyAuthoredSpawnHygiene();

        Assert.IsNull(map.GetObjectByCoid(CombatVehicleCoid),
            "Relog hygiene must remove pathing combat car");
        Assert.IsNotNull(map.GetObjectByCoid(DialogSpawnCoid) as SpawnPoint,
            "Relog hygiene must restore fam-active dialog spawn marker");
    }

    [TestMethod]
    public void D03_UserRepro_AfterRelog_ObjectiveRetry_StillKeepsDialogNpc()
    {
        // After hygiene restore, redoing a previous objective must not re-strip NPC.
        SeedCombatMission();
        SeedTutorialMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceLeakedCombatVehicle(map);
        map.ApplyAuthoredSpawnHygiene();

        // Place dialog child for turn-in (Spawn may fail without clonebase; place manually).
        PlaceDialogCreature(map);
        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        vehicle.Position = new Vector3(0, 0, 0);

        for (var i = 0; i < 20; i++)
        {
            TriggerManager.Instance.CheckTriggersFor(vehicle);
            TriggerManager.Instance.OnMissionStateChanged(vehicle);
        }

        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
        Assert.IsNull(map.GetObjectByCoid(CombatVehicleCoid));
        Assert.AreEqual(0, GetTriggerFireCount(map, RemInitiatorCoid));
    }

    [TestMethod]
    public void D04_FullChain_AcceptCombat_EnterVolume_ThenDialogGone_CombatPresent()
    {
        SeedCombatMission();
        SeedTutorialMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);

        // Pre-combat tutorial safe — stand in volume but objective gate closed.
        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));

        // Accept combat mission away from volume so re-eval does not auto-initiate.
        vehicle.Position = new Vector3(500, 0, 500);
        character.CurrentQuests.Clear();
        character.CompletedMissionIds.Add(TutorialMissionId);
        character.CurrentQuests.Add(new CharacterQuest(CombatMissionId, CombatObjectiveSeq));
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "Accept combat mission out of volume must not strip dialog NPC");
        Assert.AreEqual(0, GetTriggerFireCount(map, InitiateVolumeCoid));

        // Drive into initiate volume with combat objective active.
        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Legitimate initiate suppresses dialog Gunny for activator");
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
        Assert.IsNotNull(map.GetObjectByCoid(CombatSpawnCoid) as SpawnPoint,
            "Combat spawn marker remains after Create cascade");
        Assert.IsTrue(GetTriggerFireCount(map, InitiateVolumeCoid) >= 1);
        Assert.IsTrue(GetTriggerFireCount(map, RemInitiatorCoid) >= 1);
    }

    [TestMethod]
    public void D05_LeaveMap_ResetsWorld_ForNextVisitor()
    {
        SeedCombatMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);
        character.CurrentQuests.Add(new CharacterQuest(CombatMissionId, CombatObjectiveSeq));
        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogCreatureCoid));

        character.SetMap(null);
        vehicle.SetMap(null);
        Assert.AreEqual(0, map.PlayerCount);
        Assert.IsNotNull(map.GetObjectByCoid(DialogSpawnCoid) as SpawnPoint,
            "Leave-reset restores fam-active dialog spawn marker");
    }

    // ─── E. Spawn load / OriginalIsActive / TE deferral ─────────────────────

    [TestMethod]
    public void E01_MapLoad_OnlyOriginalIsActive_SpawnsChildren()
    {
        var map = CreateMapOnly();
        map.MapData.Templates[DialogSpawnCoid] = MakeSpawnTemplate(DialogSpawnCoid, originalActive: true);
        map.MapData.Templates[CombatSpawnCoid] = MakeSpawnTemplate(CombatSpawnCoid, originalActive: false);
        // Simulate old Create mutation on shared template.
        map.MapData.Templates[CombatSpawnCoid].IsActive = true;

        map.InitializeLocalObjectsForTests();

        Assert.IsNotNull(map.GetObjectByCoid(DialogSpawnCoid) as SpawnPoint);
        Assert.IsNotNull(map.GetObjectByCoid(CombatSpawnCoid) as SpawnPoint);
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn(),
            "Fam-inactive combat spawn must not materialize children at load");
        Assert.IsFalse(SectorMap.ShouldSpawnChildrenAtMapLoad(map.MapData.Templates[CombatSpawnCoid]));
    }

    [TestMethod]
    public void E02_CreateDoesNotMutateOriginalIsActive()
    {
        var map = CreateMapOnly();
        map.MapData.Templates[CombatSpawnCoid] = MakeSpawnTemplate(CombatSpawnCoid, originalActive: false);
        var (character, vehicle, _) = CreatePlayerOn(map);

        var create = PlaceReaction(map, CreateCombatCoid, ReactionType.Create, CombatSpawnCoid);
        Assert.IsTrue(create.TriggerIfPossible(vehicle));
        Assert.IsFalse(map.MapData.Templates[CombatSpawnCoid].OriginalIsActive);
        Assert.IsFalse(SectorMap.ShouldSpawnChildrenAtMapLoad(map.MapData.Templates[CombatSpawnCoid]));
    }

    [TestMethod]
    public void E03_TriggerEvents_DeferredWhenCreateTargetsFar()
    {
        var map = CreateMapOnly();
        TriggerManager.Instance.ClearAllForTests();

        var combatTpl = MakeSpawnTemplate(CombatSpawnCoid, originalActive: true);
        combatTpl.TriggerEvents = new long[] { 15818, -1, -1 };
        combatTpl.Location = new Vector4(0, 0, 0, 0);
        map.MapData.Templates[CombatSpawnCoid] = combatTpl;

        var padTpl = MakeSpawnTemplate(PadSpawnCoid, originalActive: false);
        padTpl.Location = new Vector4(90, 0, 0, 0);
        map.MapData.Templates[PadSpawnCoid] = padTpl;

        var createPad = new ReactionTemplate
        {
            COID = 15819,
            Name = "create_pad",
            ReactionType = ReactionType.Create,
        };
        createPad.Objects.Add(PadSpawnCoid);
        var createRx = new Reaction(createPad);
        createRx.SetCoid(15819, false);
        createRx.SetMap(map);

        var teTrig = new TriggerTemplate
        {
            COID = 15818,
            ActivationCount = -1,
            DoOnActivate = true,
        };
        teTrig.Reactions.Add(15819);
        var te = new Trigger(teTrig);
        te.SetCoid(15818, false);
        te.SetMap(map);

        var spawn = new SpawnPoint(combatTpl);
        spawn.SetCoid(CombatSpawnCoid, false);
        spawn.SetMap(map);

        var vehicle = new Vehicle();
        vehicle.SetCoid(700, true);
        vehicle.Position = new Vector3(0, 0, 0);
        vehicle.SetMap(map);

        spawn.FireAuthoredTriggerEvents(vehicle);
        Assert.IsTrue(spawn.HasDeferredAuthoredTriggerEvents);
        Assert.IsNull(map.GetObjectByCoid(PadSpawnCoid));

        vehicle.Position = new Vector3(90, 0, 0);
        spawn.TryFlushDeferredAuthoredTriggerEvents(vehicle);
        Assert.IsFalse(spawn.HasDeferredAuthoredTriggerEvents);
        Assert.IsNotNull(map.GetObjectByCoid(PadSpawnCoid) as SpawnPoint);
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestMethod]
    public void E04_Hygiene_SoloEnter_IsIdempotent()
    {
        var map = CreateMapOnly();
        map.MapData.Templates[DialogSpawnCoid] = MakeSpawnTemplate(DialogSpawnCoid, originalActive: true);
        map.MapData.Templates[CombatSpawnCoid] = MakeSpawnTemplate(CombatSpawnCoid, originalActive: false);
        PlaceLeakedCombatVehicleOn(map);
        map.ApplyAuthoredSpawnHygiene();
        map.ApplyAuthoredSpawnHygiene();
        map.ApplyAuthoredSpawnHygiene();

        Assert.IsNull(map.GetObjectByCoid(CombatVehicleCoid));
        Assert.IsNotNull(map.GetObjectByCoid(DialogSpawnCoid));
    }

    // ─── F. Stress / interleaving ───────────────────────────────────────────

    [TestMethod]
    public void F01_Interleaved_Movement_MissionReeval_VariableSet_Safe()
    {
        SeedCombatMission();
        SeedTutorialMission();
        var (character, vehicle, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);
        character.CurrentQuests.Add(new CharacterQuest(TutorialMissionId, 0));
        character.EnsureLogicVariables();

        var rng = new Random(42);
        for (var i = 0; i < 50; i++)
        {
            vehicle.Position = new Vector3(rng.Next(-5, 5), 0, rng.Next(-5, 5));
            TriggerManager.Instance.CheckTriggersFor(vehicle);
            if (i % 3 == 0)
                TriggerManager.Instance.OnMissionStateChanged(vehicle);
            if (i % 5 == 0)
                TriggerManager.Instance.OnVariableChanged(vehicle, VarConstZero);
        }

        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
        Assert.IsNull(map.GetObjectByCoid(CombatVehicleCoid));
        Assert.AreEqual(0, GetTriggerFireCount(map, RemInitiatorCoid));
    }

    [TestMethod]
    public void F02_TwoPlayers_SecondJoin_DoesNotPurgeFirstPlayersCombatMidMission()
    {
        // Hygiene only runs on PlayerCount==1; second join must not wipe mid-mission combat.
        SeedCombatMission();
        var (p1, v1, map) = CreateFullMapGraph();
        PlaceDialogCreature(map);
        p1.CurrentQuests.Add(new CharacterQuest(CombatMissionId, CombatObjectiveSeq));
        v1.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(v1);
        Assert.IsTrue(p1.MapPresence.IsSuppressed(DialogCreatureCoid));
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "P1 personal delete must leave shared dialog for P2");

        // Second player joins — must still see dialog (not suppressed for them).
        var p2 = new Character();
        p2.SetCoid(250, true);
        var v2 = new Vehicle();
        v2.SetCoid(251, true);
        p2.SetCurrentVehicleForTests(v2);
        p2.SetMap(map);
        v2.SetMap(map);
        Assert.AreEqual(2, map.PlayerCount);
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "P2 must still have shared dialog NPC available");
        Assert.IsFalse(p2.MapPresence.IsSuppressed(DialogCreatureCoid),
            "P2 has not progressed — dialog not suppressed for them");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static int GetTriggerFireCount(SectorMap map, long coid)
        => map.GetObjectByCoid(coid) is Trigger t ? t.FireCount : 0;

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateFullMapGraph()
    {
        var (character, vehicle, map) = CreatePlayer();
        SeedMapVars(map);
        BuildInitiateGraph(map);
        // Fam-active dialog + fam-inactive combat markers.
        map.MapData.Templates[DialogSpawnCoid] = MakeSpawnTemplate(DialogSpawnCoid, originalActive: true);
        map.MapData.Templates[CombatSpawnCoid] = MakeSpawnTemplate(CombatSpawnCoid, originalActive: false);
        // Place markers without children (except tests that PlaceDialogCreature).
        PlaceSpawnMarker(map, DialogSpawnCoid);
        PlaceSpawnMarker(map, CombatSpawnCoid);
        return (character, vehicle, map);
    }

    private void BuildInitiateGraph(SectorMap map)
    {
        // Delete dialog spawn marker + child
        PlaceDeleteReaction(map, DelDialogCoid, DialogSpawnCoid);
        // Create combat spawn
        PlaceReaction(map, CreateCombatCoid, ReactionType.Create, CombatSpawnCoid);
        // Activate combat spawn (spawn children when assets exist)
        PlaceReaction(map, ActCombatCoid, ReactionType.Activate, CombatSpawnCoid);
        // VariableSet hasdeleted = 1 (const one)
        var setTpl = new ReactionTemplate
        {
            COID = (int)SetDeletedLatchCoid,
            ReactionType = ReactionType.VariableSet,
            GenericVar1 = VarHasDeletedDialog,
            GenericVar3 = VarConstOne,
        };
        var setRx = new Reaction(setTpl);
        setRx.SetCoid(SetDeletedLatchCoid, false);
        setRx.SetMap(map);

        // Rem initiator: Activate-only → Delete, Create, Activate, SetVar
        var remTpl = new TriggerTemplate
        {
            COID = (int)RemInitiatorCoid,
            Name = "l1_rem_gunnysioux_initiator",
            TargetType = TriggerTargetType.Players,
            Scale = 2f,
            DoCollision = false,
            DoConditionals = false,
            DoOnActivate = true,
            AllConditionsNeeded = false,
            ActivationCount = -1,
        };
        remTpl.Reactions.Add(DelDialogCoid);
        remTpl.Reactions.Add(CreateCombatCoid);
        remTpl.Reactions.Add(ActCombatCoid);
        remTpl.Reactions.Add(SetDeletedLatchCoid);
        remTpl.Conditions.Add(new TriggerConditional
        {
            LeftId = VarHasDeletedDialog,
            RightId = VarConstZero,
            Type = ConditionalType.EqualTo,
        });
        var rem = new Trigger(remTpl);
        rem.SetCoid(RemInitiatorCoid, false);
        rem.Position = new Vector3(0, 0, 0);
        rem.Scale = 2f;
        rem.SetMap(map);

        // Activate reaction used by initiate volume
        PlaceReaction(map, ActStartCoid, ReactionType.Activate, RemInitiatorCoid);

        // Initiate collision volume: HasActiveCombatObjective == 1
        var volTpl = new TriggerTemplate
        {
            COID = (int)InitiateVolumeCoid,
            Name = "l1_coll_initiategunnysioux",
            TargetType = TriggerTargetType.Players,
            Scale = 50f,
            DoCollision = true,
            DoConditionals = true,
            DoOnActivate = true,
            AllConditionsNeeded = true,
            ActivationCount = -1,
        };
        volTpl.Reactions.Add(ActStartCoid);
        volTpl.Conditions.Add(new TriggerConditional
        {
            LeftId = VarHasActiveCombatObjective,
            RightId = VarConstOne,
            Type = ConditionalType.EqualTo,
        });
        var vol = new Trigger(volTpl);
        vol.SetCoid(InitiateVolumeCoid, false);
        vol.Position = new Vector3(0, 0, 0);
        vol.Scale = 50f;
        vol.SetMap(map);
    }

    private void PlaceRemInitiator(SectorMap map, bool includeAlwaysTrueCondition)
    {
        PlaceDeleteReaction(map, DelDialogCoid, DialogCreatureCoid);
        var remTpl = new TriggerTemplate
        {
            COID = (int)RemInitiatorCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 2f,
            DoCollision = false,
            DoConditionals = false,
            DoOnActivate = true,
            ActivationCount = -1,
        };
        remTpl.Reactions.Add(DelDialogCoid);
        if (includeAlwaysTrueCondition)
        {
            remTpl.Conditions.Add(new TriggerConditional
            {
                LeftId = VarConstZero,
                RightId = VarConstZero,
                Type = ConditionalType.EqualTo,
            });
        }

        var rem = new Trigger(remTpl);
        rem.SetCoid(RemInitiatorCoid, false);
        rem.Position = new Vector3(0, 0, 0);
        rem.Scale = 2f;
        rem.SetMap(map);
    }

    private void PlaceCollisionAlwaysTrue(SectorMap map, long coid, long deleteTarget)
    {
        const long rx = 99200;
        PlaceDeleteReaction(map, rx, deleteTarget);
        var tpl = new TriggerTemplate
        {
            COID = (int)coid,
            TargetType = TriggerTargetType.Players,
            Scale = 50f,
            DoCollision = true,
            DoConditionals = true,
            AllConditionsNeeded = true,
            ActivationCount = -1,
        };
        tpl.Reactions.Add(rx);
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = VarConstZero,
            RightId = VarConstZero,
            Type = ConditionalType.EqualTo,
        });
        var t = new Trigger(tpl);
        t.SetCoid(coid, false);
        t.Position = new Vector3(0, 0, 0);
        t.Scale = 50f;
        t.SetMap(map);
    }

    private static long _activateRxSerial = 98060;

    private void FireActivateOnRem(SectorMap map, Vehicle vehicle)
    {
        // Reuse ActStart from the graph when present (initiate wiring).
        if (map.GetObjectByCoid(ActStartCoid) is Reaction existing)
        {
            Assert.IsTrue(existing.TriggerIfPossible(vehicle));
            return;
        }

        var coid = System.Threading.Interlocked.Increment(ref _activateRxSerial);
        var actTpl = new ReactionTemplate
        {
            COID = (int)coid,
            ReactionType = ReactionType.Activate,
        };
        actTpl.Objects.Add(RemInitiatorCoid);
        var act = new Reaction(actTpl);
        act.SetCoid(coid, false);
        act.SetMap(map);
        Assert.IsTrue(act.TriggerIfPossible(vehicle));
    }

    private void PlaceDialogCreature(SectorMap map)
    {
        if (map.GetObjectByCoid(DialogSpawnCoid) is not SpawnPoint)
            PlaceSpawnMarker(map, DialogSpawnCoid);

        var creature = new Creature();
        creature.SetCoid(DialogCreatureCoid, true);
        creature.SpawnOwner = DialogSpawnCoid;
        creature.IsMissionGiver = true;
        creature.Position = new Vector3(0, 0, 0);
        creature.SetMap(map);
        if (map.GetObjectByCoid(DialogSpawnCoid) is SpawnPoint sp)
            sp.SetLastSpawnedCoidForTests(DialogCreatureCoid);
    }

    private void PlaceDeleteDialogReaction(SectorMap map)
        => PlaceDeleteReaction(map, DelDialogCoid, DialogCreatureCoid);

    private void PlaceLeakedCombatVehicle(SectorMap map)
        => PlaceLeakedCombatVehicleOn(map);

    private void PlaceLeakedCombatVehicleOn(SectorMap map)
    {
        if (map.GetObjectByCoid(CombatSpawnCoid) is not SpawnPoint)
            PlaceSpawnMarker(map, CombatSpawnCoid);

        var vehicle = new Vehicle();
        vehicle.SetCoid(CombatVehicleCoid, true);
        vehicle.SpawnOwnerCoid = CombatSpawnCoid;
        vehicle.Position = new Vector3(10, 0, 0);
        vehicle.SetMap(map);
        if (map.GetObjectByCoid(CombatSpawnCoid) is SpawnPoint sp)
            sp.SetLastSpawnedCoidForTests(CombatVehicleCoid);
    }

    private static void PlaceSpawnMarker(SectorMap map, long coid)
    {
        if (!map.MapData.Templates.ContainsKey(coid))
            map.MapData.Templates[coid] = MakeSpawnTemplate(coid, originalActive: coid == DialogSpawnCoid);

        var tpl = (SpawnPointTemplate)map.MapData.Templates[coid];
        var sp = new SpawnPoint(tpl);
        sp.SetCoid(coid, false);
        sp.Position = tpl.Location.ToVector3();
        sp.SetMap(map);
    }

    private static void PlaceSimpleObject(SectorMap map, long coid, Vector3 pos)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = pos;
        obj.SetMap(map);
    }

    private static void PlaceDeleteReaction(SectorMap map, long reactionCoid, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Delete,
        };
        tpl.Objects.Add(objectCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static Reaction PlaceReaction(SectorMap map, long reactionCoid, ReactionType type, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            Name = type.ToString(),
            ReactionType = type,
        };
        tpl.Objects.Add(objectCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static SpawnPointTemplate MakeSpawnTemplate(long coid, bool originalActive)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)coid,
            IsActive = originalActive,
            OriginalIsActive = originalActive,
            TriggerEvents = new long[] { -1, -1, -1 },
            Location = new Vector4(0, 0, 0, 0),
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = -1,
            IsTemplate = false,
        });
        return tpl;
    }

    private void SeedMapVars(SectorMap map)
    {
        map.MapData.Variables[VarHasActiveCombatObjective] = Variable.CreateForTests(
            VarHasActiveCombatObjective,
            LogicVariableStore.TypeHasActiveObjective,
            CombatObjectiveId,
            CombatObjectiveId,
            "has_active_combat_obj");
        map.MapData.Variables[VarConstOne] = Variable.CreateForTests(
            VarConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "const_1");
        map.MapData.Variables[VarConstZero] = Variable.CreateForTests(
            VarConstZero, LogicVariableStore.TypeConstant, 0f, 0f, "const_0");
        map.MapData.Variables[VarHasDeletedDialog] = Variable.CreateForTests(
            VarHasDeletedDialog, LogicVariableStore.TypeConstant, 0f, 0f, "hasdeleted");
    }

    private static void SeedCombatMission()
    {
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(
                CombatMissionId,
                MissionObjective.CreateForTests(CombatObjectiveId, CombatObjectiveSeq, CombatMissionId, 1)));
    }

    private static void SeedTutorialMission()
    {
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(
                TutorialMissionId,
                MissionObjective.CreateForTests(TutorialObjectiveId, 0, TutorialMissionId, 1)));
    }

    private SectorMap CreateMapOnly()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_tutorial_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var map = CreateMapOnly();
        return CreatePlayerOn(map);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayerOn(SectorMap map)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(150, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(151, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        SeedMapVars(map);
        character.EnsureLogicVariables();
        return (character, vehicle, map);
    }
}
