using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Types;

namespace AutoCore.Game.Tests;

using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

[TestClass]
public static class TestBootstrap
{
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        // Mirror TNLInterface static setup so ActivateGhosting / RPC events resolve class IDs.
        GhostConnection.RegisterNetClassReps();
        GhostObject.RegisterNetClassReps();
        GhostCreature.RegisterNetClassReps();
        GhostCharacter.RegisterNetClassReps();
        GhostVehicle.RegisterNetClassReps();
        TNLConnection.RegisterNetClassReps();
        NetClassRep.Initialize();

        // Unit tests must never reach the live MySQL-backed coid sequence (SS-31 allocator).
        // High base keeps test item coids clear of test character/vehicle/map coids.
        var nextTestItemCoid = 900_000L;
        AutoCore.Game.Inventory.InventoryRuntime.AllocatePersistentCoid =
            () => System.Threading.Interlocked.Increment(ref nextTestItemCoid);
    }
}
