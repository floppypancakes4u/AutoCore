namespace AutoCore.Game.Skills;

/// <summary>
/// Predicts what the client will actually sweep on the hotbar for a cooldown the server just
/// started. Diagnostics only — never consulted for enforcement, so the server window stays the
/// authored one for every account.
///
/// The sweep is client-local: <c>RequestCastSkill</c> (0x941590) calls StartRecastTimer on the
/// click and <c>CVOGHBOKToCastAgain</c> (0x51E240) holds <c>CVOGSkillNode::m_bIsRecharging</c> for
/// ceil(lCoolDown * modifier) + iCastTime ms, capped at 500 ms whenever the caster's
/// <c>CVOGCharacter::m_lGMLevel</c> (+0x6B4) is >= 1. Without this line a playtest log records a
/// 12 s window while the player saw a sub-second flash and nothing explains the difference.
/// </summary>
internal static class SkillCooldownDiagnostics
{
    /// <summary>Retail GM cap from <c>CVOGHBOKToCastAgain</c> (0x51E240).</summary>
    internal const long GmClientSweepCapMs = 500;

    internal static long PredictClientSweepMs(long serverCooldownMs, int gmLevel)
    {
        if (serverCooldownMs <= 0)
            return 0;

        // A cap, not an assignment: the client keeps a cooldown already below it.
        return gmLevel >= 1
            ? Math.Min(serverCooldownMs, GmClientSweepCapMs)
            : serverCooldownMs;
    }
}
