namespace AutoCore.Game.Constants;

/// <summary>
/// tCreatureAI.AICode → HBAI subclass selector (client factory CVOGHBAI_CreateByAICode, NPC.md §10.2).
/// Unknown codes fall back to <see cref="Default"/>.
/// </summary>
public enum HBAICode
{
    Default = 0,
    Character = 1,
    Creature = 2,
    Bot = 3,
    Mine = 4,
    Driver = 5,
    WalkingCreatureTurreted = 6,
}

/// <summary>
/// Ghost AI state byte sent under the creature/vehicle StateMask (NPC.md §10.3).
/// </summary>
public enum HBAICombatState : byte
{
    IdlePatrol = 0,
    Engage = 1,
    Combat = 2,
}
