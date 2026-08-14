using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

/// <summary>
/// The live Back Range freeze predicts on one thing: whether the shared map has been reset since
/// the server started (6/6 in the 2026-08-13 logs — every pre-reset entry worked, every post-reset
/// entry froze). Exposing that count lets a world entry state which side of the line it is on,
/// without the operator having to correlate timestamps by hand.
/// </summary>
[TestClass]
public class SharedMapResetCountTests
{
    [TestMethod]
    public void FreshMap_HasNoResets()
    {
        var map = CreateMap(9811);

        Assert.AreEqual(0, map.LocalWorldResetCount);
    }

    [TestMethod]
    public void EachReset_IncrementsCount()
    {
        var map = CreateMap(9812);
        map.InitializeLocalObjectsForTests();

        map.ResetLocalWorldToAuthored();
        map.ResetLocalWorldToAuthored();

        Assert.AreEqual(2, map.LocalWorldResetCount);
    }

    /// <summary>
    /// The reset refuses to run while a Character is still present (PlayerCount desync guard). A
    /// skipped reset leaves the world untouched, so it must not be counted as one.
    /// </summary>
    [TestMethod]
    public void SkippedReset_DoesNotIncrementCount()
    {
        var map = CreateMap(9813);
        var character = new Character();
        character.SetCoid(9_813_001, true);
        character.AttachTestDataForTests();
        character.SetMap(map);

        map.ResetLocalWorldToAuthored();

        Assert.AreEqual(0, map.LocalWorldResetCount,
            "a reset that bailed on the straggler guard changed nothing");

        character.SetMap(null);
        ObjectManager.Instance.Remove(character.ObjectId.Coid);
    }

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_resetcount_{continentId}",
            DisplayName = "reset-count",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}
