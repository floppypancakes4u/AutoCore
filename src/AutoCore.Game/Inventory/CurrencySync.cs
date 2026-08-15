namespace AutoCore.Game.Inventory;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;
using AutoCore.Utils.Logging;

/// <summary>
/// Server-authoritative currency (credits) and client sync helpers.
///
/// Client rules (retail):
///   - CreateCharacterExtended.Credits non-zero can crash — always clear on spawn.
///   - Absolute UI set: CharacterLevel (0x2017) Currency field (same as /currency / /credits).
///     CharacterLevel is a full snapshot (Level/Currency/Experience/points) — partial packets
///     zero other fields on the client (FUN_00810f00 apply ~0x00531fcb).
///   - Additive mid-session delta: GiveCredits (0x205E) via <see cref="AddCredits"/>.
/// </summary>
public static class CurrencySync
{
    /// <summary>Result of credits/currency chat commands.</summary>
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
    /// Build the CharacterLevel absolute packet used by <c>/credits</c>, <c>/currency</c>, and login restore.
    /// Uses the canonical full snapshot so the client apply path does not wipe XP/points/assigned attrs/HP.
    /// </summary>
    public static CharacterLevelPacket CreateAbsoluteCurrencyPacket(Character character, long absoluteCredits)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var packet = CharacterLevelManager.Instance.BuildPacket(character);
        packet.Currency = absoluteCredits;
        return packet;
    }

    /// <summary>
    /// Build CharacterLevel absolute restore when the character has a non-zero balance.
    /// When <paramref name="persistence"/> is provided, reloads the authoritative balance from
    /// storage first (same ledger /credits writes) so login never depends on a stale in-memory value.
    /// Returns null when there is nothing to restore (avoids a no-op packet).
    /// Login still always sends a full progress CharacterLevel via ExperienceService afterward.
    /// </summary>
    public static CharacterLevelPacket TryCreateLoginRestorePacket(
        Character character,
        IInventoryPersistence persistence = null)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        if (persistence != null)
        {
            var coid = ResolveCharacterCoid(character);
            if (coid > 0)
            {
                var loaded = persistence.LoadCredits(coid);
                character.SetCredits(loaded);
                Logger.WriteLog(
                    LogType.Network,
                    $"CurrencySync login reload: character={coid} credits={loaded}");
            }
        }

        if (character.Credits == 0L)
            return null;

        return CreateAbsoluteCurrencyPacket(character, character.Credits);
    }

    /// <summary>
    /// <c>/credits</c> (and <c>/currency</c> alias):
    /// <list type="bullet">
    /// <item><description>No args — report memory + DB balance and denomination split.</description></item>
    /// <item><description>Four args — set absolute Globes/Bars/Scrip/Clink, persist, full CharacterLevel.</description></item>
    /// </list>
    /// </summary>
    public static CommandResult TryApplyCreditsCommand(
        Character character,
        string[] parts,
        IInventoryPersistence persistence = null)
    {
        if (character == null)
        {
            return new CommandResult
            {
                Success = false,
                Message = "No character."
            };
        }

        if (parts == null || parts.Length == 0)
        {
            return new CommandResult
            {
                Success = false,
                Message = "Invalid credits command! Usage: /credits  OR  /credits <globes> <bars> <scrip> <clink>"
            };
        }

        if (parts.Length == 1)
            return QueryCredits(character, persistence);

        if (parts.Length < 5)
        {
            return new CommandResult
            {
                Success = false,
                Message = "Invalid credits command! Usage: /credits <globes> <bars> <scrip> <clink>"
            };
        }

        return SetCreditsFromDenominations(character, parts, persistence);
    }

    /// <summary>
    /// Parse <c>/currency &lt;globes&gt; &lt;bars&gt; &lt;scrip&gt; &lt;clink&gt;</c> (or bare <c>/currency</c> query).
    /// Delegates to <see cref="TryApplyCreditsCommand"/>.
    /// </summary>
    public static CommandResult TryApplyCurrencyCommand(Character character, string[] parts)
    {
        return TryApplyCreditsCommand(character, parts, persistence: null);
    }

    private static CommandResult QueryCredits(Character character, IInventoryPersistence persistence)
    {
        var mem = character.Credits;
        long db = mem;
        var coid = ResolveCharacterCoid(character);

        if (persistence != null && coid > 0)
        {
            try
            {
                db = persistence.LoadCredits(coid);
            }
            catch (Exception ex)
            {
                Logger.WriteException(LogType.Error, $"Failed to load credits", ex);
                return new CommandResult
                {
                    Success = false,
                    Message = $"Failed to load credits from DB: {ex.Message}"
                };
            }
        }
        else if (persistence == null)
        {
            Logger.WriteLog(LogType.Error, "QueryCredits: no inventory persistence bound; reporting memory only");
        }

        var (globes, bars, scrip, clink) = CharacterLevelPacket.SplitCurrency(db);
        return new CommandResult
        {
            Success = true,
            Absolute = db,
            Message =
                $"Credits mem={mem} db={db}  =>  {globes} Globes, {bars} Bars, {scrip} Scrip, {clink} Clink"
        };
    }

    private static CommandResult SetCreditsFromDenominations(
        Character character,
        string[] parts,
        IInventoryPersistence persistence)
    {
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

        try
        {
            var absolute = CharacterLevelPacket.BuildCurrency(globes, bars, scrip, clink);

            // Explicit store (ChatManager passes InventoryPersistence.Instance); else inventory-bound store.
            if (persistence != null)
                SetCreditsAbsolute(persistence, character, absolute, CurrencyChangeReason.AdminCommand);
            else
                character.Inventory.SetCreditsAbsolute(character, absolute, CurrencyChangeReason.AdminCommand);

            var packet = CreateAbsoluteCurrencyPacket(character, absolute);

            long db = character.Credits;
            var coid = ResolveCharacterCoid(character);
            if (persistence != null && coid > 0)
            {
                try
                {
                    db = persistence.LoadCredits(coid);
                }
                catch (Exception reEx)
                {
                    // Save already succeeded; surface in-memory balance if re-read fails.
                    Logger.WriteException(LogType.Error, "credits re-read after save", reEx);
                }
            }

            return new CommandResult
            {
                Success = true,
                Absolute = absolute,
                Packet = packet,
                Message =
                    $"Set credits to {globes} Globes, {bars} Bars, {scrip} Scrip, {clink} Clink! " +
                    $"(persisted={character.Credits} db={db})"
            };
        }
        catch (Exception ex)
        {
            Logger.WriteException(LogType.Error, $"Failed to set credits", ex);
            return new CommandResult
            {
                Success = false,
                Message = $"Failed to set credits: {ex.Message}"
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
        CurrencyChangeReason reason,
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
        AuditCurrencyChanged(character, reason, previous, applied, next);

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
        CurrencyChangeReason reason,
        bool allowDebt = false)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        if (!allowDebt && absoluteCredits < 0)
            absoluteCredits = 0;

        var previous = character.Credits;
        character.SetCredits(absoluteCredits);
        PersistCredits(persistence, character, absoluteCredits);
        AuditCurrencyChanged(character, reason, previous, absoluteCredits - previous, absoluteCredits);
        return absoluteCredits;
    }

    /// <summary>
    /// Phase 3 audit trail: one <c>CurrencyChanged</c> per successful money mutation.
    /// Invariant: Before + Delta == After.
    /// </summary>
    private static void AuditCurrencyChanged(
        Character character,
        CurrencyChangeReason reason,
        long before,
        long delta,
        long after)
    {
        GameLog.Audit(
            "CurrencyChanged",
            ("CharacterId", ResolveCharacterCoid(character)),
            ("Reason", reason),
            ("Before", before),
            ("Delta", delta),
            ("After", after));
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

        var coid = ResolveCharacterCoid(character);
        if (coid <= 0)
        {
            Logger.WriteLog(LogType.Error, $"PersistCredits: character coid is invalid ({coid}); balance not saved");
            return;
        }

        persistence.SaveCredits(coid, credits);
        Logger.WriteLog(LogType.Network, $"PersistCredits: character={coid} credits={credits}");
    }

    /// <summary>Positive character row key for the char DB, or 0 when unavailable.</summary>
    private static long ResolveCharacterCoid(Character character)
    {
        // TFID defaults Coid to -1; only positive server coids are valid character keys.
        return character?.ObjectId?.Coid > 0 ? character.ObjectId.Coid : 0;
    }
}
