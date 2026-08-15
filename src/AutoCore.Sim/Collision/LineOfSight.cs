using AutoCore.Game.Structures;

namespace AutoCore.Sim.Collision;

/// <summary>
/// Segment query against a built <see cref="StaticCollisionWorld"/>. Used by turret LOS.
/// </summary>
public static class LineOfSight
{
    /// <summary>
    /// True when no hull is hit before <paramref name="to"/> (minus <paramref name="stopShort"/>).
    /// A null or empty world is treated as clear.
    /// </summary>
    public static bool IsClear(StaticCollisionWorld world, Vector3 from, Vector3 to, float stopShort = 0.25f)
    {
        if (world == null)
            return true;

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dz = to.Z - from.Z;
        var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (dist <= stopShort)
            return true;

        var inv = 1f / dist;
        var dir = new Vector3(dx * inv, dy * inv, dz * inv);
        return !world.Raycast(from, dir, dist - stopShort, out _, out _);
    }

    /// <summary>
    /// Turret fire gate: unknown/unbuilt world must not grant a shot (shooting through
    /// walls while the hull cache is empty). A built world uses <see cref="IsClear"/>.
    /// </summary>
    public static bool TurretMayShoot(StaticCollisionWorld world, Vector3 from, Vector3 to)
    {
        if (world == null)
            return false;
        return IsClear(world, from, to);
    }
}
