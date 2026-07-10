namespace AutoCore.Game.Map;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;

/// <summary>
/// Uniform spatial hash over the XZ plane for entities on a <see cref="SectorMap"/>.
/// Cell size 128f keeps aggro-radius queries (~60) to at most 4 cells and continent-wide
/// scope queries (400+) to ~49 cells, versus a full scan of every map object.
/// <see cref="Add"/>/<see cref="Remove"/> are driven by <see cref="SectorMap.EnterMap"/> and
/// <see cref="SectorMap.LeaveMap"/>. Positions can still drift into a different cell between
/// ticks without going through those paths, so <see cref="RebucketSweep"/> re-homes any tracked
/// entity whose Position moved into a new cell — one O(N) pass per main-loop tick, called from
/// MapManager, makes the grid immune to any writer that forgets to call EnterMap/LeaveMap.
/// </summary>
public class SpatialHashGrid
{
    private const float CellSize = 128f;

    private readonly Dictionary<long, List<ClonedObjectBase>> _cells = new();
    private readonly Dictionary<ClonedObjectBase, long> _lastCell = new();

    /// <summary>Entities actually re-bucketed by the most recent <see cref="RebucketSweep"/> call.</summary>
    internal int LastSweepRelocationCount { get; private set; }

    /// <summary>Buckets an entity by its current Position. No-op for Trigger/Reaction/SpawnPoint.</summary>
    public void Add(ClonedObjectBase clonedObject)
    {
        if (ShouldSkip(clonedObject))
            return;

        var key = CellKeyFor(clonedObject.Position);
        AddToCell(key, clonedObject);
        _lastCell[clonedObject] = key;
    }

    /// <summary>Removes a previously-added entity. No-op if it was never tracked (e.g. skipped types).</summary>
    public void Remove(ClonedObjectBase clonedObject)
    {
        if (!_lastCell.TryGetValue(clonedObject, out var key))
            return;

        RemoveFromCell(key, clonedObject);
        _lastCell.Remove(clonedObject);
    }

    /// <summary>
    /// Re-homes any tracked entity whose Position now falls in a different cell than the one it
    /// was last bucketed into. Entities that stayed in the same cell are left untouched.
    /// </summary>
    public void RebucketSweep()
    {
        LastSweepRelocationCount = 0;

        List<(ClonedObjectBase Entity, long OldKey, long NewKey)> moved = null;

        foreach (var (entity, oldKey) in _lastCell)
        {
            var newKey = CellKeyFor(entity.Position);
            if (newKey == oldKey)
                continue;

            moved ??= new List<(ClonedObjectBase, long, long)>();
            moved.Add((entity, oldKey, newKey));
        }

        if (moved == null)
            return;

        foreach (var (entity, oldKey, newKey) in moved)
        {
            RemoveFromCell(oldKey, entity);
            AddToCell(newKey, entity);
            _lastCell[entity] = newKey;
        }

        LastSweepRelocationCount = moved.Count;
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> (cleared first) with every tracked entity within
    /// <paramref name="radius"/> of <paramref name="center"/>, using XZ-plane distance only.
    /// </summary>
    public void QueryRadius(Vector3 center, float radius, List<ClonedObjectBase> buffer)
    {
        buffer.Clear();

        var radiusSq = radius * radius;
        var minCellX = CellCoord(center.X - radius);
        var maxCellX = CellCoord(center.X + radius);
        var minCellZ = CellCoord(center.Z - radius);
        var maxCellZ = CellCoord(center.Z + radius);

        for (var cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            for (var cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                if (!_cells.TryGetValue(MakeKey(cellX, cellZ), out var bucket))
                    continue;

                foreach (var entity in bucket)
                {
                    if (DistSqXZ(entity.Position, center) <= radiusSq)
                        buffer.Add(entity);
                }
            }
        }
    }

    private static bool ShouldSkip(ClonedObjectBase clonedObject) =>
        clonedObject is Trigger or Reaction or SpawnPoint;

    private static float DistSqXZ(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    private static int CellCoord(float value) => (int)Math.Floor(value / CellSize);

    private static long MakeKey(int cellX, int cellZ) => ((long)cellX << 32) | (uint)cellZ;

    private static long CellKeyFor(Vector3 position) => MakeKey(CellCoord(position.X), CellCoord(position.Z));

    private void AddToCell(long key, ClonedObjectBase clonedObject)
    {
        if (!_cells.TryGetValue(key, out var bucket))
        {
            bucket = new List<ClonedObjectBase>();
            _cells[key] = bucket;
        }

        bucket.Add(clonedObject);
    }

    private void RemoveFromCell(long key, ClonedObjectBase clonedObject)
    {
        if (!_cells.TryGetValue(key, out var bucket))
            return;

        bucket.Remove(clonedObject);
        if (bucket.Count == 0)
            _cells.Remove(key);
    }
}
