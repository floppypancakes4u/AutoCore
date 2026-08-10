using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

/// <summary>
/// SS-31 end-to-end regression pin. This is a GREEN-ON-FIRST-RUN pin over already-fixed
/// behavior (plan-approved) — not a red-first TDD cycle. It pins the two independent layers
/// of the SS-31 fix together in one integration test:
///
///  1. The shared persistent-coid allocator (<see cref="InventoryRuntime.AllocateItemCoid"/>)
///     draws item coids from the same authority (the <c>simple_object</c> sequence) as
///     character and vehicle coids, instead of the old map-local world counter — making a
///     collision structurally impossible regardless of what the map counter happens to read.
///  2. The <see cref="InventoryPersistence.EnsureSimpleObject"/> guard, which independently
///     refuses to overwrite an existing non-placeholder simple_object row with a different
///     type/cbid — the second line of defense if a collision ever did occur.
///
/// Historical shape ("the Donuts incident"): item coids used to be minted from
/// <c>Map.LocalCoidCounter</c>, checked only against the character's own inventory. When that
/// counter reached a value already claimed by a character/vehicle coid drawn from the
/// simple_object sequence (coid 18274 = character, 18275 = vehicle), the (then-unconditional)
/// simple_object upsert overwrote the character's identity row with item data (a weapon,
/// CBID 12853), corrupting Type/CBID and access-violating the client at character select.
/// See docs/id-collisions.md and <see cref="SimpleObjectOverwriteGuardTests"/> for the
/// unit-level pin on the guard alone; this test pins the full pickup → allocate → persist path.
/// </summary>
[TestClass]
public class Ss31CollisionIntegrationTests
{
    // N / N+1 — the canonical SS-31 example coids, matching InventoryRuntime.AllocateItemCoid's
    // doc comment and SimpleObjectOverwriteGuardTests / CharacterSelectionManagerTests seeding.
    private const long CharacterCoid = 18274;
    private const long VehicleCoid = 18275;
    private const int CharacterCbid = 34;
    private const int VehicleCbid = 19658;

    private DbContextOptions<CharContext> _options = null!;

    [TestInitialize]
    public void Init()
    {
        _options = new DbContextOptionsBuilder<CharContext>()
            .UseInMemoryDatabase("ss31-e2e-" + Guid.NewGuid().ToString("N"))
            .Options;
        InventoryPersistence.CreateContext = () => new CharContext(_options);

        using var seed = new CharContext(_options);
        seed.SimpleObjects.Add(new SimpleObjectData
        {
            Coid = CharacterCoid,
            Type = (byte)CloneBaseObjectType.Character,
            CBID = CharacterCbid,
        });
        seed.SimpleObjects.Add(new SimpleObjectData
        {
            Coid = VehicleCoid,
            Type = (byte)CloneBaseObjectType.Vehicle,
            CBID = VehicleCbid,
        });
        seed.Characters.Add(new CharacterData
        {
            Coid = CharacterCoid,
            AccountId = 1,
            Name = "Ss31Pilot",
            Deleted = false,
            ActiveVehicleCoid = VehicleCoid,
        });
        seed.Vehicles.Add(new VehicleData
        {
            Coid = VehicleCoid,
            CharacterCoid = CharacterCoid,
            Name = "Ss31Vehicle",
        });
        seed.SaveChanges();
    }

    [TestCleanup]
    public void Cleanup()
    {
        InventoryPersistence.ResetForTests();
    }

    [TestMethod]
    public void SS31_Pickup_WithSharedAllocator_NeverTouchesCharacterRow()
    {
        var harness = new InventoryTestHarness(characterCoid: CharacterCoid, vehicleCoid: VehicleCoid);

        // Old-bug precondition: the map-local world counter sits exactly on the character's
        // coid. Pre-fix, the allocator returned this counter value directly, so this setup
        // used to be exactly the shape that produced the collision.
        InventoryTestMapHelper.AttachMap(harness.Character, localCoidCounter: CharacterCoid);

        const long allocatedCoid = 42_000L; // M ≠ N
        var savedAllocator = InventoryRuntime.AllocatePersistentCoid;
        try
        {
            InventoryRuntime.AllocatePersistentCoid = () => allocatedCoid;

            var runtime = new InventoryRuntime(harness.Character);
            Assert.IsTrue(runtime.CanAllocateItem, "harness must attach a map for the runtime to allocate coids");

            var claimedCoid = runtime.AllocateItemCoid();
            Assert.AreEqual(allocatedCoid, claimedCoid,
                "shared allocator must supply the persistent coid, not the map-local counter");
            Assert.AreNotEqual(CharacterCoid, claimedCoid,
                "pre-fix code returned the map-local counter value here, colliding with the character coid");

            var result = harness.Inventory.PickupWorldItem(
                cbid: 20,
                type: CloneBaseObjectType.Item,
                displayName: "SS-31 Loot",
                inventoryCoid: claimedCoid,
                itemCreator: new FakePickupItemCreator(),
                characterCoid: CharacterCoid);

            Assert.IsNotNull(result.AddedItem, result.Message);
            Assert.IsNotNull(harness.Inventory.FindByCoid(claimedCoid),
                "picked-up item must land at the allocator-minted coid, not the character coid");
            Assert.IsNull(harness.Inventory.FindByCoid(CharacterCoid),
                "picked-up item must never be stored under the character's own coid");

            Assert.IsTrue(
                harness.Persistence.Upserted.Any(u => u.CharacterCoid == CharacterCoid && u.Item.Coid == claimedCoid),
                "persistence must record the cargo upsert at the allocator-minted coid");
        }
        finally
        {
            InventoryRuntime.AllocatePersistentCoid = savedAllocator;
        }

        // Documentation-value check: the harness wires RecordingInventoryPersistence (a fake
        // that never touches a DB), so this would trivially pass even pre-fix. It is asserted
        // anyway so the pin documents, next to the allocator assertions above, exactly which
        // rows a real persistence layer must never see touched for this scenario.
        using var verify = new CharContext(_options);
        var charRow = verify.SimpleObjects.Find(CharacterCoid);
        var vehRow = verify.SimpleObjects.Find(VehicleCoid);
        Assert.AreEqual((byte)CloneBaseObjectType.Character, charRow!.Type, "character row must be byte-identical to seed");
        Assert.AreEqual(CharacterCbid, charRow.CBID, "character row CBID must be byte-identical to seed");
        Assert.AreEqual((byte)CloneBaseObjectType.Vehicle, vehRow!.Type, "vehicle row must be byte-identical to seed");
        Assert.AreEqual(VehicleCbid, vehRow.CBID, "vehicle row CBID must be byte-identical to seed");
    }

    [TestMethod]
    public void SS31_OldBugShape_EnsureSimpleObjectAtCharacterCoid_ThrowsAndLeavesRowIntact()
    {
        // 12853 is the live "Donuts incident" weapon CBID that was actually written over the
        // character's simple_object row before the SS-31 fix — kept verbatim (not swapped for
        // an arbitrary weapon CBID) so this pin reproduces the exact historical collision shape.
        Assert.ThrowsException<InvalidOperationException>(() =>
            InventoryPersistence.Instance.EnsureSimpleObject(CharacterCoid, (byte)CloneBaseObjectType.Weapon, 12853));

        using var verify = new CharContext(_options);
        var row = verify.SimpleObjects.Find(CharacterCoid);
        Assert.AreEqual((byte)CloneBaseObjectType.Character, row!.Type,
            "guard must refuse to overwrite the character row with weapon/item data");
        Assert.AreEqual(CharacterCbid, row.CBID, "character CBID must survive the rejected overwrite attempt");
    }

    private sealed class FakePickupItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y) =>
            InventoryItemCreateResult.Success(
                new CreateSimpleObjectPacket
                {
                    CBID = entry.Cbid,
                    ObjectId = new TFID(coid, global: true),
                    InventoryPositionX = x,
                    InventoryPositionY = y,
                    Quantity = 1,
                    IsInInventory = true,
                    IsIdentified = true,
                    IsBound = false
                },
                entry.DisplayName);
    }
}
