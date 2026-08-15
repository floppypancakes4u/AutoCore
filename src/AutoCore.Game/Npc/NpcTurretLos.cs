namespace AutoCore.Game.Npc;

using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Line-of-sight seam for stationary turrets. Sector/Sim installs a hull-world raycast;
/// tests inject a predicate. Null / missing world degrades to clear (turrets still shoot).
/// </summary>
public static class NpcTurretLos
{
    /// <summary>Aim height added to both endpoints so the ray is not buried in the floor.</summary>
    internal const float AimHeight = 1.2f;

    /// <summary>
    /// Optional query: true when the raised segment from A to B is unobstructed.
    /// Null means "clear".
    /// </summary>
    public static Func<SectorMap, Vector3, Vector3, bool> TryHasClearLos { get; set; }

    public static bool HasClearLos(SectorMap map, ClonedObjectBase from, ClonedObjectBase to)
    {
        if (from == null || to == null)
            return false;

        var a = from.Position;
        var b = to.Position;
        a.Y += AimHeight;
        b.Y += AimHeight;
        return TryHasClearLos?.Invoke(map, a, b) ?? true;
    }
}
