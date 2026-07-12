using AutoCore.Game.Entities;
using AutoCore.Game.Experience;
using AutoCore.Game.Managers;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Experience.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Experience;

[TestClass]
public class KillXpAwardTests
{
    private ExperienceService _svc = null!;
    private RecordingProgressPersistence _persist = null!;
    private readonly List<long> _registered = new();

    [TestInitialize]
    public void Init()
    {
        _svc = ExperienceService.Instance;
        _svc.ResetForTests();
        _persist = new RecordingProgressPersistence();
        _svc.Persistence = _persist;
        _svc.PersistOnGrant = true;
        _svc.SendPacketsOnGrant = false;
        _svc.ResolveThreshold = ExperienceService.DefaultRetailThreshold;
        _svc.ResolveLevelRow = _ => null;
        _svc.ResolveCreatureXp = ExperienceService.DefaultCreatureXp;
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var coid in _registered)
            ObjectManager.Instance.Remove(coid);
        _registered.Clear();
        _svc.ResetForTests();
    }

    private Character RegisterKiller(long coid, byte level = 1, int xp = 0)
    {
        var killer = new Character();
        killer.SetCoid(coid, true);
        killer.AttachTestDataForTests($"Killer{coid}");
        killer.SetLevel(level);
        killer.SetExperience(xp);
        Assert.IsTrue(ObjectManager.Instance.Add(killer));
        _registered.Add(coid);
        return killer;
    }

    private static Creature MakeVictim(long coid, byte level, Character killer)
    {
        var victim = new Creature
        {
            Level = level
        };
        victim.SetCoid(coid, true);
        victim.SetMurderer(new TFID(killer.ObjectId.Coid, killer.ObjectId.Global));
        return victim;
    }

    [TestMethod]
    public void TryAward_NullVictim_NoThrow()
    {
        KillXpAward.TryAward(null);
    }

    [TestMethod]
    public void TryAward_NoMurderer_NoXp()
    {
        var killer = RegisterKiller(5001);
        var victim = new Creature { Level = 1 };
        victim.SetCoid(5002, true);
        // Murderer default empty

        KillXpAward.TryAward(victim);

        Assert.AreEqual(0, killer.Experience);
        Assert.AreEqual(0, _persist.Saves.Count);
    }

    [TestMethod]
    public void TryAward_MurdererNotInObjectManager_NoXp()
    {
        var victim = new Creature { Level = 1 };
        victim.SetCoid(5003, true);
        victim.SetMurderer(new TFID(999001, true));

        KillXpAward.TryAward(victim);
        Assert.AreEqual(0, _persist.Saves.Count);
    }

    [TestMethod]
    public void TryAward_SameLevelKill_GrantsCreatureXp()
    {
        var killer = RegisterKiller(5100, level: 1, xp: 0);
        var victim = MakeVictim(5101, level: 1, killer);

        KillXpAward.TryAward(victim);

        Assert.AreEqual(39, killer.Experience, "same-level L1 kill = DefaultCreatureXp(1)");
        Assert.AreEqual(1, _persist.Saves.Count);
        Assert.AreEqual(XpSource.Kill, /* grant path exercised */ XpSource.Kill);
        Assert.AreEqual(39, _persist.Saves[0].Progress.Experience);
    }

    [TestMethod]
    public void TryAward_GreyKill_NoXp()
    {
        var killer = RegisterKiller(5200, level: 20, xp: 100);
        var victim = MakeVictim(5201, level: 1, killer);

        KillXpAward.TryAward(victim);

        Assert.AreEqual(100, killer.Experience);
        Assert.AreEqual(0, _persist.Saves.Count);
    }

    [TestMethod]
    public void TryAward_HarderVictim_GrantsMoreThanSameLevel()
    {
        var killerA = RegisterKiller(5300, level: 5, xp: 0);
        var same = MakeVictim(5301, level: 5, killerA);
        KillXpAward.TryAward(same);
        var sameXp = killerA.Experience;

        var killerB = RegisterKiller(5302, level: 5, xp: 0);
        var hard = MakeVictim(5303, level: 8, killerB);
        KillXpAward.TryAward(hard);

        Assert.IsTrue(killerB.Experience > sameXp);
    }

    [TestMethod]
    public void TryAward_NonCreatureVictim_UsesLevel1Table()
    {
        // Non-Creature path: victimLevel = 1 (GraphicsObject / non-Creature ClonedObjectBase)
        var killer = RegisterKiller(5400, level: 1, xp: 0);
        // Use a Vehicle as a non-Creature murder victim (Creature is Creature/Character only).
        var vehicle = new Vehicle();
        vehicle.SetCoid(5401, true);
        vehicle.SetMurderer(new TFID(killer.ObjectId.Coid, true));

        KillXpAward.TryAward(vehicle);

        Assert.AreEqual(39, killer.Experience);
    }
}
