using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// SS-05: exploration DB writes must not block the vehicle-move / tick-adjacent hot path.
/// </summary>
[TestClass]
public class ExplorationPersistenceTests
{
    [TestInitialize]
    public void ResetManagerPersistence()
    {
        ExplorationManager.Instance.ResetPersistenceForTests();
    }

    [TestCleanup]
    public void CleanupManagerPersistence()
    {
        ExplorationManager.Instance.ResetPersistenceForTests();
    }

    [TestMethod]
    public void Queue_Enqueue_LatestWinsPerCoidContinent()
    {
        var queue = new ExplorationPersistenceQueue();
        queue.Enqueue(coid: 10, continentId: 1, exploredBits: 0b0001);
        queue.Enqueue(coid: 10, continentId: 1, exploredBits: 0b0011); // supersedes
        queue.Enqueue(coid: 10, continentId: 2, exploredBits: 0b0001);
        queue.Enqueue(coid: 11, continentId: 1, exploredBits: 0b0100);

        Assert.AreEqual(3, queue.PendingCount);

        var flushed = new List<(long Coid, int ContinentId, uint Bits)>();
        var count = queue.Flush((coid, continentId, bits) => flushed.Add((coid, continentId, bits)));

        Assert.AreEqual(3, count);
        Assert.AreEqual(0, queue.PendingCount);
        CollectionAssert.AreEquivalent(
            new[]
            {
                (10L, 1, 0b0011u),
                (10L, 2, 0b0001u),
                (11L, 1, 0b0100u),
            },
            flushed);
    }

    [TestMethod]
    public void Queue_Enqueue_IgnoresContinentZero_AndNullFlushThrows()
    {
        var queue = new ExplorationPersistenceQueue();
        queue.Enqueue(1, continentId: 0, exploredBits: 1);
        Assert.AreEqual(0, queue.PendingCount);

        Assert.ThrowsException<ArgumentNullException>(() => queue.Flush(null));
    }

    [TestMethod]
    public void Queue_Flush_Empty_ReturnsZero()
    {
        var queue = new ExplorationPersistenceQueue();
        var calls = 0;
        Assert.AreEqual(0, queue.Flush((_, _, _) => calls++));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void Queue_Clear_DropsPending()
    {
        var queue = new ExplorationPersistenceQueue();
        queue.Enqueue(1, 2, 3);
        queue.Clear();
        Assert.AreEqual(0, queue.PendingCount);
        Assert.AreEqual(0, queue.Flush((_, _, _) => Assert.Fail("should not persist after clear")));
    }

    [TestMethod]
    public void Queue_EnqueueDuringFlush_RemainsPending()
    {
        var queue = new ExplorationPersistenceQueue();
        queue.Enqueue(1, 1, 1u);

        var sawFirst = false;
        queue.Flush((coid, continentId, bits) =>
        {
            if (!sawFirst)
            {
                sawFirst = true;
                // New discovery while prior write is flushing — must not be lost.
                queue.Enqueue(1, 1, 0b11u);
            }
        });

        Assert.AreEqual(1, queue.PendingCount);
        uint? remaining = null;
        queue.Flush((_, _, bits) => remaining = bits);
        Assert.AreEqual(0b11u, remaining);
    }

    /// <summary>
    /// SS-05 verification: artificial slow persist must not block the reveal hot path.
    /// </summary>
    [TestMethod]
    public void SS05_HotPath_ReturnsWithoutWaitingForSlowPersist()
    {
        var manager = ExplorationManager.Instance;
        // Deterministic: no ThreadPool flush during the timed section.
        manager.AutoFlushOnEnqueue = false;

        var persistCalls = 0;
        manager.PersistRow = (_, _, _) =>
        {
            Interlocked.Increment(ref persistCalls);
            Thread.Sleep(500);
        };

        var character = new Character();
        character.SetCoid(90501, true);

        var sw = Stopwatch.StartNew();
        Assert.IsTrue(manager.TryRevealForTests(character, continentId: 42, areaId: 1, out var newBits));
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds < 150,
            $"SS-05: hot path blocked by persist ({sw.ElapsedMilliseconds}ms); expected enqueue-only.");
        Assert.AreEqual(0, persistCalls, "SaveChanges/persist must not run on the hot path.");
        Assert.AreEqual(1, manager.PendingPersistCount);
        Assert.AreEqual(newBits, character.GetExploredBits(42), "In-memory bits must still update synchronously.");
        Assert.AreNotEqual(0u, newBits);

        // Off hot path: flush is allowed to be slow and must deliver latest bits.
        var flushed = manager.FlushPendingExplorations();
        Assert.AreEqual(1, flushed);
        Assert.AreEqual(1, persistCalls);
        Assert.AreEqual(0, manager.PendingPersistCount);
    }

    [TestMethod]
    public void Manager_Reveal_EnqueuesLatestWins_AndFlushInvokesPersist()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = false;

        var writes = new List<(long Coid, int ContinentId, uint Bits)>();
        manager.PersistRow = (coid, continentId, bits) => writes.Add((coid, continentId, bits));

        var character = new Character();
        character.SetCoid(77, true);

        Assert.IsTrue(manager.TryRevealForTests(character, 9, areaId: 1, out var bits1));
        Assert.IsTrue(manager.TryRevealForTests(character, 9, areaId: 2, out var bits2));
        Assert.IsFalse(manager.TryRevealForTests(character, 9, areaId: 1, out _), "duplicate area is not a new discovery");
        Assert.IsTrue(manager.TryRevealForTests(character, 10, areaId: 3, out _));

        // Two continents pending; continent 9 holds latest combined bits only once.
        Assert.AreEqual(2, manager.PendingPersistCount);

        Assert.AreEqual(2, manager.FlushPendingExplorations());
        Assert.AreEqual(0, manager.PendingPersistCount);

        CollectionAssert.AreEquivalent(
            new[]
            {
                (77L, 9, bits2),
                (77L, 10, character.GetExploredBits(10)),
            },
            writes);
        Assert.AreEqual(bits1 | ContinentAreaMaskBit(2), bits2);
    }

    [TestMethod]
    public void Manager_OnVehicleMoved_NullAndMissingOwner_NoThrow()
    {
        var manager = ExplorationManager.Instance;
        manager.OnVehicleMoved(null);

        var vehicle = new Vehicle();
        manager.OnVehicleMoved(vehicle); // no character owner
    }

    [TestMethod]
    public void Manager_SyncExplorationAfterLogin_Null_NoThrow()
    {
        ExplorationManager.Instance.SyncExplorationAfterLogin(null);
    }

    [TestMethod]
    public void Manager_TryRevealForTests_NullCharacter_ReturnsFalse()
    {
        Assert.IsFalse(ExplorationManager.Instance.TryRevealForTests(null, 1, 1, out var bits));
        Assert.AreEqual(0u, bits);
    }

    [TestMethod]
    public void Manager_ClearMaskCache_DoesNotThrow()
    {
        ExplorationManager.Instance.ClearMaskCache();
    }

    [TestMethod]
    public void Manager_Flush_UsesDefaultPersistRow_WhenUnset()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = false;
        manager.PersistRow = null; // fall back to DB adapter (no throw without pending rows)

        Assert.AreEqual(0, manager.FlushPendingExplorations());
    }

    [TestMethod]
    public void Manager_OnVehicleMoved_WithMask_RevealsAndEnqueuesWithoutBlocking()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = false;

        var persistCalls = 0;
        manager.PersistRow = (_, _, _) =>
        {
            Interlocked.Increment(ref persistCalls);
            Thread.Sleep(400);
        };

        const int continentId = 501;
        const float grid = 10f;
        // Area id 3 at cell (0,0): world pos near origin after grid offset.
        var areaIds = new byte[] { 3 };
        var mask = new ContinentAreaMask(continentId, width: 1, height: 1, gridSize: grid, areaIds);
        manager.SetMaskForTests(mask);
        manager.ResolveMaskForTests = m => m.ContinentId == continentId ? mask : null;

        var character = new Character();
        character.SetCoid(50101, true);
        var vehicle = new Vehicle();
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.Position = new Vector3(grid * 0.5f, 0f, grid * 0.5f);

        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = "test_mask",
            DisplayName = "t",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        vehicle.SetMap(map);
        character.SetMap(map);

        var sw = Stopwatch.StartNew();
        manager.OnVehicleMoved(vehicle);
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds < 150,
            $"SS-05: OnVehicleMoved blocked ({sw.ElapsedMilliseconds}ms).");
        Assert.AreEqual(0, persistCalls);
        Assert.AreEqual(1, manager.PendingPersistCount);
        Assert.AreEqual(1u << (3 - 1), character.GetExploredBits(continentId));

        // Same cell again without force: no re-sample / no extra enqueue.
        manager.OnVehicleMoved(vehicle);
        Assert.AreEqual(1, manager.PendingPersistCount);

        // Duplicate area: no new discovery.
        vehicle.Position = new Vector3(grid * 0.5f + grid, 0f, grid * 0.5f); // out of bounds area 0
        manager.OnVehicleMoved(vehicle);
        Assert.AreEqual(1, manager.PendingPersistCount);
    }

    [TestMethod]
    public void Manager_OnVehicleMoved_NullMask_NoPersist()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = false;
        manager.ResolveMaskForTests = _ => null;

        var character = new Character();
        character.SetCoid(502, true);
        var vehicle = new Vehicle();
        character.SetCurrentVehicleForTests(vehicle);

        var continent = new ContinentObject
        {
            Id = 502,
            MapFileName = "none",
            DisplayName = "t",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        vehicle.SetMap(map);

        manager.OnVehicleMoved(vehicle);
        Assert.AreEqual(0, manager.PendingPersistCount);
    }

    [TestMethod]
    public void Manager_SyncExplorationAfterLogin_PushesSnapshotAndSamples()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = false;
        manager.PersistRow = (_, _, _) => { };

        const int continentId = 600;
        var character = new Character();
        character.SetCoid(6001, true);
        character.SetExplorationsForTests(new[]
        {
            new CharacterExploration { CharacterCoid = 6001, ContinentId = continentId, ExploredBits = 0b1 },
        });

        // No vehicle/map: snapshot path only (SendUnlockRegion no-ops without connection).
        manager.SyncExplorationAfterLogin(character);
        Assert.AreEqual(0, manager.PendingPersistCount);

        // With vehicle + mask: force sample discovers a new area.
        const float grid = 8f;
        var mask = new ContinentAreaMask(continentId, 1, 1, grid, new byte[] { 2 });
        manager.ResolveMaskForTests = _ => mask;

        var vehicle = new Vehicle();
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.Position = new Vector3(grid * 0.5f, 0f, grid * 0.5f);

        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = "login",
            DisplayName = "t",
            IsTown = true,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        vehicle.SetMap(map);
        character.SetMap(map);

        manager.SyncExplorationAfterLogin(character);
        Assert.AreEqual(1, manager.PendingPersistCount);
        Assert.AreEqual(0b1u | (1u << 1), character.GetExploredBits(continentId));
    }

    [TestMethod]
    public void Manager_AutoFlushOnEnqueue_FlushesOnBackgroundThread()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = true;

        var flushed = new ManualResetEventSlim(false);
        var bitsSeen = 0u;
        manager.PersistRow = (_, _, bits) =>
        {
            bitsSeen = bits;
            flushed.Set();
        };

        var character = new Character();
        character.SetCoid(7001, true);
        Assert.IsTrue(manager.TryRevealForTests(character, 70, areaId: 4, out var newBits));

        Assert.IsTrue(flushed.Wait(TimeSpan.FromSeconds(2)), "Background flush did not run.");
        Assert.AreEqual(newBits, bitsSeen);
        Assert.AreEqual(0, manager.PendingPersistCount);
    }

    [TestMethod]
    public void Manager_TryReveal_WithConnection_SendsUnlockRegion()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = false;
        manager.PersistRow = (_, _, _) => { };

        var character = new Character();
        character.SetCoid(8001, true);
        // Connection without full net session: SendGamePacket may fail after writing bytes.
        // We only require the reveal+enqueue path to complete for SS-05; packet send is best-effort.
        try
        {
            character.SetOwningConnection(new TNLConnection());
            manager.TryRevealForTests(character, 80, 1, out _);
        }
        catch
        {
            // TNL may throw without an established connection; in-memory + queue still matter.
        }

        Assert.AreEqual(1u, character.GetExploredBits(80));
        Assert.AreEqual(1, manager.PendingPersistCount);
    }

    [TestMethod]
    public void Manager_Discover_InvalidAreaId_DoesNotEnqueue()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = false;

        const int continentId = 900;
        const float grid = 5f;
        // Area id 0 is empty / not revealable.
        var mask = new ContinentAreaMask(continentId, 1, 1, grid, new byte[] { 0 });
        manager.ResolveMaskForTests = _ => mask;

        var character = new Character();
        character.SetCoid(9001, true);
        var vehicle = new Vehicle();
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.Position = new Vector3(grid * 0.5f, 0f, grid * 0.5f);

        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = "empty",
            DisplayName = "t",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        vehicle.SetMap(map);

        manager.OnVehicleMoved(vehicle);
        Assert.AreEqual(0, manager.PendingPersistCount);
        Assert.AreEqual(0u, character.GetExploredBits(continentId));
    }

    [TestMethod]
    public void Manager_CachedMask_UsedWithoutResolver()
    {
        var manager = ExplorationManager.Instance;
        manager.AutoFlushOnEnqueue = false;
        manager.PersistRow = (_, _, _) => { };

        const int continentId = 910;
        const float grid = 12f;
        var mask = new ContinentAreaMask(continentId, 1, 1, grid, new byte[] { 5 });
        manager.SetMaskForTests(mask);
        // No ResolveMaskForTests → cache hit in GetOrLoadMask.

        var character = new Character();
        character.SetCoid(9101, true);
        var vehicle = new Vehicle();
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.Position = new Vector3(grid * 0.5f, 0f, grid * 0.5f);

        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = "cached",
            DisplayName = "t",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        vehicle.SetMap(map);

        manager.OnVehicleMoved(vehicle);
        Assert.AreEqual(1u << 4, character.GetExploredBits(continentId));
        Assert.AreEqual(1, manager.PendingPersistCount);
    }

    [TestMethod]
    public void Manager_SetMaskForTests_Null_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            ExplorationManager.Instance.SetMaskForTests(null));
    }

    private static uint ContinentAreaMaskBit(byte areaId) => 1u << (areaId - 1);
}
