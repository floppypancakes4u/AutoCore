using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Experience;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Experience;

[TestClass]
public class CharacterProgressPersistenceTests
{
    private string _dbName = null!;
    private CharacterProgressPersistence _persist = null!;

    [TestInitialize]
    public void Init()
    {
        _dbName = "xp-progress-" + Guid.NewGuid().ToString("N");
        _persist = new CharacterProgressPersistence();
        _persist.CreateContext = CreateContext;
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _persist.ResetForTests();
        CharacterProgressPersistence.Instance.ResetForTests();
    }

    private CharContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CharContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new CharContext(options);
    }

    private void SeedCharacter(long coid, byte level = 1, int xp = 0, short skill = 0, short attrib = 0, short research = 0)
    {
        using var ctx = CreateContext();
        ctx.Characters.Add(new CharacterData
        {
            Coid = coid,
            AccountId = 1,
            Name = $"C{coid}",
            Level = level,
            Experience = xp,
            SkillPoints = skill,
            AttributePoints = attrib,
            ResearchPoints = research,
        });
        ctx.SaveChanges();
    }

    [TestMethod]
    public void LoadProgress_MissingCharacter_ReturnsDefaultLevel1()
    {
        var snap = _persist.LoadProgress(999999);
        Assert.AreEqual((byte)1, snap.Level);
        Assert.AreEqual(0, snap.Experience);
    }

    [TestMethod]
    public void LoadProgress_ExistingCharacter_ReturnsStoredFields()
    {
        SeedCharacter(100, level: 7, xp: 22000, skill: 2, attrib: 4, research: 1);
        var snap = _persist.LoadProgress(100);
        Assert.AreEqual((byte)7, snap.Level);
        Assert.AreEqual(22000, snap.Experience);
        Assert.AreEqual((short)2, snap.SkillPoints);
        Assert.AreEqual((short)4, snap.AttributePoints);
        Assert.AreEqual((short)1, snap.ResearchPoints);
    }

    [TestMethod]
    public void SaveProgress_UpdatesAbsoluteFields()
    {
        SeedCharacter(200, level: 1, xp: 0);
        _persist.SaveProgress(200, new CharacterProgressSnapshot(5, 9000, 3, 6, 2));

        using var verify = CreateContext();
        var row = verify.Characters.Single(c => c.Coid == 200);
        Assert.AreEqual((byte)5, row.Level);
        Assert.AreEqual(9000, row.Experience);
        Assert.AreEqual((short)3, row.SkillPoints);
        Assert.AreEqual((short)6, row.AttributePoints);
        Assert.AreEqual((short)2, row.ResearchPoints);
    }

    [TestMethod]
    public void SaveProgress_MissingCharacter_Throws()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
            _persist.SaveProgress(404, new CharacterProgressSnapshot(2, 100)));
        Assert.IsTrue(ex.Message.Contains("404"));
        Assert.IsTrue(ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SaveThenLoad_RoundTrip()
    {
        SeedCharacter(300);
        var written = new CharacterProgressSnapshot(10, 39000, 5, 8, 3);
        _persist.SaveProgress(300, written);
        var loaded = _persist.LoadProgress(300);
        Assert.AreEqual(written.Level, loaded.Level);
        Assert.AreEqual(written.Experience, loaded.Experience);
        Assert.AreEqual(written.SkillPoints, loaded.SkillPoints);
        Assert.AreEqual(written.AttributePoints, loaded.AttributePoints);
        Assert.AreEqual(written.ResearchPoints, loaded.ResearchPoints);
    }
}
