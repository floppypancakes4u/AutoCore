namespace AutoCore.Game.Inventory;

using AutoCore.Game.Structures;

/// <summary>
/// Adjusts client-reported toss positions so world loot does not spawn on top of the vehicle.
/// </summary>
public static class ItemDropSpawnPosition
{
    public const float DefaultDistanceMultiplier = 2f;
    public const float DefaultMinHorizDistance = 4f;
    private const float CoincidentEpsilon = 0.1f;

    public static Vector3 Adjust(
        Vector3 vehiclePosition,
        Vector3 clientDropPosition,
        float distanceMultiplier = DefaultDistanceMultiplier,
        float minHorizDistance = DefaultMinHorizDistance)
    {
        var dx = clientDropPosition.X - vehiclePosition.X;
        var dz = clientDropPosition.Z - vehiclePosition.Z;
        var horizDist = (float)Math.Sqrt(dx * dx + dz * dz);

        float dirX;
        float dirZ;
        if (horizDist < CoincidentEpsilon)
        {
            dirX = 0f;
            dirZ = 1f;
            horizDist = 0f;
        }
        else
        {
            dirX = dx / horizDist;
            dirZ = dz / horizDist;
        }

        var targetDist = Math.Max(horizDist * distanceMultiplier, minHorizDistance);

        return new Vector3(
            vehiclePosition.X + dirX * targetDist,
            clientDropPosition.Y,
            vehiclePosition.Z + dirZ * targetDist);
    }
}
