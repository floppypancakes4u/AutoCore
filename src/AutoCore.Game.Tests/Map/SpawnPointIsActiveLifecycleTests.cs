using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Map-authored SpawnPoint.IsActive gates child materialization (not placement of the spawn marker).
/// Inactive spawns wait for Create/Activate; active dialog spawns load immediately.
/// </summary>
[TestClass]
public class SpawnPointIsActiveLifecycleTests
{
    private const int ContId = 811;

    [TestMethod]
    public void ShouldSpawnChildrenAtMapLoad_TrueForActiveSpawnPoint()
    {
        var tpl = new SpawnPointTemplate { COID = 100, IsActive = true, OriginalIsActive = true };
        Assert.IsTrue(SectorMap.ShouldSpawnChildrenAtMapLoad(tpl));
    }

    [TestMethod]
    public void ShouldSpawnChildrenAtMapLoad_FalseForInactiveSpawnPoint()
    {
        var tpl = new SpawnPointTemplate { COID = 101, IsActive = false, OriginalIsActive = false };
        Assert.IsFalse(SectorMap.ShouldSpawnChildrenAtMapLoad(tpl));
    }

    [TestMethod]
    public void ShouldSpawnChildrenAtMapLoad_UsesOriginalIsActive_NotMutatedIsActive()
    {
        // Create used to set IsActive=true on shared MapData; map load must still honor fam.
        var tpl = new SpawnPointTemplate
        {
            COID = 14138,
            IsActive = true, // mutated by reaction
            OriginalIsActive = false, // fam inactive combat spawn
        };
        Assert.IsFalse(SectorMap.ShouldSpawnChildrenAtMapLoad(tpl),
            "Combat Gunny must not spawn children at map load after Create mutated IsActive");
    }

    [TestMethod]
    public void ShouldSpawnChildrenAtMapLoad_TrueForNonSpawnTemplates()
    {
        var gfx = new GraphicsObjectTemplate(GraphicsObjectType.Graphics) { COID = 102, IsActive = false };
        Assert.IsTrue(SectorMap.ShouldSpawnChildrenAtMapLoad(gfx));
    }

    [TestMethod]
    public void ApplyAuthoredSpawnHygiene_SoloEnter_PurgesCombatCar_RestoresDialogGunny()
    {
        // Leaked Final Exam state: standing Gunny deleted, pathing combat vehicle still live.
        // Solo re-enter must restore Guns-of-the-Expansion turn-in NPC and remove pathing car.
        var map = CreateMap();
        const long dialogCoid = 14090;
        const long combatCoid = 14138;
        const long combatVehicleCoid = 90010;

        map.MapData.Templates[dialogCoid] = MakeSpawnTemplate(dialogCoid, isActive: true);
        map.MapData.Templates[combatCoid] = MakeSpawnTemplate(combatCoid, isActive: false);

        // Place combat spawn with leaked live vehicle (as after Create/Activate).
        var combatTpl = (SpawnPointTemplate)map.MapData.Templates[combatCoid];
        var combatSpawn = new SpawnPoint(combatTpl);
        combatSpawn.SetCoid(combatCoid, false);
        combatSpawn.SetMap(map);
        var combatVehicle = new Vehicle();
        combatVehicle.SetCoid(combatVehicleCoid, true);
        combatVehicle.SpawnOwnerCoid = combatCoid;
        combatVehicle.SetMap(map);
        combatSpawn.SetLastSpawnedCoidForTests(combatVehicleCoid);

        // Dialog spawn marker missing (deleted).
        Assert.IsNull(map.GetObjectByCoid(dialogCoid));
        Assert.IsNotNull(map.GetObjectByCoid(combatVehicleCoid));

        map.ApplyAuthoredSpawnHygiene();

        Assert.IsNull(map.GetObjectByCoid(combatVehicleCoid),
            "Pathing combat vehicle must not persist into pre-mission turn-in");
        Assert.IsNotNull(map.GetObjectByCoid(dialogCoid) as SpawnPoint,
            "Standing Gunny spawn marker must be restored for turn-in");
    }

    [TestMethod]
    public void ResetLocalWorldToAuthored_RestoresDialogSpawn_AfterDeleteAndCombatCreate()
    {
        // After Final Exam initiate, standing Gunny is deleted and combat spawn exists.
        // Next visitor turning in Guns of the Expansion must see standing Gunny again.
        var map = CreateMap();
        const long dialogCoid = 14090;
        const long combatCoid = 14138;

        map.MapData.Templates[dialogCoid] = MakeSpawnTemplate(dialogCoid, isActive: true);
        map.MapData.Templates[combatCoid] = MakeSpawnTemplate(combatCoid, isActive: false);
        // Simulate legacy Create mutation on shared template
        map.MapData.Templates[combatCoid].IsActive = true;

        map.InitializeLocalObjectsForTests();
        Assert.IsNotNull(map.GetObjectByCoid(dialogCoid) as SpawnPoint);

        // Delete standing Gunny (mission initiate)
        var del = PlaceReaction(map, 14133, ReactionType.Delete, dialogCoid);
        var player = new Character();
        player.SetCoid(300, true);
        player.SetMap(map);
        Assert.IsTrue(del.TriggerIfPossible(player));
        // Personal Delete: shared map keeps dialog; only this character is suppressed.
        Assert.IsNotNull(map.GetObjectByCoid(dialogCoid) as SpawnPoint);
        Assert.IsTrue(player.MapPresence.IsSuppressed(dialogCoid));

        // Create combat spawn marker
        var create = PlaceReaction(map, 14139, ReactionType.Create, combatCoid);
        Assert.IsTrue(create.TriggerIfPossible(player));
        Assert.IsNotNull(map.GetObjectByCoid(combatCoid));
        Assert.IsTrue(player.MapPresence.IsMaterialized(combatCoid));

        // Last player leaves → reset
        player.SetMap(null);
        Assert.AreEqual(0, map.PlayerCount);

        Assert.IsNotNull(map.GetObjectByCoid(dialogCoid) as SpawnPoint,
            "Reset must restore fam-active standing Gunny spawn");
        Assert.IsNotNull(map.GetObjectByCoid(combatCoid) as SpawnPoint,
            "Inactive combat spawn marker is placed at load");
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(combatCoid)).HasLiveSpawn(),
            "Combat children must not be live after reset without mission Create");
        Assert.IsFalse(map.MapData.Templates[combatCoid].IsActive,
            "Shared template IsActive restored to OriginalIsActive");
    }

    [TestMethod]
    public void InitializeLocalObjects_PlacesInactiveSpawnPoint_ButDoesNotRequireChildren()
    {
        var map = CreateMap();
        const long inactiveCoid = 14138;
        const long activeCoid = 14090;

        map.MapData.Templates[inactiveCoid] = MakeSpawnTemplate(inactiveCoid, isActive: false);
        map.MapData.Templates[activeCoid] = MakeSpawnTemplate(activeCoid, isActive: true);

        map.InitializeLocalObjectsForTests();

        Assert.IsNotNull(map.GetObjectByCoid(inactiveCoid) as SpawnPoint,
            "Inactive SpawnPoint marker must still be placed for Create/Activate");
        Assert.IsNotNull(map.GetObjectByCoid(activeCoid) as SpawnPoint,
            "Active SpawnPoint must be placed at map load");
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(inactiveCoid)).HasLiveSpawn(),
            "Inactive spawn must not materialize children at load");
    }

    [TestMethod]
    public void Create_PlacesAndTracksInactiveSpawnPoint()
    {
        var map = CreateMap();
        const long spawnCoid = 14138;
        const long reactionCoid = 14139;

        map.MapData.Templates[spawnCoid] = MakeSpawnTemplate(spawnCoid, isActive: false);
        Assert.IsNull(map.GetObjectByCoid(spawnCoid));

        var reaction = PlaceReaction(map, reactionCoid, ReactionType.Create, spawnCoid);
        var activator = new Character();
        activator.SetCoid(200, true);
        activator.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(activator));
        var spawn = map.GetObjectByCoid(spawnCoid) as SpawnPoint;
        Assert.IsNotNull(spawn, "Create places inactive SpawnPoint on the map");
        Assert.IsFalse(map.MapData.Templates[spawnCoid].OriginalIsActive,
            "Fam OriginalIsActive must stay false (no shared MapData mutation)");
    }

    [TestMethod]
    public void Create_WhenSpawnPointAlreadyOnMapWithoutLiveSpawn_CallsSpawnAgain()
    {
        var map = CreateMap();
        const long spawnCoid = 14138;
        const long reactionCoid = 14139;

        var template = MakeSpawnTemplate(spawnCoid, isActive: true);
        template.Spawns.Clear();
        template.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = -1, IsTemplate = false });
        map.MapData.Templates[spawnCoid] = template;

        var spawn = new SpawnPoint(template);
        spawn.SetCoid(spawnCoid, false);
        spawn.SetMap(map);
        Assert.IsFalse(spawn.HasLiveSpawn());

        var reaction = PlaceReaction(map, reactionCoid, ReactionType.Create, spawnCoid);
        var activator = new Character();
        activator.SetCoid(201, true);
        activator.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(activator));
        Assert.IsNotNull(map.GetObjectByCoid(spawnCoid) as SpawnPoint);
    }

    [TestMethod]
    public void Activate_SpawnPointWithoutLiveSpawn_SpawnsChildren()
    {
        var map = CreateMap();
        const long spawnCoid = 14138;
        const long reactionCoid = 14142;

        var template = MakeSpawnTemplate(spawnCoid, isActive: false);
        map.MapData.Templates[spawnCoid] = template;

        var spawn = new SpawnPoint(template);
        spawn.SetCoid(spawnCoid, false);
        spawn.SetMap(map);
        Assert.IsFalse(spawn.HasLiveSpawn());

        var reaction = PlaceReaction(map, reactionCoid, ReactionType.Activate, spawnCoid);
        var activator = new Character();
        activator.SetCoid(204, true);
        activator.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(activator));
        Assert.IsFalse(template.OriginalIsActive,
            "Activate must not mutate shared fam OriginalIsActive");
        // Spawn may fail without clonebase assets; reaction path still runs.
    }

    [TestMethod]
    public void Delete_SpawnPoint_PersonalSuppressesMissionGiver_SharedMapKeepsIt()
    {
        // Final Exam: l1_del_gunnysioux1 targets spawn 14090 — personal presence only.
        // Shared map keeps standing Gunny so other players can still turn in earlier missions.
        var map = CreateMap();
        const long spawnCoid = 14090;
        const long creatureCoid = 90001;
        const long reactionCoid = 14133;

        var template = MakeSpawnTemplate(spawnCoid, isActive: true);
        map.MapData.Templates[spawnCoid] = template;

        var spawn = new SpawnPoint(template);
        spawn.SetCoid(spawnCoid, false);
        spawn.SetMap(map);

        var creature = new Creature();
        creature.SetCoid(creatureCoid, true);
        creature.SpawnOwner = spawnCoid;
        creature.IsMissionGiver = true;
        creature.SetMap(map);
        spawn.SetLastSpawnedCoidForTests(creatureCoid);

        var reaction = PlaceReaction(map, reactionCoid, ReactionType.Delete, spawnCoid);
        var activator = new Character();
        activator.SetCoid(202, true);
        activator.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(activator));
        Assert.IsNotNull(map.GetObjectByCoid(spawnCoid) as SpawnPoint,
            "SpawnPoint marker stays on shared map");
        Assert.IsNotNull(map.GetObjectByCoid(creatureCoid),
            "Mission-giver stays on shared map for other players");
        Assert.IsTrue(activator.MapPresence.IsSuppressed(spawnCoid));
        Assert.IsTrue(activator.MapPresence.IsSuppressed(creatureCoid),
            "Activator must not interact with place-A giver after personal delete");
    }

    [TestMethod]
    public void Delete_SpawnPoint_PersonalSuppressesOwnedVehicle_SharedMapKeepsIt()
    {
        var map = CreateMap();
        const long spawnCoid = 14138;
        const long vehicleCoid = 90002;
        const long reactionCoid = 14133;

        var template = MakeSpawnTemplate(spawnCoid, isActive: true);
        map.MapData.Templates[spawnCoid] = template;

        var spawn = new SpawnPoint(template);
        spawn.SetCoid(spawnCoid, false);
        spawn.SetMap(map);

        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid, true);
        vehicle.SpawnOwnerCoid = spawnCoid;
        vehicle.SetMap(map);
        spawn.SetLastSpawnedCoidForTests(vehicleCoid);

        var playerVehicle = new Vehicle();
        playerVehicle.SetCoid(90003, true);
        playerVehicle.SpawnOwnerCoid = -1;
        playerVehicle.SetMap(map);

        var reaction = PlaceReaction(map, reactionCoid, ReactionType.Delete, spawnCoid);
        var activator = new Character();
        activator.SetCoid(203, true);
        activator.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(activator));
        Assert.IsNotNull(map.GetObjectByCoid(vehicleCoid),
            "NPC vehicle stays on shared map under personal delete (Phase 3: private combat)");
        Assert.IsTrue(activator.MapPresence.IsSuppressed(vehicleCoid));
        Assert.IsNotNull(map.GetObjectByCoid(90003), "Player vehicle must remain");
    }

    [TestMethod]
    public void FireAuthoredTriggerEvents_InRange_FiresCreateForPadSetup()
    {
        // Combat spawn TE → Create pad Gunny when activator is already near Create targets.
        TriggerManager.Instance.ClearAllForTests();
        var map = CreateMap();
        const long combatSpawnCoid = 14138;
        const long triggerCoid = 15818;
        const long createReactionCoid = 15819;
        const long gunny2SpawnCoid = 15820;

        var combatTpl = MakeSpawnTemplate(combatSpawnCoid, isActive: true);
        combatTpl.TriggerEvents = new long[] { triggerCoid, -1, -1 };
        map.MapData.Templates[combatSpawnCoid] = combatTpl;

        var padTpl = MakeSpawnTemplate(gunny2SpawnCoid, isActive: false);
        padTpl.Location = new Vector4(10f, 0f, 10f, 0f);
        map.MapData.Templates[gunny2SpawnCoid] = padTpl;

        PlaceCreateTriggerChain(map, triggerCoid, createReactionCoid, gunny2SpawnCoid);

        var spawn = new SpawnPoint(combatTpl);
        spawn.SetCoid(combatSpawnCoid, false);
        spawn.SetMap(map);

        var activator = new Character();
        activator.SetCoid(205, true);
        activator.Position = new Vector3(10f, 0f, 10f); // at pad
        activator.SetMap(map);

        Assert.IsNull(map.GetObjectByCoid(gunny2SpawnCoid));
        spawn.FireAuthoredTriggerEvents(activator);

        Assert.IsFalse(spawn.HasDeferredAuthoredTriggerEvents);
        Assert.IsNotNull(map.GetObjectByCoid(gunny2SpawnCoid) as SpawnPoint,
            "In-range TriggerEvents must Create pad Gunny spawn");
        Assert.IsFalse(map.MapData.Templates[gunny2SpawnCoid].OriginalIsActive,
            "Create must not rewrite fam OriginalIsActive on shared MapData");
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestMethod]
    public void FireAuthoredTriggerEvents_FarFromCreateTargets_DefersUntilApproach()
    {
        // Combat start TE must not Create pad objects while player is still at the fight
        // (otherwise air-drop animation finishes off-screen; vehicle sits at pad early).
        TriggerManager.Instance.ClearAllForTests();
        var map = CreateMap();
        const long combatSpawnCoid = 14138;
        const long triggerCoid = 15818;
        const long createReactionCoid = 15819;
        const long gunny2SpawnCoid = 15820;

        var combatTpl = MakeSpawnTemplate(combatSpawnCoid, isActive: true);
        combatTpl.TriggerEvents = new long[] { triggerCoid, -1, -1 };
        combatTpl.Location = new Vector4(0f, 0f, 0f, 0f);
        map.MapData.Templates[combatSpawnCoid] = combatTpl;

        var padTpl = MakeSpawnTemplate(gunny2SpawnCoid, isActive: false);
        // ~90u away — past TriggerEventProximity (45)
        padTpl.Location = new Vector4(90f, 0f, 0f, 0f);
        map.MapData.Templates[gunny2SpawnCoid] = padTpl;

        PlaceCreateTriggerChain(map, triggerCoid, createReactionCoid, gunny2SpawnCoid);

        var spawn = new SpawnPoint(combatTpl);
        spawn.SetCoid(combatSpawnCoid, false);
        spawn.Position = new Vector3(0f, 0f, 0f);
        spawn.SetMap(map);

        var vehicle = new Vehicle();
        vehicle.SetCoid(220, true);
        vehicle.Position = new Vector3(0f, 0f, 0f); // combat site
        vehicle.SetMap(map);

        spawn.FireAuthoredTriggerEvents(vehicle);
        Assert.IsTrue(spawn.HasDeferredAuthoredTriggerEvents, "Far Create targets defer TE");
        Assert.IsNull(map.GetObjectByCoid(gunny2SpawnCoid), "Pad must not materialize at combat start");

        // Approach pad
        vehicle.Position = new Vector3(90f, 0f, 0f);
        spawn.TryFlushDeferredAuthoredTriggerEvents(vehicle);

        Assert.IsFalse(spawn.HasDeferredAuthoredTriggerEvents);
        Assert.IsNotNull(map.GetObjectByCoid(gunny2SpawnCoid) as SpawnPoint,
            "Approach flush Creates pad setup (air-drop / Gunny2)");
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestMethod]
    public void NotifySpawnedChildDied_RequestsTriggerEvents()
    {
        TriggerManager.Instance.ClearAllForTests();
        var map = CreateMap();
        const long combatSpawnCoid = 14138;
        const long triggerCoid = 15818;
        const long createReactionCoid = 15819;
        const long gunny2SpawnCoid = 15820;

        var combatTpl = MakeSpawnTemplate(combatSpawnCoid, isActive: true);
        combatTpl.TriggerEvents = new long[] { triggerCoid, -1, -1 };
        map.MapData.Templates[combatSpawnCoid] = combatTpl;

        var padTpl = MakeSpawnTemplate(gunny2SpawnCoid, isActive: false);
        padTpl.Location = new Vector4(5f, 0f, 0f, 0f);
        map.MapData.Templates[gunny2SpawnCoid] = padTpl;

        PlaceCreateTriggerChain(map, triggerCoid, createReactionCoid, gunny2SpawnCoid);

        var spawn = new SpawnPoint(combatTpl);
        spawn.SetCoid(combatSpawnCoid, false);
        spawn.SetMap(map);

        var dying = new Vehicle();
        dying.SetCoid(221, true);
        dying.SpawnOwnerCoid = combatSpawnCoid;
        dying.Position = new Vector3(5f, 0f, 0f);
        dying.SetMap(map);

        var killer = new Vehicle();
        killer.SetCoid(222, true);
        killer.Position = new Vector3(5f, 0f, 0f);
        killer.SetMap(map);

        spawn.NotifySpawnedChildDied(dying, killer);
        Assert.IsNotNull(map.GetObjectByCoid(gunny2SpawnCoid));
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestMethod]
    public void Spawn_DefaultDoesNotRequestTriggerEvents()
    {
        // Map load must not request TE (boot cascades Delete standing mission givers).
        var map = CreateMap();
        var template = MakeSpawnTemplate(14138, isActive: true);
        template.TriggerEvents = new long[] { 15818, -1, -1 };
        map.MapData.Templates[14138] = template;

        var spawn = new SpawnPoint(template);
        spawn.SetCoid(14138, false);
        spawn.SetMap(map);
        spawn.Spawn();
        Assert.IsFalse(spawn.LastSpawnRequestedFireTriggerEvents);
    }

    static void PlaceCreateTriggerChain(SectorMap map, long triggerCoid, long createReactionCoid, long createObjectCoid)
    {
        var createTpl = new ReactionTemplate
        {
            COID = (int)createReactionCoid,
            Name = "l1_create_gunny2copy",
            ReactionType = ReactionType.Create,
        };
        createTpl.Objects.Add(createObjectCoid);
        var createRx = new Reaction(createTpl);
        createRx.SetCoid(createReactionCoid, false);
        createRx.SetMap(map);

        var triggerTpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            Name = "l1_rem_creates_gunny2",
            ActivationCount = -1,
            DoOnActivate = true,
        };
        triggerTpl.Reactions.Add(createReactionCoid);
        var trigger = new Trigger(triggerTpl);
        trigger.SetCoid(triggerCoid, false);
        trigger.SetMap(map);
    }

    [TestMethod]
    public void FireAuthoredTriggerEvents_SkipsNegativeAndMissing()
    {
        TriggerManager.Instance.ClearAllForTests();
        var map = CreateMap();
        var template = MakeSpawnTemplate(500, isActive: true);
        template.TriggerEvents = new long[] { -1, 0, 99999 };
        map.MapData.Templates[500] = template;

        var spawn = new SpawnPoint(template);
        spawn.SetCoid(500, false);
        spawn.SetMap(map);

        var activator = new Character();
        activator.SetCoid(206, true);
        activator.SetMap(map);

        spawn.FireAuthoredTriggerEvents(activator); // must not throw
        TriggerManager.Instance.ClearAllForTests();
    }

    private static SpawnPointTemplate MakeSpawnTemplate(long coid, bool isActive)
    {
        var template = new SpawnPointTemplate
        {
            COID = (int)coid,
            IsActive = isActive,
            OriginalIsActive = isActive,
            TriggerEvents = new long[] { -1, -1, -1 },
            Location = new Vector4(0f, 0f, 0f, 0f),
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = -1,
            IsTemplate = false,
        });
        return template;
    }

    private static Reaction PlaceReaction(SectorMap map, long reactionCoid, ReactionType type, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            Name = type.ToString(),
            ReactionType = type,
            ActOnActivator = false,
        };
        tpl.Objects.Add(objectCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_spawn_active_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}
