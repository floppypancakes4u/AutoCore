using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.Npc;

/// <summary>
/// Stage 10: <see cref="FactionHostility.IsHostile"/> is the single choke point for aggro
/// decisions. wad.xml tFactions: 0 Humans / 1 Mutants / 2 Biomeks are player races (never mutual
/// aggro); >= 3 are NPC factions (aggressive toward any real faction != themselves); -1 unset and
/// -100 neutral never aggro either way.
/// </summary>
[TestClass]
public class FactionHostilityTests
{
    [TestMethod]
    public void IsHostile_MatrixCases()
    {
        // NPC faction (>=3) is hostile to any real faction (>=0) other than itself, both ways.
        Assert.IsTrue(FactionHostility.IsHostile(3, 0), "NPC faction 3 must aggro human player (0)");
        Assert.IsTrue(FactionHostility.IsHostile(0, 3), "hostility is symmetric: player (0) vs NPC 3");
        Assert.IsTrue(FactionHostility.IsHostile(3, 1), "NPC faction 3 must aggro mutant player (1)");
        Assert.IsTrue(FactionHostility.IsHostile(3, 2), "NPC faction 3 must aggro biomek player (2)");
        Assert.IsTrue(FactionHostility.IsHostile(3, 4), "distinct NPC factions must aggro each other");
        Assert.IsTrue(FactionHostility.IsHostile(4, 3), "distinct NPC factions must aggro each other (reverse)");

        // Same faction never aggros.
        Assert.IsFalse(FactionHostility.IsHostile(3, 3), "same NPC faction must not aggro itself");
        Assert.IsFalse(FactionHostility.IsHostile(0, 0), "same player faction must not aggro itself");

        // Player factions (0/1/2) never mutually aggro.
        Assert.IsFalse(FactionHostility.IsHostile(0, 1), "player races must never mutual-aggro (0 vs 1)");
        Assert.IsFalse(FactionHostility.IsHostile(1, 2), "player races must never mutual-aggro (1 vs 2)");
        Assert.IsFalse(FactionHostility.IsHostile(2, 0), "player races must never mutual-aggro (2 vs 0)");

        // Unset (-1) and neutral (-100) never aggro, in either slot.
        Assert.IsFalse(FactionHostility.IsHostile(3, -1), "NPC vs unset (-1) must not aggro");
        Assert.IsFalse(FactionHostility.IsHostile(-1, 3), "unset (-1) vs NPC must not aggro");
        Assert.IsFalse(FactionHostility.IsHostile(3, -100), "NPC vs neutral (-100) must not aggro");
        Assert.IsFalse(FactionHostility.IsHostile(-100, 3), "neutral (-100) vs NPC must not aggro");
        Assert.IsFalse(FactionHostility.IsHostile(-1, -1), "unset vs unset must not aggro");
    }
}
