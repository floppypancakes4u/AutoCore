namespace AutoCore.Game.Inventory;

using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;

/// <summary>
/// Server-authoritative currency (credits) and client sync helpers.
///
/// Client rules (retail):
///   - CreateCharacterExtended.Credits non-zero can crash — always clear on spawn.
///   - Absolute UI set: CharacterLevel (0x2017) Currency field (same as /currency).
///   - Additive mid-session delta: GiveCredits (0x205E) via <see cref="AddCredits"/>.
/// </summary>
public static class CurrencySync
{
    /// <summary>Result of <see cref="TryApplyCurrencyCommand"/>.</summary>
    public sealed class CommandResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public long Absolute { get; init; }
        public CharacterLevelPacket Packet { get; init; }
    }

    /// <summary>
    /// Force CreateCharacterExtended money fields to zero (login-safe).
    /// Live balance is restored after spawn via <see cref="TryCreateLoginRestorePacket"/>.
    /// </summary>
    public static void ClearCreateCharacterCredits(CreateCharacterExtendedPacket packet)
    {
        if (packet == null)
            throw new ArgumentNullException(nameof(packet));

        packet.Credits = 0;
        packet.CreditDebt = 0;
    }

    /// <summary>
    /// Build CharacterLevel absolute restore when the character has a non-zero balance.
    /// Returns null when there is nothing to restore (avoids a no-op packet).
    /// </summary>
    public static CharacterLevelPacket TryCreateLoginRestorePacket(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        if (character.Credits == 0L)
            return null;

        return new CharacterLevelPacket
        {
            CharacterId = character.ObjectId,
            Level = character.Level,
            Currency = character.Credits
        };
    }

    /// <summary>
    /// Parse <c>/currency &lt;globes&gt; &lt;bars&gt; &lt;scrip&gt; &lt;clink&gt;</c>, persist absolute
    /// balance, and build the CharacterLevel packet that updates the client UI.
    /// </summary>
    public static CommandResult TryApplyCurrencyCommand(Character character, string[] parts)
    {
        if (parts == null || parts.Length < 5)
        {
            return new CommandResult
            {
                Success = false,
                Message = "Invalid currency command! Usage: /currency <globes> <bars> <scrip> <clink>"
            };
        }

        if (!long.TryParse(parts[1], out var globes)
            || !int.TryParse(parts[2], out var bars)
            || !int.TryParse(parts[3], out var scrip)
            || !int.TryParse(parts[4], out var clink))
        {
            return new CommandResult
            {
                Success = false,
                Message = "Invalid currency values! All values must be numbers."
            };
        }

        if (character == null)
        {
            return new CommandResult
            {
                Success = false,
                Message = "No character."
            };
        }

        try
        {
            var absolute = CharacterLevelPacket.BuildCurrency(globes, bars, scrip, clink);
            character.Inventory.SetCreditsAbsolute(character, absolute);
            var packet = new CharacterLevelPacket
            {
                CharacterId = character.ObjectId,
                Level = character.Level,
                Currency = absolute
            };

            return new CommandResult
            {
                Success = true,
                Absolute = absolute,
                Packet = packet,
                Message =
                    $"Set currency to {globes} Globes, {bars} Bars, {scrip} Scrip, {clink} Clink! (persisted={character.Credits})"
            };
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"Failed to set currency: {ex.Message}");
            return new CommandResult
            {
                Success = false,
                Message = $"Failed to set currency: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Apply a signed credit delta, persist absolute balance, and build a client
    /// <see cref="GiveCreditsPacket"/> for the applied delta (0x205E is additive on the client).
    /// Negative deltas floor at zero unless <paramref name="allowDebt"/> is true.
    /// </summary>
    public static AddCreditsResult AddCredits(
        IInventoryPersistence persistence,
        Character character,
        long amount,
        bool allowDebt = false)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var previous = character.Credits;
        long next;
        if (allowDebt)
        {
            next = previous + amount;
        }
        else if (amount >= 0)
        {
            next = previous + amount;
        }
        else
        {
            next = previous + amount;
            if (next < 0)
                next = 0;
        }

        var applied = next - previous;
        character.SetCredits(next);
        PersistCredits(persistence, character, next);

        return new AddCreditsResult(
            previous,
            next,
            applied,
            applied != 0 ? new GiveCreditsPacket { Amount = applied } : null);
    }

    /// <summary>Set absolute credits, persist, and return the absolute value.</summary>
    public static long SetCreditsAbsolute(
        IInventoryPersistence persistence,
        Character character,
        long absoluteCredits,
        bool allowDebt = false)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        if (!allowDebt && absoluteCredits < 0)
            absoluteCredits = 0;

        character.SetCredits(absoluteCredits);
        PersistCredits(persistence, character, absoluteCredits);
        return absoluteCredits;
    }

    public static void PersistCredits(
        IInventoryPersistence persistence,
        Character character,
        long credits)
    {
        if (persistence == null)
        {
            Logger.WriteLog(LogType.Error, "PersistCredits: no inventory persistence bound; balance not saved");
            return;
        }

        // TFID defaults Coid to -1; only positive server coids are valid character keys.
        var coid = character?.ObjectId?.Coid ?? 0;
        if (coid <= 0)
        {
            Logger.WriteLog(LogType.Error, $"PersistCredits: character coid is invalid ({coid}); balance not saved");
            return;
        }

        persistence.SaveCredits(coid, credits);
        Logger.WriteLog(LogType.Debug, $"PersistCredits: character={coid} credits={credits}");
    }
}
