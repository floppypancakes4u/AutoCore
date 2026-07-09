using TNL.Utils;

namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

/// <summary>
/// EMSG_Sector_Damage (0x2023) — bit-packed payload for retail floating combat numbers.
///
/// Client: unpack 0x636F00 → apply 0x812A60 → combat-event queue → FUN_0093FFB0 formats
/// and attaches CWndDamageText (rendered via list at game+0xAA8).
///
/// Event type is usually 0 (damage) on the direct recv path. Special text comes from entry
/// flags mapped in Client FUN_00812A60 → event bytes checked in FUN_0093FFB0:
///   IsCrit    → crit number styling
///   IsResist  → "Resist"  (requires amount != 0 so the event is queued)
///   IsDeflect → "Deflect" (requires amount != 0 so the event is queued)
///
/// Signed amounts: the wire value is int16. Positive = damage numbers. Negative amounts
/// are how client heal application (FUN_004d78e0) derives event type 1 ("+NHP") when the
/// packet is applied through that path — abs(amount) is displayed with an HP suffix.
///
/// Miss (event+0x2A) and type 2 ("+NPP") are NOT set by this packet's direct recv path
/// (miss is hardcoded 0; type is hardcoded 0). Miss/PP floaters come from local combat /
/// skill tick code (FUN_005538a0 / FUN_0058cc40).
///
/// Amount 0 without special handling: recv path often never queues a combat event
/// for vehicle sources, so nothing floats. Prefer |amount| ≥ 1 for damage/heal text.
///
/// Send with <c>skipOpcode: true</c> (RPC type carries 0x2023; buffer is pure BitStream).
/// <see cref="MaxDisplayAmount"/> / <see cref="MinDisplayAmount"/> are safe bounds —
/// INT16_MAX (32767) does not reliably produce a floater on the retail client.
/// </summary>
public class DamagePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.Damage;

    /// <summary>Safe positive amount for floaters (INT16_MAX is a client edge-case).</summary>
    public const int MaxDisplayAmount = short.MaxValue - 1; // 32766

    /// <summary>Safe negative amount for heal-style floaters (avoids INT16_MIN abs edge-case).</summary>
    public const int MinDisplayAmount = -(short.MaxValue - 1); // -32766

    /// <summary>Header TFID — attacker / damage source.</summary>
    public TFID Source { get; set; } = new();

    public List<DamageEntry> Entries { get; } = new();

    public void AddHit(TFID target, int amount, DamageEntryFlags flags = default)
    {
        Entries.Add(new DamageEntry
        {
            Target = target ?? new TFID(),
            Amount = (short)Math.Clamp(amount, MinDisplayAmount, MaxDisplayAmount),
            Flags = flags
        });
    }

    public override void Write(BinaryWriter writer)
    {
        var stream = new BitStream();

        stream.Write(Source?.Coid ?? -1L);
        stream.WriteFlag(Source?.Global ?? false);

        var count = Math.Clamp(Entries.Count, 0, ushort.MaxValue);
        stream.WriteInt((uint)count, 16);

        foreach (var entry in Entries.Take(count))
        {
            // EMSG_Sector_Damage_Unpack order (FUN_00636F00).
            stream.WriteFlag(entry.Flags.IsCrit);          // → event+0x29 crit styling
            stream.WriteInt((uint)(entry.Amount & 0xFFFF), 16);
            stream.Write(entry.Target?.Coid ?? -1L);
            stream.WriteFlag(entry.Target?.Global ?? false);

            stream.WriteFlag(entry.Flags.IsResist);        // → event+0x2B "Resist"
            stream.WriteFlag(entry.Flags.IsDeflect);       // → event+0x2C "Deflect"
            stream.WriteFlag(false);
            stream.WriteFlag(false);
            stream.WriteFlag(false);
            stream.WriteFlag(false);
            stream.WriteFlag(false);
            stream.WriteFlag(false);
            stream.WriteFlag(false);
        }

        writer.Write(stream.GetBuffer(), 0, (int)stream.GetBytePosition());
    }

    public class DamageEntry
    {
        public TFID Target { get; set; } = new();
        public short Amount { get; set; }
        public DamageEntryFlags Flags { get; set; }
    }

    public struct DamageEntryFlags
    {
        public bool IsCrit;
        public bool IsResist;
        public bool IsDeflect;

        public static DamageEntryFlags None => default;
        public static DamageEntryFlags Crit => new() { IsCrit = true };
        public static DamageEntryFlags Resist => new() { IsResist = true };
        public static DamageEntryFlags Deflect => new() { IsDeflect = true };
    }
}
