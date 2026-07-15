using System.Globalization;
using System.Text.Json;

namespace ChromiumOverlay;

/// <summary>
/// Player vehicle combat pools mirrored from the retail client (read-only).
/// Offsets match AutoCore.DevTool VehicleCombatReader / offsets/bak.json / vehicle_combat_pool RE.
/// </summary>
public readonly record struct PlayerCombatPools(
    int Hp,
    int MaxHp,
    int Power,
    int MaxPower,
    int Shield,
    int MaxShield,
    bool HasVehicle)
{
    public const int VogClientBaseRva = 0x91A840;
    public const int LocalPlayerOffset = 0x0E98;
    public const int PlayerVehicleOffset = 0x250;

    public const int CurrentShieldOffset = 0x144;
    public const int MaxShieldOffset = 0x148;
    public const int CurrentPowerOffset = 0x12C; // int16 on creature base
    public const int MaxPowerOffset = 0x12E;     // int16
    // HP is accessed via vfuncs (+0x248 / +0x240) on the client; bridge may fill these
    // when a safe in-process read is available. -1 means unknown.
    public const int Unknown = -1;

    public static PlayerCombatPools Empty { get; } = new(Unknown, Unknown, 0, 0, 0, 0, false);

    public string ToJson(int tick = 0, int pid = 0)
    {
        // Compact JSON for the MMF channel (bridge writes the same shape).
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"pid\":{pid},\"tick\":{tick},\"hasVehicle\":{(HasVehicle ? "true" : "false")},\"hp\":{Hp},\"maxHp\":{MaxHp},\"power\":{Power},\"maxPower\":{MaxPower},\"shield\":{Shield},\"maxShield\":{MaxShield}}}");
    }

    public static bool TryParseJson(string json, out PlayerCombatPools pools)
    {
        pools = Empty;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            pools = new PlayerCombatPools(
                ReadInt(r, "hp", Unknown),
                ReadInt(r, "maxHp", Unknown),
                ReadInt(r, "power", 0),
                ReadInt(r, "maxPower", 0),
                ReadInt(r, "shield", 0),
                ReadInt(r, "maxShield", 0),
                r.TryGetProperty("hasVehicle", out var hv) && hv.ValueKind == JsonValueKind.True);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ReadInt(JsonElement r, string name, int fallback)
    {
        if (!r.TryGetProperty(name, out var p))
            return fallback;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
            return n;
        return fallback;
    }
}
