namespace AutoCore.Game.Map;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

/// <summary>
/// Foreign NPC vehicle target-frame Cur/Max: CreateVehicle then CreateCreature with
/// <c>CoidCurrentVehicle</c> so client PostCreate calls SetVehicle (vehicle+0xAC).
/// See NPC.md §14.4 / <c>CVOGCreature_PostCreateFromPacket</c>.
/// </summary>
public static class ForeignNpcDriverWire
{
    /// <summary>
    /// True when <paramref name="vehicle"/> has a pure-creature driver (not a player Character).
    /// </summary>
    public static bool HasPureCreatureDriver(Vehicle vehicle)
    {
        if (vehicle?.Owner == null)
            return false;
        if (vehicle.Owner.GetAsCharacter() != null)
            return false;
        var driver = vehicle.Owner.GetAsCreature();
        return driver != null && driver.CBID > 0;
    }

    /// <summary>
    /// Builds a CreateCreature packet for the vehicle's pure-creature driver with chassis COID at +0xF8.
    /// Returns false when there is no eligible driver.
    /// </summary>
    public static bool TryBuildDriverCreate(Vehicle vehicle, out CreateCreaturePacket packet)
    {
        packet = null;
        if (vehicle == null)
            return false;

        var owner = vehicle.Owner;
        if (owner == null || owner.GetAsCharacter() != null)
            return false;

        var driver = owner.GetAsCreature();
        if (driver == null || driver.CBID <= 0)
            return false;

        packet = new CreateCreaturePacket();
        driver.WriteToPacket(packet);
        packet.CoidCurrentVehicle = vehicle.ObjectId.Coid;
        return true;
    }

    /// <summary>
    /// Sends CreateCreature after CreateVehicle so PostCreate can SetVehicle.
    /// </summary>
    public static bool TrySendDriverCreate(TNLConnection connection, Vehicle vehicle)
    {
        if (connection == null || !TryBuildDriverCreate(vehicle, out var packet))
            return false;

        connection.SendGamePacket(packet);
        if (WireDiag.Enabled)
        {
            var driver = vehicle.Owner.GetAsCreature();
            Logger.WriteLog(LogType.Network,
                "ForeignDriverCreate coid={0} driverCoid={1} driverCbid={2} vehicleCoid={3}",
                vehicle.ObjectId.Coid, driver.ObjectId.Coid, driver.CBID, vehicle.ObjectId.Coid);
        }

        return true;
    }

    /// <summary>
    /// Builds the attach-reapply packet sequence:
    /// Destroy(vehicle) → Destroy(driver) → CreateVehicle → CreateCreature(+0xF8).
    /// Returns empty when the vehicle has no pure-creature driver.
    /// </summary>
    public static List<BasePacket> BuildOwnerAttachReapplyPackets(Vehicle vehicle)
    {
        var packets = new List<BasePacket>();
        if (!HasPureCreatureDriver(vehicle))
            return packets;

        var driver = vehicle.Owner.GetAsCreature();
        packets.Add(new DestroyObjectPacket(vehicle.ObjectId));
        packets.Add(new DestroyObjectPacket(driver.ObjectId));

        var createVehicle = new CreateVehiclePacket();
        vehicle.WriteToPacket(createVehicle);
        createVehicle.IsItemLink = false;
        packets.Add(createVehicle);

        if (TryBuildDriverCreate(vehicle, out var createCreature))
            packets.Add(createCreature);

        return packets;
    }

    /// <summary>
    /// Executes owner-attach reapply: destroy chassis+driver, recreate vehicle then creature.
    /// Dirties health/wheel masks on <paramref name="ghost"/> when present.
    /// </summary>
    public static bool TryExecuteOwnerAttachReapply(
        TNLConnection connection,
        Vehicle vehicle,
        GhostObject ghost)
    {
        if (connection == null || vehicle == null)
            return false;

        var packets = BuildOwnerAttachReapplyPackets(vehicle);
        if (packets.Count == 0)
            return false;

        foreach (var packet in packets)
            connection.SendGamePacket(packet);

        if (ghost != null)
        {
            ghost.SetMaskBits(GhostObject.HealthMask | GhostObject.HealthMaxMask);
            if (vehicle.WheelSet != null && vehicle.WheelSet.CBID > 0)
                ghost.SetMaskBits(GhostVehicle.WheelSetMask);
        }

        if (LogFilters.ForeignOwnerAttach)
        {
            Logger.WriteLog(LogType.Network,
                "ForeignOwnerAttachReapply coid={0} driverCoid={1} destroy veh+driver recreate vehicle→creature",
                vehicle.ObjectId.Coid, vehicle.Owner.ObjectId.Coid);
        }

        return true;
    }
}
