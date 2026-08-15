namespace AutoCore.Game.Skills;

/// <summary>Retail eSkillResponses values consumed by Client_RecvSkillStatusEffect.</summary>
public enum SkillResponse : byte
{
    Ok = 0,
    ServerChecksFailed = 1,
    GenericFailed = 2,
    Corpse = 3,
    Power = 4,
    Status = 5,
    Busy = 6,
    Recharge = 7,
    OutOfRange = 13,
    WrongTarget = 14,

    /// <summary>
    /// SKILL_RESPONSE_CANCELLED_ACTIVE. The only reject the client handles quietly: its
    /// SkillStatusEffect handler (0x811170) pops m_plistSkillsQueue and cancels an active skill of
    /// that id, without entering the "Aborting cooldown" branch that destroys the optimistic
    /// CVOGHBOKToCastAgain heartbeat and without printing a "Server says" chat line.
    /// </summary>
    CancelledActive = 17,
}
