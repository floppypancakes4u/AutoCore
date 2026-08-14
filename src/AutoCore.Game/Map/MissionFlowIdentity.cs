namespace AutoCore.Game.Map;

using AutoCore.Game.Entities;

/// <summary>
/// Local 20-bit COIDs for the synthetic GiveMissionDialog trigger/reaction that
/// <c>CVOGSectorMap::CreateMissionFlow</c> @0x004d4040 attaches to mission-giver
/// creatures that have no authored FAM TriggerEvents.
/// <para>
/// <c>CVOGCreature::CreateFromPacket</c> @0x004c82b0 only calls CreateMissionFlow
/// when CreateCreature +0x128 (on-use trigger) is not −1. GhostCreature packs those
/// IDs in 20 bits, so they must stay below 1&lt;&lt;20 and above authored FAM COIDs
/// (thousands).
/// </para>
/// </summary>
public static class MissionFlowIdentity
{
    /// <summary>Base of the synthetic on-use range (fits 20-bit ghost pack).</summary>
    public const int CoidBase = 0x70000;

    /// <summary>Keys above this fall back to CBID so the packed id stays in 20 bits.</summary>
    public const int MaxKey = 0x7FFF;

    /// <summary>
    /// Deterministic local COID for the synthetic trigger (reaction=false) or
    /// GiveMissionDialog reaction (reaction=true) of one giver.
    /// </summary>
    public static int CoidFor(int key, bool reaction)
    {
        var k = key & MaxKey;
        return CoidBase + k * 2 + (reaction ? 1 : 0);
    }

    /// <summary>
    /// Assigns on-use trigger/reaction COIDs when this creature is a mission giver
    /// and they are still unset. Idempotent.
    /// </summary>
    public static bool TryEnsure(Creature creature)
    {
        if (creature == null || !creature.IsMissionGiver)
            return false;

        if (creature.OnUseTriggerCoid > 0 && creature.OnUseReactionCoid > 0)
            return true;

        var key = creature.SpawnOwner > 0 && creature.SpawnOwner <= MaxKey
            ? (int)creature.SpawnOwner
            : creature.CBID;
        if (key <= 0)
            return false;

        creature.OnUseTriggerCoid = CoidFor(key, reaction: false);
        creature.OnUseReactionCoid = CoidFor(key, reaction: true);
        return true;
    }
}
