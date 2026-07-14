using TNL.Utils;

namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

/// <summary>
/// Server->client authoritative Damage (opcode 0x2023).
///
/// VERIFIED wire format (TNL bitstream) from client Net_ParseDamagePacket_TNL @0x636f00
/// and apply path Net_HandleDamage_0x2023 @0x812a60 / CVOGDamage_RouteSectorDamage:
///   header:  u64 attacker coid (lo32 then hi32), 1 bool attacker-global, u16 hitCount
///   per hit: 1 bool CRIT-flag (hit+0x14 — drives client "Critical!" text; NOT a "primary" flag),
///            s16 damage (hit+0x10), u64 target coid (lo32 then hi32, hit+0x00),
///            1 bool target-global (hit+0x08), 9 trailing bool flags (unused; kept false)
///
/// Apply semantics: a NON-ZERO damage int reduces health
/// (RouteSectorDamage -> CVOGCharacter_ApplyDamageToTargets -> ApplyDamageDelta,
///  newHP = clamp(current - damage)). A zero damage int is the buff/debuff path, NOT damage.
/// The non-zero path also requires target+0x17c bit5 and an attacker that resolves to a
/// local sector, so Attacker must be the real shooter's vehicle TFID.
/// Note: ApplyDamageDelta does NOT emit death (0x2020); server must track HP + send 0x2020 to kill.
///
/// Sent via SendGamePacket(packet, skipOpcode: true) — opcode 0x2023 is carried by the TNL RPC,
/// the buffer is the bit-packed payload (same path as GroupReactionCall 0x206C).
/// </summary>
public class DamagePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.Damage;

    public TFID Attacker { get; set; } = new();
    public TFID Target { get; set; } = new();

    /// <summary>Signed 16-bit damage amount. Non-zero = health reduction.</summary>
    public short Damage { get; set; }

    /// <summary>Primary/direct-hit flag (hit+0x14), consumed by RouteSectorDamage.</summary>
    public bool Primary { get; set; } = true;

    /// <summary>Whether this hit was a critical. Carried server-side (logging/messages); see WriteHit re: the wire flag.</summary>
    public bool IsCrit { get; set; }

    /// <summary>
    /// Multi-hit payload (cone/spray fire). When non-empty, this list is serialized (one hit entry
    /// each) instead of the single Target/Damage/Primary above — the client walks hitCount entries,
    /// so cone weapons must report every target in ONE packet. Leave empty for a single hit.
    /// </summary>
    public List<(TFID Target, short Damage, bool Primary, bool IsCrit)> Hits { get; set; } = new();

    public override void Write(BinaryWriter writer)
    {
        var stream = new BitStream();

        // --- header ---
        WriteCoid(stream, Attacker.Coid);
        stream.WriteFlag(Attacker.Global);

        if (Hits.Count > 0)
        {
            stream.WriteInt((uint)Hits.Count, 16); // hitCount
            foreach (var (target, damage, primary, isCrit) in Hits)
                WriteHit(stream, target, damage, primary, isCrit);
        }
        else
        {
            stream.WriteInt(1u, 16); // hitCount
            WriteHit(stream, Target, Damage, Primary, IsCrit);
        }

        writer.Write(stream.GetBuffer(), 0, (int)stream.GetBytePosition());
    }

    private static void WriteHit(BitStream stream, TFID target, short damage, bool primary, bool isCrit)
    {
        // The FIRST per-hit flag is the CRIT flag — the old "primary" label was a misnomer. Pinned via
        // Ghidra 2026-07-09: sender CVOGWeapon::OnFire @0x56e000 stores crit (bVar5) at SVOGDamagePacket
        // +0x15; the inbound handler Process_EMSG_Sector_Damage @0x812a60 repacks each hit into the FX
        // packet with packet+0x15 <- hit+0x14 (uStack_43 = *puVar7), and hit+0x14 is the FIRST flag read by
        // unpackDamage @0x636f00. So the client shows "Critical!" iff this first flag is set. We were
        // writing it as primary=true on every hit -> every hit rendered as a crit (the v253 regression).
        // Write the real isCrit. (report-1.7's "trailing index 3" was wrong; trailing flags stay false.)
        stream.WriteFlag(isCrit);                        // hit+0x14 — CRIT flag (drives client "Critical!" text)
        stream.WriteInt((uint)(ushort)damage, 16);       // hit+0x10 s16 damage
        WriteCoid(stream, target.Coid);                  // hit+0x00 target coid (lo32, hi32)
        stream.WriteFlag(target.Global);                 // hit+0x08 target-global flag
        _ = primary;                                     // vestigial — there is no separate primary wire flag
        for (var i = 0; i < 9; i++)                       // 9 trailing flags — unused by the crit + apply paths
            stream.WriteFlag(false);
    }

    private static void WriteCoid(BitStream stream, long coid)
    {
        stream.WriteInt((uint)(coid & 0xFFFFFFFF), 32);
        stream.WriteInt((uint)((ulong)coid >> 32), 32);
    }
}
