using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;

/// <summary>
/// SS-31: <c>SimpleObject.LoadFromDB</c> already refuses character/vehicle identity rows.
/// A {Type=0, CBID=0} placeholder row (item coid allocator reserved the coid but never
/// persisted real item data) reaches <c>LoadCloneBase(0)</c> and throws — this must be
/// refused before LoadCloneBase, same as Character/Vehicle.
/// </summary>
[TestClass]
public class SimpleObjectLoadGuardTests
{
    private DbContextOptions<CharContext> _options = null!;

    [TestInitialize]
    public void Init()
    {
        _options = new DbContextOptionsBuilder<CharContext>()
            .UseInMemoryDatabase("so-load-guard-" + Guid.NewGuid().ToString("N"))
            .Options;
    }

    [TestMethod]
    public void LoadFromDB_PlaceholderRow_ReturnsFalse()
    {
        const long coid = 21001;

        using (var seed = new CharContext(_options))
        {
            seed.SimpleObjects.Add(new SimpleObjectData { Coid = coid, Type = 0, CBID = 0 });
            seed.SaveChanges();
        }

        using var context = new CharContext(_options);
        var item = new SimpleObject(GraphicsObjectType.Graphics);

        var loaded = item.LoadFromDB(context, coid);

        Assert.IsFalse(loaded, "placeholder {Type=0, CBID=0} rows must not reach LoadCloneBase(0) (SS-31)");
    }
}
