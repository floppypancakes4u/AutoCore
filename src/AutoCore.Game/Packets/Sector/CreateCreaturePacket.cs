namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;

/// <summary>
/// S→C CreateCreature (0x2013). Client <c>Client_PacketDispatch</c> → <c>FUN_0080af70</c>.
/// Nested ghost owner form uses the same offsets (FUN_005f5ad0 creature nest).
/// <c>CoidCurrentVehicle</c> at client +0xF8 drives <c>Creature::SetVehicle</c> in
/// <c>CVOGCreature_PostCreateFromPacket</c> when the vehicle already exists.
/// </summary>
public class CreateCreaturePacket : CreateSimpleObjectPacket
{
    public override GameOpcode Opcode => GameOpcode.CreateCreature;

    /// <summary>Client offset of EnhancementId (includes root opcode).</summary>
    public const int ClientEnhancementIdOffset = 0xD8;

    /// <summary>Client offset of DoesntCountAsSummon bool (includes root opcode).</summary>
    public const int ClientDoesntCountAsSummonOffset = 0xF0;

    /// <summary>Client offset of vehicle COID for SetVehicle (includes root opcode).</summary>
    public const int ClientVehicleCoidOffset = 0xF8;

    /// <summary>Client offset of Level as uint32 (includes root opcode).</summary>
    public const int ClientLevelOffset = 0x114;

    /// <summary>Client offset of spawn-owner COID (includes root opcode). Unset = −1.</summary>
    public const int ClientSpawnOwnerOffset = 0x108;

    /// <summary>Client offset of skill-heartbeat count byte (includes root opcode).</summary>
    public const int ClientSkillCountOffset = 0x10C;

    /// <summary>Client offset of AI state byte copied by FUN_004c82b0.</summary>
    public const int ClientAiStateOffset = 0x127;

    /// <summary>Client offset of on-use trigger COID. Unset = −1.</summary>
    public const int ClientOnUseTriggerOffset = 0x128;

    /// <summary>Client offset of on-use reaction COID. Unset = −1.</summary>
    public const int ClientOnUseReactionOffset = 0x12C;

    /// <summary>
    /// Fixed size including root opcode. <c>FUN_004c82b0</c> unconditionally reads
    /// +0x128 as i32, so the game packet must cover through +0x12C.
    /// Ghost nest buffer is larger (0x930) and includes the skill table at +0x138.
    /// </summary>
    public const int ClientCreateCreatureSize = 0x130;

    /// <summary>Body size without root opcode (Write() payload only).</summary>
    public const int BodySize = ClientCreateCreatureSize - 4;

    public int EnhancementId { get; set; } = -1;
    public long CoidCurrentVehicle { get; set; } = -1;
    public byte Level { get; set; } = 1;
    public bool DoesntCountAsSummon { get; set; }
    public bool IsElite { get; set; }

    public override void Write(BinaryWriter writer)
    {
        var bodyStart = writer.BaseStream.Position;

        // CreateSimpleObject body is 212 bytes → client +0xD8 with root opcode.
        base.Write(writer);

        writer.Write(EnhancementId); // client +0xD8
        writer.Write(-1); // +0xDC
        writer.Write(-1); // +0xE0
        writer.Write(-1); // +0xE4
        writer.Write(-1); // +0xE8
        writer.Write(-1); // +0xEC
        writer.Write(DoesntCountAsSummon); // +0xF0
        writer.WriteZeros(7); // +0xF1..0xF7
        writer.Write(CoidCurrentVehicle); // +0xF8
        writer.Write(-1); // +0x100  FUN_005d2520 default
        writer.Write(-1); // +0x104  FUN_005d2520 default
        writer.Write(-1); // +0x108  spawn owner; FUN_004c82b0 treats 0 as a COID
        writer.Write((byte)0); // +0x10C skill count
        writer.WriteZeros(0x114 - 0x10D); // +0x10D..0x113
        writer.Write((int)Level); // +0x114
        writer.Write(IsElite); // +0x118
        writer.WriteZeros(0x127 - 0x119); // +0x119..0x126
        writer.Write((byte)0); // +0x127 AI state
        writer.Write(-1); // +0x128 on-use trigger
        writer.Write(-1); // +0x12C on-use reaction

        var written = (int)(writer.BaseStream.Position - bodyStart);
        var pad = PadBytesNeeded(written, BodySize);
        if (pad > 0)
            writer.WriteZeros(pad);
    }

    /// <summary>How many zero bytes to append so body length equals <paramref name="bodySize"/>.</summary>
    internal static int PadBytesNeeded(int written, int bodySize)
    {
        if (written > bodySize)
            throw new InvalidOperationException(
                $"CreateCreature body wrote {written} bytes; expected {bodySize}.");
        return bodySize - written;
    }
}
