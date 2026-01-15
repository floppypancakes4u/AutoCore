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
///   Offset 0x2C: Unknown_0x2C (8 bytes)
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
    public long Unknown_0x2C { get; set; } = 0;
    
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

    public override void Write(BinaryWriter writer)
    {
        writer.Write(UnknownHeader);
        writer.WriteTFID(CharacterId);
        writer.Write(Level);
        
        // Padding (7 bytes to align to 0x20)
        writer.BaseStream.Position += 7;
        
        // Currency/XP fields
        writer.Write(Currency);
        writer.Write(Experience);
        writer.Write(Unknown_0x2C);
        
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
