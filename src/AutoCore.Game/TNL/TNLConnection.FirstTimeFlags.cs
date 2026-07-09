namespace AutoCore.Game.TNL;

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;

public partial class TNLConnection
{
    /// <summary>
    /// Client → server tips/hints flags (opcode 0x20B1).
    /// Wire + EF glue; persistence logic is in <see cref="FirstTimeFlagsAccountSync"/>.
    /// </summary>
    [ExcludeFromCodeCoverage] // CharContext EF glue — covered via FirstTimeFlagsAccountSync.TryProcessRequest
    private void HandleUpdateFirstTimeFlagsRequest(BinaryReader reader)
    {
        try
        {
            using var context = new CharContext();
            ProcessFirstTimeFlagsRequest(
                reader,
                id => context.Accounts.FirstOrDefault(a => a.Id == id),
                () => context.SaveChanges());
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "HandleUpdateFirstTimeFlagsRequest: Exception saving for account {0}: {1}",
                Account?.Id ?? 0, ex.Message);
        }
    }

    /// <summary>
    /// Testable C2S path: parse packet and apply via injectable load/save callbacks.
    /// </summary>
    internal bool ProcessFirstTimeFlagsRequest(
        BinaryReader reader,
        Func<uint, Account> loadAccountById,
        Action saveChanges)
    {
        var packet = new UpdateFirstTimeFlagsRequestPacket();
        packet.Read(reader);

        Logger.WriteLog(LogType.Debug,
            "UpdateFirstTimeFlagsRequest account={0}: {1:X8} {2:X8} {3:X8} {4:X8}",
            Account?.Id ?? 0,
            packet.FirstFlags1, packet.FirstFlags2, packet.FirstFlags3, packet.FirstFlags4);

        if (!FirstTimeFlagsAccountSync.TryProcessRequest(
                Account,
                packet,
                loadAccountById,
                saveChanges,
                out var error))
        {
            Logger.WriteLog(LogType.Error, "HandleUpdateFirstTimeFlagsRequest: {0}", error);
            return false;
        }

        Logger.WriteLog(LogType.Network,
            "Updated FirstTimeFlags for account {0}: {1:X8} {2:X8} {3:X8} {4:X8}",
            Account.Id, Account.FirstFlags1, Account.FirstFlags2, Account.FirstFlags3, Account.FirstFlags4);
        return true;
    }
}
