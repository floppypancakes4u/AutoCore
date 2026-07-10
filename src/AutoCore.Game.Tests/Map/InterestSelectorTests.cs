using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Stage 6: pure unit tests for <see cref="InterestSelector"/> — no TNL connection / ghosts needed.
/// The already-ghosted memory is injected as a predicate.
/// </summary>
[TestClass]
public class InterestSelectorTests
{
    private static readonly Func<ClonedObjectBase, bool> NoneGhosted = _ => false;

    private float _origAdd;
    private float _origDrop;
    private float _origMgAdd;
    private float _origMgDrop;
    private int _origCap;

    [TestInitialize]
    public void SetUp()
    {
        _origAdd = InterestSelector.BaseScopeAddRadius;
        _origDrop = InterestSelector.BaseScopeDropRadius;
        _origMgAdd = InterestSelector.MissionGiverAddRadius;
        _origMgDrop = InterestSelector.MissionGiverDropRadius;
        _origCap = InterestSelector.ScopeSoftCap;
    }

    [TestCleanup]
    public void TearDown()
    {
        InterestSelector.BaseScopeAddRadius = _origAdd;
        InterestSelector.BaseScopeDropRadius = _origDrop;
        InterestSelector.MissionGiverAddRadius = _origMgAdd;
        InterestSelector.MissionGiverDropRadius = _origMgDrop;
        InterestSelector.ScopeSoftCap = _origCap;
    }

    [TestMethod]
    public void Select_PlayersAlwaysIncluded_RegardlessOfDistance()
    {
        var player = MakeCharacter(1, 100000f); // absurdly far
        var output = Run(
            self: null,
            isTown: false,
            players: new[] { player });

        Assert.IsTrue(output.Contains(player), "Players must be scoped regardless of distance.");
    }

    [TestMethod]
    public void Select_MissionGiver_IncludedAtExtendedRadius_OthersNot()
    {
        // Extended radius 800 includes the mission giver at 700; a plain NPC at 700 is beyond
        // the base add radius (400) and base drop radius (460), so it is not scoped.
        var giver = MakeCreature(1, 700f, missionGiver: true);
        var plain = MakeCreature(2, 700f);

        var output = Run(
            self: null,
            isTown: false,
            missionGivers: new[] { giver },
            nearby: new[] { plain });

        Assert.IsTrue(output.Contains(giver), "Mission giver within 800 must be scoped.");
        Assert.IsFalse(output.Contains(plain), "Plain NPC beyond base add radius must not be scoped.");
    }

    [TestMethod]
    public void Select_BudgetCap_KeepsNearestNewCandidates()
    {
        // 900 new candidates all inside the add radius; soft cap keeps exactly the nearest 700.
        var candidates = new List<ClonedObjectBase>();
        for (var i = 1; i <= 900; i++)
            candidates.Add(MakeCreature(i, i * 0.4f)); // 0.4 .. 360, all < 400

        var output = Run(
            self: null,
            isTown: false,
            nearby: candidates);

        Assert.AreEqual(InterestSelector.ScopeSoftCap, output.Count, "Tier-3 output must be capped at the soft cap.");
        // Nearest 700 (i = 1..700) kept; farthest 200 dropped.
        Assert.IsTrue(output.Contains(candidates[0]), "Nearest candidate must be kept.");
        Assert.IsTrue(output.Contains(candidates[699]), "700th nearest candidate must be kept.");
        Assert.IsFalse(output.Contains(candidates[700]), "701st nearest candidate must be dropped by the cap.");
        Assert.IsFalse(output.Contains(candidates[899]), "Farthest candidate must be dropped by the cap.");
    }

    [TestMethod]
    public void Select_Hysteresis_GhostedEntityRetainedBetweenAddAndDrop()
    {
        // 430 is between add (400) and drop (460): an already-ghosted entity is retained.
        var entity = MakeCreature(1, 430f);

        var output = Run(
            self: null,
            isTown: false,
            nearby: new[] { entity },
            isGhosted: e => ReferenceEquals(e, entity));

        Assert.IsTrue(output.Contains(entity), "Already-ghosted entity within the hysteresis band must be retained.");
    }

    [TestMethod]
    public void Select_Hysteresis_NotGhosted_NotAdded()
    {
        // 430 is beyond the add radius (400): a not-yet-ghosted entity is not scoped.
        var entity = MakeCreature(1, 430f);

        var output = Run(
            self: null,
            isTown: false,
            nearby: new[] { entity },
            isGhosted: NoneGhosted);

        Assert.IsFalse(output.Contains(entity), "New entity beyond the add radius must not be scoped.");
    }

    [TestMethod]
    public void Select_Hysteresis_DroppedBeyondDropRadius()
    {
        // 500 is beyond the drop radius (460): even an already-ghosted entity is dropped.
        var entity = MakeCreature(1, 500f);

        var output = Run(
            self: null,
            isTown: false,
            nearby: new[] { entity },
            isGhosted: e => ReferenceEquals(e, entity));

        Assert.IsFalse(output.Contains(entity), "Ghosted entity beyond the drop radius must be dropped.");
    }

    [TestMethod]
    public void Select_TownFiltersVehicles_FieldFiltersCharacters()
    {
        var vehicle = MakeVehicle(1, 50f);
        var character = MakeCharacter(2, 50f);

        var townOutput = Run(
            self: null,
            isTown: true,
            nearby: new ClonedObjectBase[] { vehicle, character });
        Assert.IsFalse(townOutput.Contains(vehicle), "Vehicles must be hidden in towns.");
        Assert.IsTrue(townOutput.Contains(character), "Characters must be visible in towns.");

        var fieldOutput = Run(
            self: null,
            isTown: false,
            nearby: new ClonedObjectBase[] { vehicle, character });
        Assert.IsTrue(fieldOutput.Contains(vehicle), "Vehicles must be visible in the field.");
        Assert.IsFalse(fieldOutput.Contains(character), "Characters must be hidden in the field.");
    }

    [TestMethod]
    public void Select_KeptGhostsConsumeBudgetBeforeNewAdds()
    {
        InterestSelector.ScopeSoftCap = 2;

        // Two already-ghosted entities near the drop radius, and three nearer new candidates.
        var keptA = MakeCreature(1, 450f);
        var keptB = MakeCreature(2, 455f);
        var newNear1 = MakeCreature(3, 10f);
        var newNear2 = MakeCreature(4, 20f);
        var newNear3 = MakeCreature(5, 30f);

        var ghosted = new HashSet<ClonedObjectBase> { keptA, keptB };
        var output = Run(
            self: null,
            isTown: false,
            nearby: new ClonedObjectBase[] { keptA, keptB, newNear1, newNear2, newNear3 },
            isGhosted: ghosted.Contains);

        Assert.AreEqual(2, output.Count, "Kept ghosts consume the whole budget.");
        Assert.IsTrue(output.Contains(keptA) && output.Contains(keptB), "Both kept ghosts must be retained.");
        Assert.IsFalse(output.Contains(newNear1) || output.Contains(newNear2) || output.Contains(newNear3),
            "Nearer new candidates must not displace already-ghosted entities within budget.");
    }

    [TestMethod]
    public void Select_SelfAlwaysIncluded_EvenWhenFieldFiltered()
    {
        // Self is a Character; in the field characters are normally filtered, but self is pinned.
        var self = MakeCharacter(1, 0f);

        var output = Run(
            self: self,
            isTown: false,
            nearby: Array.Empty<ClonedObjectBase>());

        Assert.IsTrue(output.Contains(self), "The scope object itself must always be in scope.");
    }

    private static List<ClonedObjectBase> Run(
        ClonedObjectBase self,
        bool isTown,
        IReadOnlyList<ClonedObjectBase> players = null,
        IReadOnlyList<ClonedObjectBase> missionGivers = null,
        IReadOnlyList<ClonedObjectBase> nearby = null,
        Func<ClonedObjectBase, bool> isGhosted = null)
    {
        var output = new List<ClonedObjectBase>();
        InterestSelector.Select(
            self,
            new Vector3(0f, 0f, 0f),
            isTown,
            players ?? Array.Empty<ClonedObjectBase>(),
            missionGivers ?? Array.Empty<ClonedObjectBase>(),
            nearby ?? Array.Empty<ClonedObjectBase>(),
            isGhosted ?? NoneGhosted,
            output);
        return output;
    }

    private static Creature MakeCreature(long coid, float x, bool missionGiver = false)
    {
        var creature = new Creature();
        creature.SetCoid(coid, true);
        creature.Position = new Vector3(x, 0f, 0f);
        creature.IsMissionGiver = missionGiver;
        return creature;
    }

    private static Character MakeCharacter(long coid, float x)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.Position = new Vector3(x, 0f, 0f);
        return character;
    }

    private static Vehicle MakeVehicle(long coid, float x)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.Position = new Vector3(x, 0f, 0f);
        return vehicle;
    }
}
