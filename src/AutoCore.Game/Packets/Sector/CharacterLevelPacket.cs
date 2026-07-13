namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// CharacterLevel (0x2017) - Updates character level and related stats.
/// 
/// Currency Format:
///   A single long value split into 4 display groups (like decimal comma groupings):
///   - Clink  = value % 1000 (last 3 digits)
///   - Scrip  = (value / 1000) % 1000 (next 3 digits)
///   - Bars   = (value / 1000000) % 1000 (next 3 digits)
///   - Globes = value / 1000000000 (remaining digits)
///   Example: 123,999,888,777,666 = 123999 Globes, 888 Bars, 777 Scrip, 666 Clink
/// 
/// Field Layout:
///   Offset 0x04: UnknownHeader (4 bytes)
///   Offset 0x08: TFID CharacterId (16 bytes)
///   Offset 0x18: Level (1 byte)
///   Offset 0x19: Padding (7 bytes)
///   Offset 0x20: Currency (8 bytes) - Globes/Bars/Scrip/Clink
///   Offset 0x28: Experience (4 bytes)
///   Offset 0x2C: Health (4 bytes)
///   Offset 0x30: HealthMaximum (4 bytes)
///   Offset 0x34: CurrentMana (2 bytes)
///   Offset 0x36: MaxMana (2 bytes)
///   Offset 0x38: AttributeTech (2 bytes)
///   Offset 0x3A: AttributeCombat (2 bytes)
///   Offset 0x3C: AttributeTheory (2 bytes)
///   Offset 0x3E: AttributePerception (2 bytes)
///   Offset 0x40: AttributePoints (2 bytes)
///   Offset 0x42: SkillPoints (2 bytes)
///   Offset 0x44: Unknown7 (2 bytes)
///   Offset 0x46: ResearchPoints (2 bytes)
/// </summary>
public class CharacterLevelPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.CharacterLevel;

    public int UnknownHeader { get; set; } = 0;
    public TFID CharacterId { get; set; }
    public byte Level { get; set; }

    // Currency: single long split as Globes,Bars,Scrip,Clink (groups of 1000)
    public long Currency { get; set; } = 0;
    public int Experience { get; set; } = 0;
    public int Health { get; set; }
    public int HealthMaximum { get; set; }
    
    // Attributes and stats
    public short CurrentMana { get; set; } = 100;
    public short MaxMana { get; set; } = 100;
    public short AttributeTech { get; set; } = 0;
    public short AttributeCombat { get; set; } = 0;
    public short AttributeTheory { get; set; } = 0;
    public short AttributePerception { get; set; } = 0;
    public short AttributePoints { get; set; } = 0;
    public short SkillPoints { get; set; } = 0;
    public short Unknown7 { get; set; } = 0;
    public short ResearchPoints { get; set; } = 0;

    /// <summary>
    /// Helper to build Currency value from individual denominations.
    /// </summary>
    public static long BuildCurrency(long globes, int bars, int scrip, int clink)
    {
        return (globes * 1_000_000_000L) + (bars * 1_000_000L) + (scrip * 1_000L) + clink;
    }

    /// <summary>
    /// Split absolute credits into Globes / Bars / Scrip / Clink (base-1000 groups).
    /// Negative balances are treated as zero for display.
    /// </summary>
    public static (long Globes, int Bars, int Scrip, int Clink) SplitCurrency(long absolute)
    {
        if (absolute < 0)
            absolute = 0;

        var clink = (int)(absolute % 1000L);
        var scrip = (int)((absolute / 1000L) % 1000L);
        var bars = (int)((absolute / 1_000_000L) % 1000L);
        var globes = absolute / 1_000_000_000L;
        return (globes, bars, scrip, clink);
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(UnknownHeader);
        writer.WriteTFID(CharacterId);
        writer.Write(Level);

        // Padding (7 bytes) to put Currency at absolute packet offset 0x20 (with opcode).
        // Use WriteZeros so gaps are defined (Position+= leaves MemoryStream gaps fragile).
        writer.WriteZeros(7);

        // Currency/XP fields — client apply FUN_00531fcb: Level@0x18, Currency@0x20, Experience@0x28
        writer.Write(Currency);
        writer.Write(Experience);
        writer.Write(Health);
        writer.Write(HealthMaximum);

        // Short fields
        writer.Write(CurrentMana);
        writer.Write(MaxMana);
        writer.Write(AttributeTech);
        writer.Write(AttributeCombat);
        writer.Write(AttributeTheory);
        writer.Write(AttributePerception);
        writer.Write(AttributePoints);
        writer.Write(SkillPoints);
        writer.Write(Unknown7);
        writer.Write(ResearchPoints);
    }
}
