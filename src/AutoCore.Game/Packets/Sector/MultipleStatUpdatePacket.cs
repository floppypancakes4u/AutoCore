namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// MultipleStatUpdate (0x2010) — absolute combat-pool writes on the retail client.
///
/// Dispatch: <c>Client_PacketDispatch</c> case <c>0x2010</c> → <c>FUN_0080BC40</c> →
/// <c>FUN_0080B3A0</c> per object.
///
/// Per-object apply (<c>FUN_0080B3A0</c>):
/// <list type="bullet">
///   <item>0 Heat → vehicle+0x150 (absolute)</item>
///   <item>1 Shield → <c>Vehicle_SetCurrentShield</c> (vehicle+0x144, clamped to max)</item>
///   <item>2 HP → vtbl+0x240 absolute current HP</item>
///   <item>3 Power → owner vtbl+0xAC absolute current power</item>
/// </list>
///
/// Wire (body after opcode):
/// <code>
///   uint16 objectCount
///   for each object:
///     TFID (16 bytes: int64 coid + bool global + 7 pad)
///     uint8 numStats
///     for each stat: 12 bytes { uint8 type, 7 zero pad, float value }
/// </code>
///
/// Ghost masks still replicate heat/shield/power to other clients; this packet is the
/// owner-facing absolute path analogous to CharacterLevel for HP/power.
/// StatUpdate (0x20AA) is a related single-object path into the same apply helper.
/// </summary>
public class MultipleStatUpdatePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.MultipleStatUpdate;

    /// <summary>Retail apply type ids in FUN_0080B3A0.</summary>
    public enum StatType : byte
    {
        Heat = 0,
        Shield = 1,
        Health = 2,
        Power = 3,
    }

    public sealed class StatEntry
    {
        public StatType Type { get; set; }
        public float Value { get; set; }

        public StatEntry()
        {
        }

        public StatEntry(StatType type, float value)
        {
            Type = type;
            Value = value;
        }

        public StatEntry(StatType type, int value)
            : this(type, (float)value)
        {
        }
    }

    public sealed class ObjectStats
    {
        public TFID ObjectId { get; set; } = new();
        public List<StatEntry> Stats { get; } = new();

        public ObjectStats()
        {
        }

        public ObjectStats(TFID objectId, params StatEntry[] stats)
        {
            ObjectId = objectId ?? new TFID();
            if (stats != null)
                Stats.AddRange(stats);
        }
    }

    public List<ObjectStats> Objects { get; } = new();

    public static MultipleStatUpdatePacket ForVehicleShield(TFID vehicleId, int currentShield)
    {
        var packet = new MultipleStatUpdatePacket();
        packet.Objects.Add(new ObjectStats(
            vehicleId,
            new StatEntry(StatType.Shield, currentShield)));
        return packet;
    }

    public static MultipleStatUpdatePacket ForVehicleShieldAndMax(
        TFID vehicleId,
        int currentShield,
        int maxShield)
    {
        // Max is not a StatUpdate type — only current is written via case 1.
        // Include heat-less shield-only current; max still rides ghost ShieldMaxMask.
        return ForVehicleShield(vehicleId, currentShield);
    }

    public override void Write(BinaryWriter writer)
    {
        var count = Math.Clamp(Objects.Count, 0, ushort.MaxValue);
        writer.Write((ushort)count);

        foreach (var obj in Objects.Take(count))
        {
            writer.WriteTFID(obj.ObjectId ?? new TFID());

            var stats = obj.Stats ?? new List<StatEntry>();
            var n = Math.Clamp(stats.Count, 0, byte.MaxValue);
            writer.Write((byte)n);

            for (var i = 0; i < n; i++)
            {
                var s = stats[i] ?? new StatEntry();
                writer.Write((byte)s.Type);
                writer.WriteZeros(7);
                writer.Write(s.Value);
            }
        }
    }
}
