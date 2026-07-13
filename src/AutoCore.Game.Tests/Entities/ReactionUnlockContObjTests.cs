using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// UnlockContObj (32) / RelockContObj (70): client CVOGReaction_Dispatch cases 0x20 / 0x46 call
/// CVOGReaction_UnlockContinentObject / RelockContinentObject with GenericVar1 = continent id.
/// Server must track the same hash so login create-packet and mid-session TransferMap stay in sync.
/// </summary>
[TestClass]
public class ReactionUnlockContObjTests
{
    private const int ContId = 558;
    private const int UnlockContinentId = 693; // Back Range
    private const int OtherContinentId = 550;  // Fireline

    private readonly List<string> _incomplete = new();
    private readonly List<(long Coid, int ContinentId, uint Bits)> _persisted = new();

    [TestInitialize]
    public void SetUp()
    {
        _incomplete.Clear();
        _persisted.Clear();
        IncompleteHandlerLog.TestSink = msg => _incomplete.Add(msg);
        TriggerManager.Instance.ClearAllForTests();

        ExplorationManager.Instance.ResetPersistenceForTests();
        ExplorationManager.Instance.AutoFlushOnEnqueue = false;
        ExplorationManager.Instance.PersistRow = (coid, continentId, bits) =>
            _persisted.Add((coid, continentId, bits));
    }

    [TestCleanup]
    public void TearDown()
    {
        IncompleteHandlerLog.TestSink = null;
        TriggerManager.Instance.ClearAllForTests();
        ExplorationManager.Instance.ResetPersistenceForTests();
        _incomplete.Clear();
        _persisted.Clear();
    }

    [TestMethod]
    public void UnlockContObj_TracksContinentOnCharacter()
    {
        var (character, vehicle, map) = CreatePlayer();
        var reaction = CreateUnlockReaction(map, UnlockContinentId);

        Assert.IsFalse(character.IsContinentUnlocked(UnlockContinentId));
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        Assert.IsTrue(character.IsContinentUnlocked(UnlockContinentId),
            "GenericVar1 is the continent id to unlock (client UnlockContinentObject).");
        Assert.AreEqual(0u, character.GetExploredBits(UnlockContinentId),
            "Unlock creates the continent entry; explored bits stay 0 until area reveal.");
        AssertNoUnlockIncomplete();
    }

    [TestMethod]
    public void UnlockContObj_IsIdempotent_PreservesExploredBits()
    {
        var (character, vehicle, map) = CreatePlayer();
        character.TryRevealArea(UnlockContinentId, areaId: 1, out _);
        Assert.AreEqual(1u, character.GetExploredBits(UnlockContinentId));

        var reaction = CreateUnlockReaction(map, UnlockContinentId);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.IsTrue(character.IsContinentUnlocked(UnlockContinentId));
        Assert.AreEqual(1u, character.GetExploredBits(UnlockContinentId),
            "Second unlock must not wipe already-explored bits.");
        AssertNoUnlockIncomplete();
    }

    [TestMethod]
    public void UnlockContObj_EnqueuesPersistAndDoesNotIncomplete()
    {
        var (character, vehicle, map) = CreatePlayer();
        var reaction = CreateUnlockReaction(map, UnlockContinentId);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        ExplorationManager.Instance.FlushPendingExplorations();

        Assert.IsTrue(
            _persisted.Any(p => p.Coid == character.ObjectId.Coid
                && p.ContinentId == UnlockContinentId
                && p.Bits == 0u),
            "Unlock must persist the continent entry for login restore.");
        AssertNoUnlockIncomplete();
    }

    [TestMethod]
    public void RelockContObj_ClearsUnlock_SymmetricWithUnlock()
    {
        var (character, vehicle, map) = CreatePlayer();
        var unlock = CreateUnlockReaction(map, UnlockContinentId, coid: 16218);
        var relock = CreateRelockReaction(map, UnlockContinentId, coid: 17871);

        Assert.IsTrue(unlock.TriggerIfPossible(vehicle));
        Assert.IsTrue(character.IsContinentUnlocked(UnlockContinentId));

        Assert.IsTrue(relock.TriggerIfPossible(vehicle));
        Assert.IsFalse(character.IsContinentUnlocked(UnlockContinentId),
            "RelockContObj must remove the continent from the unlock set.");
        AssertNoUnlockIncomplete();
    }

    [TestMethod]
    public void RelockContObj_WithoutPriorUnlock_IsSafeNoOp()
    {
        var (character, vehicle, map) = CreatePlayer();
        var relock = CreateRelockReaction(map, UnlockContinentId);

        Assert.IsTrue(relock.TriggerIfPossible(vehicle));
        Assert.IsFalse(character.IsContinentUnlocked(UnlockContinentId));
        AssertNoUnlockIncomplete();
    }

    [TestMethod]
    public void UnlockContObj_NullCharacter_StillSucceedsForClientNotify()
    {
        // No character on activator — still return true so 0x206C can fire for pure-client paths.
        var map = CreateMap();
        var vehicle = new Vehicle();
        vehicle.SetCoid(999, true);
        vehicle.SetMap(map);

        var reaction = CreateUnlockReaction(map, UnlockContinentId);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        AssertNoUnlockIncomplete();
    }

    [TestMethod]
    public void UnlockContObj_ZeroContinentId_NoStateMutation()
    {
        var (character, vehicle, map) = CreatePlayer();
        var reaction = CreateUnlockReaction(map, continentId: 0);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        Assert.IsFalse(character.IsContinentUnlocked(0));
        Assert.AreEqual(0, character.GetExplorationSnapshot().Count);
        AssertNoUnlockIncomplete();
    }

    [TestMethod]
    public void Character_IsContinentUnlocked_TrueAfterRevealOrUnlock()
    {
        var character = new Character();
        character.SetCoid(1, true);

        Assert.IsFalse(character.IsContinentUnlocked(OtherContinentId));
        Assert.IsTrue(character.TryUnlockContinent(OtherContinentId));
        Assert.IsTrue(character.IsContinentUnlocked(OtherContinentId));
        Assert.IsFalse(character.TryUnlockContinent(OtherContinentId), "already unlocked");

        Assert.IsTrue(character.TryRelockContinent(OtherContinentId));
        Assert.IsFalse(character.IsContinentUnlocked(OtherContinentId));
        Assert.IsFalse(character.TryRelockContinent(OtherContinentId), "already locked");
    }

    [TestMethod]
    public void UpsideBackRangeStyle_UnlockThenTransferDestinationIsUnlocked()
    {
        // Mirrors L0_unlock_* / L1_unlock_backrange then L0_trans_tobackrange (md=693).
        var (character, vehicle, map) = CreatePlayer();
        var unlock = CreateUnlockReaction(map, UnlockContinentId, coid: 16218, name: "L1_unlock_backrange");

        Assert.IsTrue(unlock.TriggerIfPossible(vehicle));
        Assert.IsTrue(character.IsContinentUnlocked(UnlockContinentId),
            "Client TransferMap (FUN_004d2ac0) requires continent 693 in the unlock hash.");
        AssertNoUnlockIncomplete();
    }

    private void AssertNoUnlockIncomplete()
    {
        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("Reaction.UnlockContObj") || m.Contains("Reaction.RelockContObj")),
            "Expected no Incomplete log for Unlock/Relock ContObj, got: " + string.Join(" | ", _incomplete));
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
            GenericVar3 = 0,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(coid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static Reaction CreateRelockReaction(
        SectorMap map,
        int continentId,
        long coid = 17871,
        string name = "l0_relock_test")
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)coid,
            Name = name,
            ReactionType = ReactionType.RelockContObj,
            ActOnActivator = true,
            GenericVar1 = continentId,
            GenericVar3 = 0,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(coid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var map = CreateMap();
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

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_unlock_cont_{ContId}",
            DisplayName = "UpsideTest",
            IsTown = true,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}
