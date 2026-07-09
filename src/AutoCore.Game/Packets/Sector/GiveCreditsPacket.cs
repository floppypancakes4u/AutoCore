namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// EMSG_Sector_GiveCredits (0x205E). Client handler FUN_0080CAC0 calls FUN_005355A0 which
/// <b>adds</b> the amount to the local character money at char+0x720 (not a visual-only probe).
/// May also queue a type-4 combat-event floater as a side effect.
///
/// Use via <c>InventoryManager.AddCredits</c> / <c>CurrencySync.AddCredits</c> so the
/// server DB stays authoritative. On login restore absolute balance with CharacterLevel
/// (0x2017) — never write non-zero CreateCharacterExtended.Credits.
///
/// Wire (opcode written by SendGamePacket at +0x00):
///   +0x04 reserved/pad (int32)
///   +0x08 amount (int64) — signed delta applied by the client
/// </summary>
public class GiveCreditsPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.GiveCredits;

    public long Amount { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(0); // pad so amount lands at message +0x08
        writer.Write(Amount);
    }
}
