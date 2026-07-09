namespace AutoCore.Game.Combat;

using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Pure combat-floater probe commands (no skills, no inventory).
///
/// Client combat-event types (FUN_0093FFB0) used for visualizations:
///   0 — damage number / Resist / Deflect (from Damage 0x2023 + flags)
///   3 — XP (GiveXP 0x205F)
///
/// GiveCredits (0x205E) is a real currency delta on the client (FUN_005355A0), not a
/// combat-text probe — client currency UI is set via /currency (CharacterLevel 0x2017).
///
/// Damage recv (FUN_00812A60) hardcodes event type=0 and miss flag=0, so Miss/HP/PP
/// cannot be forced through Damage the same way hit/crit/resist/deflect can.
/// </summary>
public static class CombatTextCommand
{
    public sealed class Result
    {
        public string Message { get; init; } = "";
        public IReadOnlyList<PacketSend> Packets { get; init; } = Array.Empty<PacketSend>();
    }

    public sealed class PacketSend
    {
        public BasePacket Packet { get; init; }
        public bool SkipOpcode { get; init; }

        public PacketSend(BasePacket packet, bool skipOpcode = false)
        {
            Packet = packet;
            SkipOpcode = skipOpcode;
        }
    }

    public sealed class Context
    {
        public bool HasVehicle { get; init; }
        public TFID Source { get; init; } = new();
        public TFID Target { get; init; } = new();
        public bool HasExplicitTarget { get; init; }
        public string TargetTypeName { get; init; } = "Vehicle";
    }

    public static Result Execute(string[] parts, Context ctx)
    {
        if (ctx == null || !ctx.HasVehicle)
        {
            return new Result
            {
                Message = "Enter a vehicle first. Prefer a target. Then: /ct dmg 50 | /ct crit | /ct resist | /ct deflect | /ct xp 500"
            };
        }

        var sourceTf = ctx.Source ?? new TFID();
        var targetTf = ctx.Target ?? sourceTf;
        var attachDesc = ctx.HasExplicitTarget
            ? $"target {ctx.TargetTypeName}#{targetTf.Coid}"
            : $"self vehicle #{targetTf.Coid}";

        var arg = parts != null && parts.Length >= 2 ? parts[1].ToLowerInvariant() : "help";

        try
        {
            switch (arg)
            {
                case "help":
                case "?":
                    return Msg(
                        "Combat visualizations + XP: " +
                        $"/ct dmg [n] (1..{DamagePacket.MaxDisplayAmount}) | /ct crit [n] | /ct resist | /ct deflect | /ct xp [n]. " +
                        "Credits: use /currency (not combat text).");

                case "go":
                case "test":
                    return WithDamage(sourceTf, targetTf, 42, default,
                        $"Sent Damage 42 on {attachDesc}.");

                case "dmg":
                case "damage":
                case "hit":
                {
                    var amount = 50;
                    if (parts != null && parts.Length >= 3 && int.TryParse(parts[2], out var parsed))
                        amount = parsed;
                    var clamped = Math.Clamp(amount, 1, DamagePacket.MaxDisplayAmount);
                    var text = amount != clamped
                        ? $"Sent Damage amount={clamped} on {attachDesc} (requested {amount}, clamped to max {DamagePacket.MaxDisplayAmount})."
                        : $"Sent Damage amount={clamped} on {attachDesc}.";
                    return WithDamage(sourceTf, targetTf, clamped, default, text);
                }

                case "crit":
                {
                    var amount = 75;
                    if (parts != null && parts.Length >= 3 && int.TryParse(parts[2], out var parsed))
                        amount = Math.Clamp(parsed, 1, DamagePacket.MaxDisplayAmount);
                    return WithDamage(sourceTf, targetTf, amount, DamagePacket.DamageEntryFlags.Crit,
                        $"Sent Crit amount={amount} on {attachDesc}.");
                }

                case "resist":
                    return WithDamage(sourceTf, targetTf, 1, DamagePacket.DamageEntryFlags.Resist,
                        $"Sent Resist (amount 1) on {attachDesc}. Expect text \"Resist\".");

                case "deflect":
                    return WithDamage(sourceTf, targetTf, 1, DamagePacket.DamageEntryFlags.Deflect,
                        $"Sent Deflect (amount 1) on {attachDesc}. Expect text \"Deflect\".");

                case "miss":
                    // Damage recv hardcodes miss flag (event+0x2A) to 0. No pure combat-text
                    // "Miss" packet exists — Miss is set only by local hit-fail (FUN_005538A0).
                    return Msg(
                        "Miss combat text: no pure combat-text opcode. Damage 0x2023 hardcodes miss=0 " +
                        "(FUN_00812A60). Retail \"Miss\" is queued by local combat miss (FUN_005538A0), not a server float packet. " +
                        "No packet sent.");

                case "hp":
                case "heal":
                    // Type-1 "+NHP" is set when local heal apply queues amount&lt;0 as type 1
                    // (FUN_004d78e0). Negative Damage stays type 0 (shows as normal dmg number).
                    return Msg(
                        "HP combat text (+NHP): no pure combat-text opcode. Event type 1 is set only by " +
                        "local heal application (FUN_004d78e0), not Damage/GiveXP-style packets. " +
                        "Negative Damage was tested live and shows as normal damage. No packet sent.");

                case "pp":
                case "power":
                    // Type-2 "+NPP" is set only by local power apply (FUN_0058cc40).
                    return Msg(
                        "PP combat text (+NPP): no pure combat-text opcode. Event type 2 is set only by " +
                        "local power application (FUN_0058cc40). No packet sent.");

                case "xp":
                case "givexp":
                {
                    var amount = 500;
                    if (parts != null && parts.Length >= 3 && int.TryParse(parts[2], out var parsed))
                        amount = parsed;
                    return new Result
                    {
                        Message = $"Sent GiveXP 0x205F amount={amount} (type-3 combat floater).",
                        Packets = new[]
                        {
                            new PacketSend(new GiveXPPacket { Amount = amount, LevelHint = -1 })
                        }
                    };
                }

                case "credits":
                case "givecredits":
                    return Msg(
                        "Credits are not a combat-text visualization. Use /currency <globes> <bars> <scrip> <clink> " +
                        "(CharacterLevel absolute set + server persist). No packet sent.");

                default:
                    return Msg($"Unknown '{arg}'. Try /ct dmg 50 | /ct crit | /ct resist | /ct deflect | /ct xp 500");
            }
        }
        catch (Exception ex)
        {
            return Msg($"combattext failed: {ex.Message}");
        }
    }

    public static DamagePacket BuildDamagePacket(TFID source, TFID target, int amount, DamagePacket.DamageEntryFlags flags = default)
    {
        var packet = new DamagePacket { Source = source ?? new TFID() };
        packet.AddHit(target ?? new TFID(), amount, flags);
        return packet;
    }

    private static Result Msg(string message) => new() { Message = message };

    private static Result WithDamage(TFID source, TFID target, int amount, DamagePacket.DamageEntryFlags flags, string message)
    {
        return new Result
        {
            Message = message,
            Packets = new[]
            {
                new PacketSend(BuildDamagePacket(source, target, amount, flags), skipOpcode: true)
            }
        };
    }
}
