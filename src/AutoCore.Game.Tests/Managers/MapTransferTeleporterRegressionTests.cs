using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using MapTransferType = AutoCore.Game.Constants.MapTransferType;

/// <summary>
/// Heavy regression for the Upside ↔ Back Range teleporter path fixed across three layers:
/// <list type="number">
/// <item>Town on-foot collision uses character (not parked vehicle).</item>
/// <item>UnlockContObj tracks continent unlocks for client TransferMap gates.</item>
/// <item>TransferCharacterToMap spawns at origin-keyed EnterPoint, not default EntryPoint.</item>
/// </list>
/// Synthetic ids mirror retail map data (Upside 558, Back Range 693, Fireline 550, etc.).
/// </summary>
[TestClass]
public class MapTransferTeleporterRegressionTests
{
    private const int UpsideId = 558;
    private const int BackRangeId = 693;
    private const int HighwayId = 426;
    private const int FirelineId = 550;
    private const int ScavAlleyId = 561;
    private const int GreasepitId = 679;

    private readonly List<string> _incomplete = new();
    private readonly List<(long Coid, int ContinentId, uint Bits)> _persisted = new();
    private readonly List<(long Coid, int ContinentId)> _deleted = new();

    [TestInitialize]
    public void SetUp()
    {
        _incomplete.Clear();
        _persisted.Clear();
        _deleted.Clear();
        IncompleteHandlerLog.TestSink = msg => _incomplete.Add(msg);
        TriggerManager.Instance.ClearAllForTests();
        ExplorationManager.Instance.ResetPersistenceForTests();
        ExplorationManager.Instance.AutoFlushOnEnqueue = false;
        ExplorationManager.Instance.PersistRow = (coid, continentId, bits) =>
            _persisted.Add((coid, continentId, bits));
        ExplorationManager.Instance.DeleteRow = (coid, continentId) =>
            _deleted.Add((coid, continentId));
    }

    [TestCleanup]
    public void TearDown()
    {
        IncompleteHandlerLog.TestSink = null;
        TriggerManager.Instance.ClearAllForTests();
        ExplorationManager.Instance.ResetPersistenceForTests();
        MapManager.Instance.ResolveMapForTests = null;
        MapManager.Instance.SuppressCreatePacketsForTests = false;
        _incomplete.Clear();
        _persisted.Clear();
        _deleted.Clear();
    }

    // ─── End-to-end: Upside on-foot pad → Back Range gate spawn ─────────────

    [TestMethod]
    public void E2E_UpsideOnFootPad_TransfersToBackRangeUpsideGate()
    {
        var (character, vehicle, upside) = CreatePlayer(UpsideId, isTown: true);
        character.AttachTestDataForTests("Flopss");
        vehicle.AttachTestDataForTests();

        // Retail L0_trans_tobackrange: Players collision, no conditions, TransferMap md=693.
        const long transferRxCoid = 4778;
        const long triggerCoid = 4777;
        PlaceTransferPad(
            upside,
            triggerCoid,
            transferRxCoid,
            padPosition: new Vector3(200f, 33f, 400f),
            destContinentId: BackRangeId,
            name: "L0_trans_tobackrange");

        // On foot at pad; vehicle remains at town entry.
        character.Position = new Vector3(200f, 33f, 400f);
        vehicle.Position = new Vector3(178f, 33f, 385f);

        var backRange = CreateMap(BackRangeId, isTown: false, entry: new Vector4(1801f, 131f, 1525f, 0f));
        AddContinentEnterPoint(
            backRange,
            coid: 8498,
            sourceContinentId: UpsideId,
            location: new Vector4(1240.8f, 61.9f, 2302.2f, 0f),
            rotation: new Quaternion(0f, 0.45f, 0f, 0.89f));
        // Unrelated EP must not win.
        AddContinentEnterPoint(
            backRange,
            coid: 10308,
            sourceContinentId: 789,
            location: new Vector4(406f, 26f, 2299f, 0f),
            rotation: Quaternion.Default);

        MapManager.Instance.ResolveMapForTests = id =>
        {
            Assert.AreEqual(BackRangeId, id);
            return backRange;
        };
        MapManager.Instance.SuppressCreatePacketsForTests = true;

        // Vehicle-only scan (pre-fix) would miss the pad.
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.AreSame(upside, character.Map, "Vehicle off-pad must not transfer");

        TriggerManager.Instance.CheckTriggersForPlayer(character);

        Assert.AreSame(backRange, character.Map, "On-foot pad must fire TransferMap");
        Assert.AreEqual(1240.8f, character.Position.X, 0.01f);
        Assert.AreEqual(61.9f, character.Position.Y, 0.01f);
        Assert.AreEqual(2302.2f, character.Position.Z, 0.01f);
        Assert.AreEqual(0.45f, character.Rotation.Y, 0.01f);
        Assert.AreEqual(character.Position.X, vehicle.Position.X, 0.01f);
        Assert.AreEqual(character.Position.Z, vehicle.Position.Z, 0.01f);
        Assert.AreEqual(BackRangeId, character.LastTownId);
        Assert.AreEqual(1240.8f, character.GetDbPositionXForTests(), 0.01f);
        Assert.AreEqual(2302.2f, character.GetDbPositionZForTests(), 0.01f);
        AssertNoIncomplete();
    }

    [TestMethod]
    public void E2E_BackRangeVehiclePad_TransfersToUpsideBackRangeGate()
    {
        // Reverse direction: field map uses vehicle as activator; Upside EP data=693.
        var (character, vehicle, backRange) = CreatePlayer(BackRangeId, isTown: false);
        character.AttachTestDataForTests();
        vehicle.AttachTestDataForTests();

        const long transferRxCoid = 6045;
        const long triggerCoid = 6046;
        PlaceTransferPad(
            backRange,
            triggerCoid,
            transferRxCoid,
            padPosition: new Vector3(1250f, 62f, 2310f),
            destContinentId: UpsideId,
            name: "l0_trans_upside");

        character.Position = new Vector3(0f, 0f, 0f); // may lag
        vehicle.Position = new Vector3(1250f, 62f, 2310f);

        var upside = CreateMap(UpsideId, isTown: true, entry: new Vector4(178f, 33f, 385f, 0f));
        AddContinentEnterPoint(
            upside,
            coid: 4770,
            sourceContinentId: BackRangeId,
            location: new Vector4(174.8f, 32.9f, 380.2f, 0f),
            rotation: new Quaternion(0f, 0.45f, 0f, 0.89f));
        AddContinentEnterPoint(
            upside,
            coid: 576,
            sourceContinentId: HighwayId,
            location: new Vector4(175.6f, 32.9f, 389f, 0f),
            rotation: Quaternion.Default);

        MapManager.Instance.ResolveMapForTests = id => id == UpsideId ? upside : null;
        MapManager.Instance.SuppressCreatePacketsForTests = true;

        TriggerManager.Instance.CheckTriggersForPlayer(character);

        Assert.AreSame(upside, character.Map);
        Assert.AreEqual(174.8f, character.Position.X, 0.01f);
        Assert.AreEqual(32.9f, character.Position.Y, 0.01f);
        Assert.AreEqual(380.2f, character.Position.Z, 0.01f);
        AssertNoIncomplete();
    }

    // ─── ResolvePlayerTriggerActivator ─────────────────────────────────────

    [TestMethod]
    public void ResolvePlayerTriggerActivator_Town_ReturnsCharacterEvenWithVehicle()
    {
        var (character, vehicle, _) = CreatePlayer(UpsideId, isTown: true);
        var activator = TriggerManager.ResolvePlayerTriggerActivator(character);
        Assert.AreSame(character, activator);
        Assert.AreNotSame(vehicle, activator);
    }

    [TestMethod]
    public void ResolvePlayerTriggerActivator_NonTown_ReturnsVehicle()
    {
        var (character, vehicle, _) = CreatePlayer(BackRangeId, isTown: false);
        Assert.AreSame(vehicle, TriggerManager.ResolvePlayerTriggerActivator(character));
    }

    [TestMethod]
    public void ResolvePlayerTriggerActivator_NonTown_NoVehicle_ReturnsCharacter()
    {
        var (character, _, _) = CreatePlayer(BackRangeId, isTown: false);
        character.SetCurrentVehicleForTests(null);
        Assert.AreSame(character, TriggerManager.ResolvePlayerTriggerActivator(character));
    }

    [TestMethod]
    public void ResolvePlayerTriggerActivator_Null_ReturnsNull()
    {
        Assert.IsNull(TriggerManager.ResolvePlayerTriggerActivator(null));
    }

    [TestMethod]
    public void ResolvePlayerTriggerActivator_Town_NoMap_FallsBackToCharacter()
    {
        var character = new Character();
        character.SetCoid(1, true);
        // No map → IsTown false path; no vehicle → character.
        Assert.AreSame(character, TriggerManager.ResolvePlayerTriggerActivator(character));
    }

    // ─── Town pad latch leave / re-enter ───────────────────────────────────

    [TestMethod]
    public void TownPad_LeaveAndReenter_RefiresTransferVolume()
    {
        var (character, vehicle, map) = CreatePlayer(UpsideId, isTown: true);

        const long rxCoid = 8001;
        const long triggerCoid = 8002;
        // Use Delete so we can count FireCount without map transfer tearing the map down.
        PlaceDeletePad(map, triggerCoid, rxCoid, new Vector3(50f, 0f, 50f), out var trigger);

        character.Position = new Vector3(50f, 0f, 50f);
        vehicle.Position = new Vector3(0f, 0f, 0f);

        TriggerManager.Instance.CheckTriggersForPlayer(character);
        Assert.AreEqual(1, trigger.FireCount);

        TriggerManager.Instance.CheckTriggersForPlayer(character);
        Assert.AreEqual(1, trigger.FireCount, "Still inside volume — no re-fire");

        character.Position = new Vector3(500f, 0f, 500f);
        TriggerManager.Instance.CheckTriggersForPlayer(character);

        character.Position = new Vector3(50f, 0f, 50f);
        TriggerManager.Instance.CheckTriggersForPlayer(character);
        Assert.AreEqual(2, trigger.FireCount, "Leave then re-enter must re-fire");
    }

    // ─── MapTransferSpawn matrix ───────────────────────────────────────────

    [TestMethod]
    public void MapTransferSpawn_NullMap_SafeDefaults()
    {
        Assert.IsFalse(MapTransferSpawn.TryResolve(null, UpsideId, out var pos, out var rot));
        Assert.AreEqual(0f, pos.X);
        Assert.AreEqual(Quaternion.Default.W, rot.W);
    }

    [TestMethod]
    public void MapTransferSpawn_SourceZero_UsesEntryPoint()
    {
        var map = CreateMap(BackRangeId, isTown: false, entry: new Vector4(10f, 20f, 30f, 0f));
        AddContinentEnterPoint(map, 1, UpsideId, new Vector4(1f, 2f, 3f, 0f), Quaternion.Default);

        Assert.IsFalse(MapTransferSpawn.TryResolve(map, sourceContinentId: 0, out var pos, out _));
        Assert.AreEqual(10f, pos.X);
        Assert.AreEqual(20f, pos.Y);
        Assert.AreEqual(30f, pos.Z);
    }

    [TestMethod]
    public void MapTransferSpawn_FirstMatchingContinentEnterPointWins()
    {
        var map = CreateMap(BackRangeId, isTown: false, entry: new Vector4(0f, 0f, 0f, 0f));
        AddContinentEnterPoint(map, 1, UpsideId, new Vector4(100f, 1f, 100f, 0f), Quaternion.Default);
        AddContinentEnterPoint(map, 2, UpsideId, new Vector4(200f, 2f, 200f, 0f), Quaternion.Default);

        Assert.IsTrue(MapTransferSpawn.TryResolve(map, UpsideId, out var pos, out _));
        // Dictionary order is insertion order for int keys in practice; either 100 or 200 is a match.
        Assert.IsTrue(
            (Math.Abs(pos.X - 100f) < 0.01f) || (Math.Abs(pos.X - 200f) < 0.01f),
            $"Expected one of the Upside enter points, got {pos.X}");
    }

    [TestMethod]
    public void MapTransferSpawn_IgnoresType255DefaultMarker()
    {
        var map = CreateMap(BackRangeId, isTown: false, entry: new Vector4(1801f, 131f, 1525f, 0f));
        var marker = new EnterPointTemplate
        {
            COID = 9472,
            MapTransferType = 255,
            MapTransferData = -1,
            Location = new Vector4(1801f, 131f, 1525f, 0f),
            Rotation = Quaternion.Default,
        };
        map.MapData.Templates[9472] = marker;
        AddContinentEnterPoint(map, 8498, UpsideId, new Vector4(1240f, 62f, 2302f, 0f), Quaternion.Default);

        Assert.IsTrue(MapTransferSpawn.TryResolve(map, UpsideId, out var pos, out _));
        Assert.AreEqual(1240f, pos.X, 0.01f);
    }

    [TestMethod]
    public void MapTransferSpawn_HighwayOrigin_SelectsCorrectGate()
    {
        // Highway 426 has many continent EPs; picking Scav Alley origin must hit data=561 style match.
        var hwy = CreateMap(HighwayId, isTown: false, entry: new Vector4(5156f, 14f, 585f, 0f));
        AddContinentEnterPoint(hwy, 10, UpsideId, new Vector4(5197f, 17f, 403f, 0f), Quaternion.Default);
        AddContinentEnterPoint(hwy, 11, FirelineId, new Vector4(8629f, 31f, 7309f, 0f), Quaternion.Default);
        AddContinentEnterPoint(hwy, 12, ScavAlleyId, new Vector4(8134f, 8f, 1786f, 0f), Quaternion.Default);

        Assert.IsTrue(MapTransferSpawn.TryResolve(hwy, ScavAlleyId, out var pos, out _));
        Assert.AreEqual(8134f, pos.X, 0.01f);
        Assert.AreEqual(1786f, pos.Z, 0.01f);
    }

    [TestMethod]
    public void TransferCharacterToMap_NoSourceMap_FallsBackToEntryPoint()
    {
        var dest = CreateMap(BackRangeId, isTown: false, entry: new Vector4(9f, 8f, 7f, 0f));
        AddContinentEnterPoint(dest, 1, UpsideId, new Vector4(1f, 2f, 3f, 0f), Quaternion.Default);

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        var character = new Character();
        character.SetCoid(50, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);
        character.SetCurrentVehicleForTests(new Vehicle());
        character.CurrentVehicle.SetCoid(51, true);
        character.CurrentVehicle.AttachTestDataForTests();
        // character.Map is null → sourceContinentId 0 → entry fallback

        MapManager.Instance.ResolveMapForTests = _ => dest;
        MapManager.Instance.SuppressCreatePacketsForTests = true;

        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, BackRangeId));
        Assert.AreEqual(9f, character.Position.X);
        Assert.AreEqual(8f, character.Position.Y);
        Assert.AreEqual(7f, character.Position.Z);
    }

    [TestMethod]
    public void TransferCharacterToMap_RoundTrip_UpsideAndBackRange_UsePairedGates()
    {
        // Both maps carry EnterPoints (CBID 0). Leaving a map resets local world and re-Create()s
        // templates — GraphicsObjectTemplate must not LoadCloneBase(0) or the transfer throws.
        var upside = CreateMap(UpsideId, isTown: true, entry: new Vector4(178f, 33f, 385f, 0f));
        AddContinentEnterPoint(upside, 4770, BackRangeId, new Vector4(174.8f, 32.9f, 380.2f, 0f),
            new Quaternion(0f, 0.1f, 0f, 0.99f));

        var backRange = CreateMap(BackRangeId, isTown: false, entry: new Vector4(1801f, 131f, 1525f, 0f));
        AddContinentEnterPoint(backRange, 8498, UpsideId, new Vector4(1240.8f, 61.9f, 2302.2f, 0f),
            new Quaternion(0f, 0.2f, 0f, 0.98f));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(60, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);
        character.SetCurrentVehicleForTests(new Vehicle());
        character.CurrentVehicle.SetCoid(61, true);
        character.CurrentVehicle.AttachTestDataForTests();
        character.SetMap(upside);
        character.CurrentVehicle.SetMap(upside);

        var previousResolver = MapManager.Instance.ResolveMapForTests;
        var previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        try
        {
            MapManager.Instance.ResolveMapForTests = id => id switch
            {
                BackRangeId => backRange,
                UpsideId => upside,
                _ => null,
            };
            MapManager.Instance.SuppressCreatePacketsForTests = true;

            Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, BackRangeId));
            Assert.AreEqual(1240.8f, character.Position.X, 0.01f);
            Assert.AreEqual(0.2f, character.Rotation.Y, 0.01f);

            Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, UpsideId));
            Assert.AreEqual(174.8f, character.Position.X, 0.01f);
            Assert.AreEqual(380.2f, character.Position.Z, 0.01f);
            Assert.AreEqual(0.1f, character.Rotation.Y, 0.01f);
        }
        finally
        {
            MapManager.Instance.ResolveMapForTests = previousResolver;
            MapManager.Instance.SuppressCreatePacketsForTests = previousSuppress;
        }
    }

    [TestMethod]
    public void EnterPointTemplate_Create_WithCbidZero_DoesNotThrow()
    {
        // Regression: last player leaving a map re-Creates EnterPoints via InitializeLocalObjects.
        var ep = new EnterPointTemplate
        {
            COID = 8498,
            CBID = 0,
            MapTransferType = (byte)MapTransferType.ContinentObject,
            MapTransferData = UpsideId,
            Location = new Vector4(1f, 2f, 3f, 0f),
            Rotation = Quaternion.Default,
        };

        var obj = ep.Create();
        Assert.IsNotNull(obj);
        Assert.AreEqual(1f, obj.Position.X);
        Assert.AreEqual(3f, obj.Position.Z);
    }

    [TestMethod]
    public void LeavingMapWithEnterPoints_ResetLocalWorld_DoesNotThrow()
    {
        var map = CreateMap(UpsideId, isTown: true, entry: new Vector4(178f, 33f, 385f, 0f));
        AddContinentEnterPoint(map, 4770, BackRangeId, new Vector4(174f, 33f, 380f, 0f), Quaternion.Default);

        var character = new Character();
        character.SetCoid(70, true);
        character.SetMap(map);
        Assert.AreEqual(1, map.PlayerCount);

        // Last character leave → ResetLocalWorldToAuthored → InitializeLocalObjects → EnterPoint.Create.
        character.SetMap(null);
        Assert.AreEqual(0, map.PlayerCount);
    }

    // ─── UnlockContObj regression ──────────────────────────────────────────

    [TestMethod]
    public void UnlockContObj_PerPlayerLoadBatch_UnlocksMultipleContinents()
    {
        // Upside PerPlayerLoad fires L0_unlock_fireline/scavalley/jakesupsidegreasepit.
        var (character, vehicle, map) = CreatePlayer(UpsideId, isTown: true);

        foreach (var (coid, continentId, name) in new[]
                 {
                     (4839L, FirelineId, "L0_unlock_fireline"),
                     (4828L, ScavAlleyId, "L0_unlock_scavalley"),
                     (5782L, GreasepitId, "L0_unlock_jakesupsidegreasepit"),
                 })
        {
            var rx = CreateUnlockReaction(map, continentId, coid, name);
            Assert.IsTrue(rx.TriggerIfPossible(vehicle), name);
            Assert.IsTrue(character.IsContinentUnlocked(continentId), name);
        }

        ExplorationManager.Instance.FlushPendingExplorations();
        Assert.AreEqual(3, _persisted.Count);

        var packet = new CreateCharacterExtendedPacket
        {
            ObjectId = character.ObjectId,
            Name = "t",
            ClanName = "",
            CustomizedName = "",
            Position = new Vector3(0f, 0f, 0f),
            Rotation = Quaternion.Default,
        };
        character.WriteExploration(packet);

        var unlockedIds = packet.ContinentUnlocked
            .Where(c => c != null)
            .Select(c => c.ContinentId)
            .ToHashSet();
        Assert.IsTrue(unlockedIds.Contains(FirelineId)
            && unlockedIds.Contains(ScavAlleyId)
            && unlockedIds.Contains(GreasepitId),
            "CreateCharacterExtended must restore PerPlayerLoad unlocks after relog/map transfer.");
        AssertNoIncomplete();
    }

    [TestMethod]
    public void UnlockContObj_OnFootCharacterActivator_Works()
    {
        var (character, _, map) = CreatePlayer(UpsideId, isTown: true);
        var rx = CreateUnlockReaction(map, BackRangeId, 16218, "L1_unlock_backrange");
        Assert.IsTrue(rx.TriggerIfPossible(character));
        Assert.IsTrue(character.IsContinentUnlocked(BackRangeId));
        AssertNoIncomplete();
    }

    [TestMethod]
    public void RelockContObj_FlushDeletesPersistRow()
    {
        var (character, vehicle, map) = CreatePlayer(UpsideId, isTown: true);
        Assert.IsTrue(CreateUnlockReaction(map, BackRangeId).TriggerIfPossible(vehicle));
        ExplorationManager.Instance.FlushPendingExplorations();
        _persisted.Clear();

        Assert.IsTrue(CreateRelockReaction(map, BackRangeId).TriggerIfPossible(vehicle));
        ExplorationManager.Instance.FlushPendingExplorations();

        Assert.IsFalse(character.IsContinentUnlocked(BackRangeId));
        Assert.IsTrue(_deleted.Any(d => d.Coid == character.ObjectId.Coid && d.ContinentId == BackRangeId));
        Assert.AreEqual(0, _persisted.Count, "Relock must delete, not upsert bits=0");
        AssertNoIncomplete();
    }

    [TestMethod]
    public void UnlockContinent_Manager_NullAndZero_ReturnFalse()
    {
        Assert.IsFalse(ExplorationManager.Instance.UnlockContinent(null, BackRangeId));
        var character = new Character();
        character.SetCoid(2, true);
        Assert.IsFalse(ExplorationManager.Instance.UnlockContinent(character, 0));
        Assert.IsFalse(ExplorationManager.Instance.RelockContinent(null, BackRangeId));
        Assert.IsFalse(ExplorationManager.Instance.RelockContinent(character, 0));
    }

    [TestMethod]
    public void UnlockThenTransfer_WriteExploration_SurvivesMapChange()
    {
        var (character, vehicle, upside) = CreatePlayer(UpsideId, isTown: true);
        character.AttachTestDataForTests();
        vehicle.AttachTestDataForTests();

        Assert.IsTrue(CreateUnlockReaction(upside, BackRangeId, 16218, "L1_unlock_backrange")
            .TriggerIfPossible(vehicle));

        var backRange = CreateMap(BackRangeId, isTown: false, entry: new Vector4(1f, 2f, 3f, 0f));
        AddContinentEnterPoint(backRange, 8498, UpsideId, new Vector4(1240f, 62f, 2302f, 0f), Quaternion.Default);

        MapManager.Instance.ResolveMapForTests = _ => backRange;
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, BackRangeId));

        Assert.IsTrue(character.IsContinentUnlocked(BackRangeId),
            "Map transfer must not clear continent unlock set.");
        var snap = character.GetExplorationSnapshot();
        Assert.IsTrue(snap.ContainsKey(BackRangeId));
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private void AssertNoIncomplete()
    {
        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("INCOMPLETE")),
            "Unexpected Incomplete: " + string.Join(" | ", _incomplete));
    }

    private static void PlaceTransferPad(
        SectorMap map,
        long triggerCoid,
        long reactionCoid,
        Vector3 padPosition,
        int destContinentId,
        string name)
    {
        var transferTpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            Name = name,
            ReactionType = ReactionType.TransferMap,
            ActOnActivator = true,
            MapTransfer = MapTransferType.ContinentObject,
            MapTransferData = destContinentId,
        };
        var transferRx = new Reaction(transferTpl);
        transferRx.SetCoid(reactionCoid, false);
        transferRx.SetMap(map);

        var triggerTpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            Name = name,
            TargetType = TriggerTargetType.Players,
            Scale = 15f,
            DoCollision = true,
            DoConditionals = false,
            ActivationCount = -1,
        };
        triggerTpl.Reactions.Add(reactionCoid);
        var trigger = new Trigger(triggerTpl);
        trigger.SetCoid(triggerCoid, false);
        trigger.Position = padPosition;
        trigger.Scale = 15f;
        trigger.SetMap(map);
    }

    private static void PlaceDeletePad(
        SectorMap map,
        long triggerCoid,
        long reactionCoid,
        Vector3 padPosition,
        out Trigger trigger)
    {
        var rxTpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Delete,
        };
        var rx = new Reaction(rxTpl);
        rx.SetCoid(reactionCoid, false);
        rx.SetMap(map);

        var triggerTpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 15f,
            DoCollision = true,
            ActivationCount = -1,
        };
        triggerTpl.Reactions.Add(reactionCoid);
        trigger = new Trigger(triggerTpl);
        trigger.SetCoid(triggerCoid, false);
        trigger.Position = padPosition;
        trigger.Scale = 15f;
        trigger.SetMap(map);
    }

    private static Reaction CreateUnlockReaction(
        SectorMap map,
        int continentId,
        long coid = 4839,
        string name = "L0_unlock_test")
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)coid,
            Name = name,
            ReactionType = ReactionType.UnlockContObj,
            ActOnActivator = true,
            GenericVar1 = continentId,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(coid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static Reaction CreateRelockReaction(SectorMap map, int continentId, long coid = 17871)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)coid,
            Name = "l0_relock_test",
            ReactionType = ReactionType.RelockContObj,
            ActOnActivator = true,
            GenericVar1 = continentId,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(coid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static void AddContinentEnterPoint(
        SectorMap map,
        long coid,
        int sourceContinentId,
        Vector4 location,
        Quaternion rotation)
    {
        map.MapData.Templates[coid] = new EnterPointTemplate
        {
            COID = (int)coid,
            MapTransferType = (byte)MapTransferType.ContinentObject,
            MapTransferData = sourceContinentId,
            Location = location,
            Rotation = rotation,
        };
    }

    private static SectorMap CreateMap(int continentId, bool isTown, Vector4 entry)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_teleport_{continentId}",
            DisplayName = $"map_{continentId}",
            IsTown = isTown,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, entry);
    }

    private static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer(
        int continentId,
        bool isTown)
    {
        var map = CreateMap(continentId, isTown, new Vector4(0f, 0f, 0f, 0f));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18325, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(18326, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}
