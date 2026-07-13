namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// EMSG_Sector_SkillStatusEffect (0x2031).
///
/// Layout reverse-engineered from client packer <c>CVOGReaction_CastSkillOnTarget</c> (0x4D09A0)
/// and consumer <c>Client_RecvSkillStatusEffect</c> (0x811170):
///
///   +0x00 opcode 0x2031 (written by SendGamePacket)
///   +0x04 size (int16) = targetCount * 0x18 + 0x58   ← NOT 0x40 + n*0x18
///   +0x06 pad (int16)
///   +0x08 skillId (int32) from skill+0x5FC
///   +0x0C skillLevel (int16)
///   +0x0E pad (int16)
///   +0x10 applyPower (int32) — &lt; 1 → miss path; else skill-HB duration
///   +0x14 status (byte) — 0 success, 0x63 ('c') alternate, 0x11 cancel
///   +0x15 pad (3)
///   +0x18 position XYZ (3×float)
///   +0x24 pad (int32)
///   +0x28 source-owner TFID (4 dwords / 16 bytes). For a learned player skill this is the
///         character TFID, which the client matches to its local character and resolves to vehicle.
///   +0x38 flag (byte) — packer sets (param5 == 0)
///   +0x39 pad (3)
///   +0x3C diceSeed (int32, optional)
///   +0x40 targets[] each 0x18, then terminator TFID (-1,-1,0,0)
///
/// Target entry (0x18): 4 dwords TFID region + int16 power + int16 aux + pad
/// </summary>
public class SkillStatusEffectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.SkillStatusEffect;

    public int SkillId { get; set; }

    public short SkillLevel { get; set; } = 1;

    /// <summary>If &lt; 1, miss path. If ≥ 1, skill heartbeat duration.</summary>
    public int ApplyPower { get; set; } = 1000;

    /// <summary>0 = success. 0x63 ('c') = alternate cast. 0x11 = cancel.</summary>
    public byte Status { get; set; }

    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }

    public TFID Caster { get; set; } = new();

    /// <summary>Packer sets this when a cast-related param is zero.</summary>
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
