namespace AutoCore.Game.Managers;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Account-scoped FirstTimeFlags load/save used by CreateCharacterExtended and 0x20B1 handler.
/// Extracted for unit testing without TNL/DB infrastructure.
/// </summary>
public static class FirstTimeFlagsAccountSync
{
    /// <summary>
    /// Populate extended-packet flags from session account. Returns false if account missing.
    /// </summary>
    public static bool TryCopyToPacketFlags(Account account, uint[] destination)
    {
        if (!FirstTimeFlags.IsValidFlagsArray(destination))
            return false;

        if (account == null)
        {
            FirstTimeFlags.CopyToBuffer(destination, 0, 0, 0, 0);
            return false;
        }

        FirstTimeFlags.CopyToBuffer(destination,
            account.FirstFlags1, account.FirstFlags2, account.FirstFlags3, account.FirstFlags4);
        return true;
    }

    /// <summary>
    /// Full-replace flags on DB entity and optional in-memory session account.
    /// Returns false if <paramref name="dbAccount"/> is null.
    /// </summary>
    public static bool TryApplyUpdate(Account dbAccount, Account sessionAccount,
        uint f1, uint f2, uint f3, uint f4)
    {
        if (dbAccount == null)
            return false;

        dbAccount.FirstFlags1 = f1;
        dbAccount.FirstFlags2 = f2;
        dbAccount.FirstFlags3 = f3;
        dbAccount.FirstFlags4 = f4;

        if (sessionAccount != null)
        {
            sessionAccount.FirstFlags1 = f1;
            sessionAccount.FirstFlags2 = f2;
            sessionAccount.FirstFlags3 = f3;
            sessionAccount.FirstFlags4 = f4;
        }

        return true;
    }

    /// <summary>
    /// Full C2S update path without EF: load → apply → save. Returns false with error reason.
    /// </summary>
    public static bool TryProcessRequest(
        Account sessionAccount,
        UpdateFirstTimeFlagsRequestPacket packet,
        Func<uint, Account> loadAccountById,
        Action saveChanges,
        out string error)
    {
        error = null;

        if (sessionAccount == null)
        {
            error = "Account is null";
            return false;
        }

        if (packet == null)
        {
            error = "Packet is null";
            return false;
        }

        if (loadAccountById == null || saveChanges == null)
        {
            error = "Persistence callbacks are null";
            return false;
        }

        var dbAccount = loadAccountById(sessionAccount.Id);
        if (!TryApplyUpdate(dbAccount, sessionAccount,
                packet.FirstFlags1, packet.FirstFlags2, packet.FirstFlags3, packet.FirstFlags4))
        {
            error = $"Account {sessionAccount.Id} not found in database";
            return false;
        }

        saveChanges();
        return true;
    }
}
