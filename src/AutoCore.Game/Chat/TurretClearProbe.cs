namespace AutoCore.Game.Chat;

using AutoCore.Game.Entities;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

public enum TurretClearProbeMethod
{
    GhostAbsent = 0,
    GhostCbidMinusOne = 1,
    CreateVehicleEmptySlot = 2,
    CreateVehicleCbidMinusOne = 3,
    DestroyTurretObject = 4,
    GhostAbsentPlusDestroy = 5,
    InventoryUnequip = 6,
}

public static class TurretClearProbe
{
    private static readonly string[] MethodNames =
    {
        "ghost-absent",
        "ghost-cbid-minus-one",
        "create-vehicle-empty-slot",
        "create-vehicle-cbid-minus-one",
        "destroy-turret-object (CRASH-UNSAFE)",
        "ghost-absent-plus-destroy (CRASH-UNSAFE)",
        "inventory-unequip-0x203e",
    };

    private static readonly Dictionary<long, int> NextStepByCharacterCoid = new();

    public static int MethodCount => MethodNames.Length;

    public static string GetMethodName(int index) => MethodNames[index];

    public static bool TryExecute(
        Character character,
        string[] parts,
        out string message,
        out IReadOnlyList<BasePacket> packets)
    {
        packets = Array.Empty<BasePacket>();

        if (character?.CurrentVehicle == null)
        {
            message = "You are not in a vehicle!";
            return false;
        }

        if (character.OwningConnection == null)
        {
            message = "No active connection.";
            return false;
        }

        var vehicle = character.CurrentVehicle;
        var characterCoid = character.ObjectId.Coid;

        if (parts.Length >= 2 && string.Equals(parts[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            message = BuildListMessage(characterCoid);
            return true;
        }

        int methodIndex;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var explicitIndex))
        {
            if (explicitIndex < 0 || explicitIndex >= MethodNames.Length)
            {
                message = $"Invalid method index {explicitIndex}. Use /tc list (0..{MethodNames.Length - 1}).";
                return false;
            }

            methodIndex = explicitIndex;
            NextStepByCharacterCoid[characterCoid] = (explicitIndex + 1) % MethodNames.Length;
        }
        else
        {
            if (!NextStepByCharacterCoid.TryGetValue(characterCoid, out var nextIndex))
                nextIndex = 0;

            methodIndex = nextIndex % MethodNames.Length;
            NextStepByCharacterCoid[characterCoid] = (methodIndex + 1) % MethodNames.Length;
        }

        var previousTurretId = vehicle.ClearTurretForProbe();
        var destroyTarget = ResolveDestroyTarget(previousTurretId, vehicle.LastProbeTurretTfid);
        packets = ApplyMethod(character, vehicle, (TurretClearProbeMethod)methodIndex, destroyTarget);
        message = $"step {methodIndex}/{MethodNames.Length - 1}: {MethodNames[methodIndex]}";
        if (IsDestroyMethod((TurretClearProbeMethod)methodIndex) && destroyTarget is not { Coid: > 0 })
            message += " (no turret TFID to destroy)";
        if ((TurretClearProbeMethod)methodIndex == TurretClearProbeMethod.InventoryUnequip && destroyTarget is not { Coid: > 0 })
            message += " (no turret TFID for unequip)";
        return true;
    }

    public static void ConfigureCreateVehicleTurretSlot(CreateVehicleExtendedPacket packet, TurretClearProbeMethod method)
    {
        switch (method)
        {
            case TurretClearProbeMethod.CreateVehicleEmptySlot:
                packet.CreateWeapons[1] = null;
                break;

            case TurretClearProbeMethod.CreateVehicleCbidMinusOne:
                packet.CreateWeapons[1] = CreateStubTurretWeaponPacket();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, "Not a create-vehicle probe method.");
        }
    }

    private static CreateWeaponPacket CreateStubTurretWeaponPacket()
    {
        return new CreateWeaponPacket
        {
            CBID = -1,
            ObjectId = new TFID(-1, true),
            MinimumDamage = DamageSpecific.CreateEmpty(),
            MaximumDamage = DamageSpecific.CreateEmpty(),
            Name = string.Empty,
        };
    }

    private static TFID? ResolveDestroyTarget(TFID? previousTurretId, TFID? lastProbeTurretTfid)
    {
        if (previousTurretId is { Coid: > 0 })
            return previousTurretId;

        if (lastProbeTurretTfid is { Coid: > 0 })
            return lastProbeTurretTfid;

        return null;
    }

    private static bool IsDestroyMethod(TurretClearProbeMethod method) =>
        method is TurretClearProbeMethod.DestroyTurretObject or TurretClearProbeMethod.GhostAbsentPlusDestroy;

    private static string BuildListMessage(long characterCoid)
    {
        var lines = new List<string> { $"Turret clear probe methods ({MethodNames.Length}):" };
        var nextIndex = NextStepByCharacterCoid.GetValueOrDefault(characterCoid, 0) % MethodNames.Length;
        lines.Add($"Next /tc will run index {nextIndex}: {MethodNames[nextIndex]}");

        for (var i = 0; i < MethodNames.Length; i++)
            lines.Add($"  {i}: {MethodNames[i]}");

        return string.Join('\n', lines);
    }

    private static IReadOnlyList<BasePacket> ApplyMethod(
        Character character,
        Vehicle vehicle,
        TurretClearProbeMethod method,
        TFID? previousTurretId)
    {
        switch (method)
        {
            case TurretClearProbeMethod.GhostAbsent:
                ApplyGhostAbsent(character, vehicle);
                return Array.Empty<BasePacket>();

            case TurretClearProbeMethod.GhostCbidMinusOne:
                ApplyGhostCbidMinusOne(character, vehicle);
                return Array.Empty<BasePacket>();

            case TurretClearProbeMethod.CreateVehicleEmptySlot:
                return new BasePacket[] { BuildCreateVehiclePacket(vehicle, TurretClearProbeMethod.CreateVehicleEmptySlot) };

            case TurretClearProbeMethod.CreateVehicleCbidMinusOne:
                return new BasePacket[] { BuildCreateVehiclePacket(vehicle, TurretClearProbeMethod.CreateVehicleCbidMinusOne) };

            case TurretClearProbeMethod.DestroyTurretObject:
                return BuildDestroyPackets(previousTurretId);

            case TurretClearProbeMethod.GhostAbsentPlusDestroy:
                ApplyGhostAbsent(character, vehicle);
                return BuildDestroyPackets(previousTurretId);

            case TurretClearProbeMethod.InventoryUnequip:
                ApplyGhostAbsent(character, vehicle);
                return BuildInventoryUnequipPackets(vehicle, previousTurretId);

            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, null);
        }
    }

    private static void ApplyGhostAbsent(Character character, Vehicle vehicle)
    {
        EnsureVehicleGhostDelivery(character, vehicle);
        vehicle.Ghost?.SetMaskBits(GhostVehicle.TurretWeaponMask);
    }

    private static void ApplyGhostCbidMinusOne(Character character, Vehicle vehicle)
    {
        EnsureVehicleGhostDelivery(character, vehicle);
        vehicle.ForceTurretGhostClearCbidMinusOne = true;
        vehicle.Ghost?.SetMaskBits(GhostVehicle.TurretWeaponMask);
    }

    /// <summary>
    /// SetMaskBits only reaches clients that already have a GhostInfo ref on the NetObject.
    /// If ObjectLocalScopeAlways ran before Scoping was ready, or the vehicle ghost was
    /// never attached, dirty bits are silently dropped. Re-scope before marking dirty.
    /// </summary>
    private static void EnsureVehicleGhostDelivery(Character character, Vehicle vehicle)
    {
        if (vehicle.Ghost == null)
            vehicle.CreateGhost();

        if (character.OwningConnection is not TNLConnection connection)
            return;

        // Prefer re-attaching when no GhostInfo ref exists. ObjectLocalScopeAlways
        // requires Scoping (set in ActivateGhosting); IsGhosting becomes true after
        // the client handshake. Mid-session /tc always has both.
        if (vehicle.Ghost.GetFirstObjectRef() == null)
            connection.ObjectLocalScopeAlways(vehicle.Ghost);
        else
            connection.ObjectInScope(vehicle.Ghost);
    }

    private static CreateVehicleExtendedPacket BuildCreateVehiclePacket(Vehicle vehicle, TurretClearProbeMethod method)
    {
        var packet = new CreateVehicleExtendedPacket();
        vehicle.WriteToPacket(packet);
        ConfigureCreateVehicleTurretSlot(packet, method);
        return packet;
    }

    private static IReadOnlyList<BasePacket> BuildDestroyPackets(TFID? destroyTarget)
    {
        if (destroyTarget is not { Coid: > 0 })
            return Array.Empty<BasePacket>();

        return new BasePacket[] { new DestroyObjectPacket(destroyTarget) };
    }

    private static IReadOnlyList<BasePacket> BuildInventoryUnequipPackets(
        Vehicle vehicle,
        TFID? turretTfid)
    {
        if (turretTfid is not { Coid: > 0 })
            return Array.Empty<BasePacket>();

        // Matches VehicleNet_PostCorrectionEvent's synthesized 0x203E and
        // SMSG_Sector_InventoryUnequip: fidItem, fidVehicleSentFrom, x=0, y=0, type=HARDPOINT(2).
        // PostCorrectionEvent copies the ghost parent object's TFID into fidVehicleSentFrom
        // (vehicle), not the character TFID.
        return new BasePacket[]
        {
            new InventoryUnequipPacket
            {
                ItemId = new TFID(turretTfid.Coid, turretTfid.Global),
                VehicleId = new TFID(vehicle.ObjectId.Coid, vehicle.ObjectId.Global),
                InventoryPositionX = 0,
                InventoryPositionY = 0,
                InventoryType = 2,
            }
        };
    }
}
