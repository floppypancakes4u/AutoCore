namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// EMSG_Sector_SkillStatusEffect (0x2031).
///
/// Layout confirmed against the client PDB type <c>SMSG_Sector_SkillStatusEffect</c> (size 2464,
/// <c>arrTargets</c> is a fixed <c>sSkillTargetInfo[100]</c>) and its consumer
/// <c>Process_EMSG_Sector_SkillStatusEffect</c> (0x811170):
///
///   +0x00 opcode 0x2031 (written by SendGamePacket; the PDB's leading padding dword)
///   +0x04 uiSize (uint16) — Skill_GenericCast derives the target count as (uiSize - 0x40) / 0x18,
///         so the count must include the terminator slot: 0x40 + (n + 1) * 0x18
///   +0x08 lSkillID (int32)
///   +0x0C iSkillLevel (int16) — compared against m_iSkillBoost + m_iSkillLevel; a mismatch makes
///         the client call SetSkillLevel and recompute currentAttributes
///   +0x10 lDelayTime (int32) — our ApplyPower. &lt; 1 fires the effect immediately; &gt;= 1 creates a
///         wakeup heartbeat and defers the fire by that many ms
///   +0x14 ucErrorCode (byte) — 0 success, 0x63 ('c') alternate, 0x11 quiet cancel
///   +0x18 nduavTargetPosition (3×float, 12 bytes, padded to +0x28)
///   +0x28 fidSource (TFID, 16 bytes). For a learned player skill this is the character TFID, which
///         the client matches to its local character and resolves to the vehicle.
///   +0x38 bIsItemSkill (bool) — our Flag. True selects the item-skill path (GetSkillBaseCopy plus
///         AddRechargeGroup) instead of the learned-skill path; must be 0 for learned skills.
///   +0x3C lDiceSeed (int32)
///   +0x40 arrTargets[] — sSkillTargetInfo, 0x18 each, then a terminator TFID (-1,-1,0,0)
///
/// sSkillTargetInfo (0x18): TFID fid (16) + int16 lMana + int16 lMaxMana + pad.
///
/// This message carries no cooldown: the hotbar sweep is entirely client-local. RequestCastSkill
/// (0x941590) calls StartRecastTimer on the click, and CVOGHBOKToCastAgain (0x51E240) sets
/// CVOGSkillNode::m_bIsRecharging for ceil(lCoolDown * modifier) + iCastTime ms, which is what
/// CBtnQuickBar::OnUpdateCooldownsNow (0x827AB0) draws. The server can only shorten that window by
/// sending an aborting ucErrorCode.
/// </summary>
public class SkillStatusEffectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.SkillStatusEffect;

    public int SkillId { get; set; }

    public short SkillLevel { get; set; } = 1;

    /// <summary>
    /// Retail <c>lDelayTime</c>. If &lt; 1 the client fires the effect immediately; if ≥ 1 it defers
    /// the fire behind a wakeup heartbeat for that many milliseconds.
    /// </summary>
    public int ApplyPower { get; set; } = 1000;

    /// <summary>Retail <c>ucErrorCode</c>. 0 = success, 0x63 ('c') = alternate cast, 0x11 = cancel.</summary>
    public byte Status { get; set; }

    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }

    public TFID Caster { get; set; } = new();

    /// <summary>Retail <c>bIsItemSkill</c>. Must be 0 for a learned skill.</summary>
    public byte Flag { get; set; } = 1;

    public int DiceSeed { get; set; }

    public List<SkillStatusTarget> Targets { get; } = new();

    public void AddTarget(TFID target, short mana = 0, short maxMana = 0)
    {
        Targets.Add(new SkillStatusTarget
        {
            Target = target ?? new TFID(),
            Mana = mana,
            MaxMana = maxMana
        });
    }

    public override void Write(BinaryWriter writer)
    {
        // Body starts at message +0x04 (opcode already written).
        var targetCount = Math.Clamp(Targets.Count, 0, 32);

        // CVOGReaction_CastSkillOnTarget: size = count * 0x18 + 0x58
        var size = (short)(targetCount * 0x18 + 0x58);
        writer.Write(size);
        writer.Write((short)0);

        writer.Write(SkillId);
        writer.Write(SkillLevel);
        writer.Write((short)0);

        writer.Write(Math.Max(0, ApplyPower));
        writer.Write(Status);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        writer.Write(PosX);
        writer.Write(PosY);
        writer.Write(PosZ);
        writer.Write(0);

        writer.WriteTFID(Caster ?? new TFID());

        writer.Write(Flag);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        writer.Write(DiceSeed);

        foreach (var t in Targets.Take(targetCount))
            WriteTarget(writer, t.Target, t.Mana, t.MaxMana);

        // Terminator: 4 dwords of invalid TFID (DAT_009CBF68 = -1,-1,0,0).
        // Packer only writes 0x10 bytes here; remaining 8 of the 0x18 slot stay zero.
        writer.Write(-1);
        writer.Write(-1);
        writer.Write(0);
        writer.Write(0);
        // The terminator occupies a full 0x18-byte target slot. Its TFID is 0x10 bytes;
        // the final two shorts and pad remain zero in the retail fixed-size structure.
        writer.WriteZeros(8);
    }

    private static void WriteTarget(BinaryWriter writer, TFID tfid, short mana, short maxMana)
    {
        // 0x18 bytes matching packer: 4 dwords TFID + 2 shorts + pad.
        writer.WriteTFID(tfid ?? new TFID());
        writer.Write(mana);
        writer.Write(maxMana);
        writer.Write(0);
    }

    public sealed class SkillStatusTarget
    {
        public TFID Target { get; set; } = new();
        public short Mana { get; set; }
        public short MaxMana { get; set; }
    }
}
