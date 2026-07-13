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
}
