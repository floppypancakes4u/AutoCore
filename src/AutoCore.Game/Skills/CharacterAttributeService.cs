namespace AutoCore.Game.Skills;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Experience;

/// <summary>
/// Handles C2S <c>AttributeIncrement</c> (0x205A): spend one unspent attribute point.
/// Tech raises vehicle max HP and heat; Theory raises max power; Combat/Perception are live at hit time.
/// </summary>
public sealed class CharacterAttributeService
{
    public static CharacterAttributeService Instance { get; } = new();

    /// <summary>Test hook: when set, replaces DB persistence so unit tests need no CharContext.</summary>
    internal static Action<Character> PersistForTests { get; set; }

    public bool TryIncrement(Character character, uint mask, out string error)
    {
        error = null;
        if (character == null)
        {
            error = "No character.";
            return false;
        }

        var kind = CharacterAttributeMasks.FromMask(mask);
        if (kind == CharacterAttributeKind.None)
        {
            error = "Unknown attribute mask.";
            return false;
        }

        if (character.AttributePoints <= 0)
        {
            error = "No attribute points available.";
            return false;
        }

        switch (kind)
        {
            case CharacterAttributeKind.Combat:
                character.SetAttributeCombat((short)(character.AttributeCombat + 1));
                break;
            case CharacterAttributeKind.Theory:
                character.SetAttributeTheory((short)(character.AttributeTheory + 1));
                break;
            case CharacterAttributeKind.Tech:
                character.SetAttributeTech((short)(character.AttributeTech + 1));
                break;
            case CharacterAttributeKind.Perception:
                character.SetAttributePerception((short)(character.AttributePerception + 1));
                break;
        }

        character.SetAttributePoints((short)(character.AttributePoints - 1));
        Persist(character);

        if (kind == CharacterAttributeKind.Tech)
            ApplyTechCombatSideEffects(character);
        else if (kind == CharacterAttributeKind.Theory)
            ApplyTheoryCombatSideEffects(character);

        return true;
    }

    private static void ApplyTechCombatSideEffects(Character character)
    {
        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return;

        vehicle.RecalculateMaximumHitPoints(refillCurrent: false, triggerGhostUpdate: true);
        vehicle.RecalculateMaximumHeat(triggerGhostUpdate: true);
    }

    private static void ApplyTheoryCombatSideEffects(Character character)
    {
        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return;

        vehicle.RecalculateMaximumPower(startPowerAtFull: false, triggerGhostUpdate: true);
    }

    private static void Persist(Character character)
    {
        if (PersistForTests != null)
        {
            PersistForTests(character);
            return;
        }

        CharacterProgressPersistence.Instance.SaveProgress(character.ObjectId.Coid, character.ToProgressSnapshot());
    }
}
