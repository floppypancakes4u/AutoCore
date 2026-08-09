using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;

/// <summary>
/// SS-31 tripwire: <c>EnsureSimpleObject</c> used to overwrite ANY existing simple_object row
/// with item data. When the (buggy) item coid allocator produced a coid already owned by a
/// character or vehicle, the overwrite corrupted that object's identity row and the client
/// access-violated at character select. The guard must refuse cross-category overwrites.
/// </summary>
[TestClass]
public class SimpleObjectOverwriteGuardTests
{
    private DbContextOptions<CharContext> _options = null!;

    [TestInitialize]
    public void Init()
    {
        _options = new DbContextOptionsBuilder<CharContext>()
            .UseInMemoryDatabase("so-guard-" + Guid.NewGuid().ToString("N"))
            .Options;
        InventoryPersistence.CreateContext = () => new CharContext(_options);
    }

    [TestCleanup]
    public void Cleanup()
    {
        InventoryPersistence.ResetForTests();
    }

    [TestMethod]
    public void EnsureSimpleObject_RefusesToOverwriteCharacterRow()
    {
        using (var seed = new CharContext(_options))
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = 18274,
                Type = (byte)CloneBaseObjectType.Character,
                CBID = 34
            });
            seed.SaveChanges();
        }

        Assert.ThrowsException<InvalidOperationException>(() =>
            InventoryPersistence.Instance.EnsureSimpleObject(18274, (byte)CloneBaseObjectType.Item, 17774));

        using var verify = new CharContext(_options);
        var row = verify.SimpleObjects.Find(18274L);
        Assert.AreEqual((byte)CloneBaseObjectType.Character, row.Type, "character row must survive untouched");
        Assert.AreEqual(34, row.CBID);
    }

    [TestMethod]
    public void EnsureSimpleObject_RefusesToOverwriteVehicleRow()
    {
        using (var seed = new CharContext(_options))
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = 18275,
                Type = (byte)CloneBaseObjectType.Vehicle,
                CBID = 19658
            });
            seed.SaveChanges();
        }

        Assert.ThrowsException<InvalidOperationException>(() =>
            InventoryPersistence.Instance.EnsureSimpleObject(18275, (byte)CloneBaseObjectType.Weapon, 1552));

        using var verify = new CharContext(_options);
        Assert.AreEqual((byte)CloneBaseObjectType.Vehicle, verify.SimpleObjects.Find(18275L).Type);
    }

    [TestMethod]
    public void EnsureSimpleObject_UpdatesPlaceholderRow_FromAllocator()
    {
        // The allocator reserves the coid with a Type=0 placeholder; persist fills it in.
        using (var seed = new CharContext(_options))
        {
            seed.SimpleObjects.Add(new SimpleObjectData { Coid = 500, Type = 0, CBID = 0 });
            seed.SaveChanges();
        }

        InventoryPersistence.Instance.EnsureSimpleObject(500, (byte)CloneBaseObjectType.Item, 2993);

        using var verify = new CharContext(_options);
        var row = verify.SimpleObjects.Find(500L);
        Assert.AreEqual((byte)CloneBaseObjectType.Item, row.Type);
        Assert.AreEqual(2993, row.CBID);
    }

    [TestMethod]
    public void EnsureSimpleObject_UpdatesSameCategoryRow_Normally()
    {
        using (var seed = new CharContext(_options))
        {
            seed.SimpleObjects.Add(new SimpleObjectData { Coid = 600, Type = (byte)CloneBaseObjectType.Item, CBID = 2993 });
            seed.SaveChanges();
        }

        InventoryPersistence.Instance.EnsureSimpleObject(600, (byte)CloneBaseObjectType.Item, 5477);

        using var verify = new CharContext(_options);
        Assert.AreEqual(5477, verify.SimpleObjects.Find(600L).CBID, "same-category re-save must still update normally");
    }
}
