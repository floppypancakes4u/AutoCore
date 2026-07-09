namespace AutoCore.Game.Entities;

using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;

public partial class Character
{
    /// <summary>
    /// Fills CreateCharacterExtended FirstTimeFlags from the session account (packet offset 0x8EC).
    /// </summary>
    internal void WriteFirstTimeFlags(CreateCharacterExtendedPacket extendedCharPacket)
    {
        var account = OwningConnection?.Account;
        if (!FirstTimeFlagsAccountSync.TryCopyToPacketFlags(account, extendedCharPacket.FirstTimeFlags))
        {
            Logger.WriteLog(LogType.Error,
                "Character.WriteToPacket: OwningConnection/Account null; FirstTimeFlags sent as zeros");
            return;
        }

        Logger.WriteLog(LogType.Debug,
            "Character.WriteToPacket: FirstTimeFlags for account {0}: {1:X8} {2:X8} {3:X8} {4:X8}",
            account.Id,
            extendedCharPacket.FirstTimeFlags[0],
            extendedCharPacket.FirstTimeFlags[1],
            extendedCharPacket.FirstTimeFlags[2],
            extendedCharPacket.FirstTimeFlags[3]);
    }
}
