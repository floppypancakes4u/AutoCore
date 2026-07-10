using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using System.Collections.Generic;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Stage 5: pure unit tests for <see cref="SpatialHashGrid"/> — no SectorMap/asset data needed.
/// </summary>
[TestClass]
public class SpatialHashGridTests
{
    [TestMethod]
    public void AddQuery_ReturnsOnlyWithinRadius()
    {
        var grid = new SpatialHashGrid();
        var near = MakeCreature(1, new Vector3(10f, 0f, 10f));
        var far = MakeCreature(2, new Vector3(1000f, 0f, 1000f));

        grid.Add(near);
        grid.Add(far);

        var buffer = new List<ClonedObjectBase>();
        grid.QueryRadius(new Vector3(0f, 0f, 0f), 50f, buffer);

        Assert.IsTrue(buffer.Contains(near), "Entity within radius must be returned.");
        Assert.IsFalse(buffer.Contains(far), "Entity outside radius must not be returned.");
    }

    [TestMethod]
    public void QueryRadius_SpansCellBoundaries()
    {
        var grid = new SpatialHashGrid();
        // Cell size is 128f: (130, _, 0) sits in cell (1, 0) while the query below is centered
        // in cell (0, 0). The radius crosses the cell boundary, so a naive single-cell lookup
        // would miss this entity.
        var entity = MakeCreature(1, new Vector3(130f, 0f, 0f));

        grid.Add(entity);

        var buffer = new List<ClonedObjectBase>();
        grid.QueryRadius(new Vector3(127f, 0f, 0f), 10f, buffer);

        Assert.IsTrue(buffer.Contains(entity), "Query must scan all cells overlapped by the radius, not just the center cell.");
    }

    [TestMethod]
    public void Remove_ExcludesFromQuery()
    {
        var grid = new SpatialHashGrid();
        var entity = MakeCreature(1, new Vector3(0f, 0f, 0f));

        grid.Add(entity);
        grid.Remove(entity);

        var buffer = new List<ClonedObjectBase>();
        grid.QueryRadius(new Vector3(0f, 0f, 0f), 50f, buffer);

        Assert.IsFalse(buffer.Contains(entity), "Removed entity must not be returned by later queries.");
    }

    [TestMethod]
    public void RebucketSweep_MovedEntityFoundAtNewPosition_NotOld()
    {
        var grid = new SpatialHashGrid();
        var entity = MakeCreature(1, new Vector3(0f, 0f, 0f));
        grid.Add(entity);

        // Move far enough to land in a different cell (cell size 128f) without going through
        // Add/Remove — this is exactly the "missed Position writer" scenario RebucketSweep covers.
        entity.Position = new Vector3(500f, 0f, 500f);
        grid.RebucketSweep();

        var atNewPosition = new List<ClonedObjectBase>();
        grid.QueryRadius(new Vector3(500f, 0f, 500f), 10f, atNewPosition);
        Assert.IsTrue(atNewPosition.Contains(entity), "Entity must be found at its new position after RebucketSweep.");

        var atOldPosition = new List<ClonedObjectBase>();
        grid.QueryRadius(new Vector3(0f, 0f, 0f), 10f, atOldPosition);
        Assert.IsFalse(atOldPosition.Contains(entity), "Entity must no longer be found at its old position after RebucketSweep.");
    }

    [TestMethod]
    public void RebucketSweep_UnmovedEntity_NoReallocation()
    {
        var grid = new SpatialHashGrid();
        var entity = MakeCreature(1, new Vector3(10f, 0f, 10f));
        grid.Add(entity);

        grid.RebucketSweep();

        Assert.AreEqual(0, grid.LastSweepRelocationCount, "An entity that stayed in the same cell must not be re-bucketed.");

        var buffer = new List<ClonedObjectBase>();
        grid.QueryRadius(new Vector3(10f, 0f, 10f), 5f, buffer);
        Assert.IsTrue(buffer.Contains(entity));
    }

    [TestMethod]
    public void Add_SkipsTriggersReactionsSpawnPoints()
    {
        var grid = new SpatialHashGrid();
        var trigger = new Trigger(new TriggerTemplate()) { Position = new Vector3(0f, 0f, 0f) };
        var reaction = new Reaction(new ReactionTemplate()) { Position = new Vector3(0f, 0f, 0f) };
        var spawnPoint = new SpawnPoint(new SpawnPointTemplate()) { Position = new Vector3(0f, 0f, 0f) };

        grid.Add(trigger);
        grid.Add(reaction);
        grid.Add(spawnPoint);

        var buffer = new List<ClonedObjectBase>();
        grid.QueryRadius(new Vector3(0f, 0f, 0f), 200f, buffer);

        Assert.AreEqual(0, buffer.Count, "Trigger/Reaction/SpawnPoint must never be bucketed by the spatial grid.");
    }

    private static Creature MakeCreature(long coid, Vector3 position)
    {
        var creature = new Creature();
        creature.SetCoid(coid, true);
        creature.Position = position;
        return creature;
    }
}
