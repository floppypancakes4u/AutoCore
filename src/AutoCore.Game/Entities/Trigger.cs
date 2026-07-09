namespace AutoCore.Game.Entities;

using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Structures;

public enum TriggerTargetType
{
    Players = 0,
    Creatures = 1,
    Vehicles = 2,
    MapEnemies = 3,
    List = 4,
    SummonTemplate = 5,
    SummonCBID = 6
}

public class Trigger : GraphicsObject
{
    public TriggerTemplate Template { get; set; }
    public long LeftObject { get; set; }
    public long RightObject { get; set; }

    /// <summary>Times this instance has fired; compared to ActivationCount (-1 unlimited).</summary>
    public int FireCount { get; set; }

    public Trigger(TriggerTemplate template)
        : base(GraphicsObjectType.GraphicsPhysics)
    {
        Template = template;
    }

    public bool CanTrigger(ClonedObjectBase activator)
    {
        if (activator is null || activator.Map is null)
            return false;

        if (activator is Character && Template.TargetType != TriggerTargetType.Players)
            return false;

        if (activator is Vehicle vehicle)
        {
            if (vehicle.GetSuperCharacter(false) != null)
            {
                // Player vehicles may activate Players or Vehicles target types.
                if (Template.TargetType != TriggerTargetType.Players
                    && Template.TargetType != TriggerTargetType.Vehicles)
                {
                    return false;
                }
            }
            else if (Template.TargetType != TriggerTargetType.Vehicles)
            {
                return false;
            }
        }

        if (activator is Creature && activator is not Character && Template.TargetType != TriggerTargetType.Creatures)
            return false;

        if (activator.Position.DistSq(Position) > Scale * Scale)
            return false;

        if (!ConditionsPass(activator))
            return false;

        return true;
    }

    /// <summary>
    /// Conditions only (no range/target). Used for remote logic triggers on variable/mission change.
    /// </summary>
    public bool ConditionsPass(ClonedObjectBase activator)
    {
        if (Template.Conditions.Count == 0)
            return true;

        foreach (var condition in Template.Conditions)
        {
            var conditionSatisfied = condition.Check(activator);
            if (conditionSatisfied && !Template.AllConditionsNeeded)
                return true;

            if (!conditionSatisfied && Template.AllConditionsNeeded)
                return false;
        }

        // AllConditionsNeeded: every condition satisfied.
        // Any-condition (AllConditionsNeeded=false): none were true above.
        return Template.AllConditionsNeeded;
    }

    public bool TriggerIfPossible(ClonedObjectBase clonedObject)
    {
        if (!CanTrigger(clonedObject))
            return false;

        clonedObject.Map.TriggerReactions(clonedObject, Template.Reactions);

        return true;
    }
}

public enum ConditionalType
{
    LessThan = 0,
    GreaterThan = 1,
    LessThanOrEqualTo = 2,
    GreaterThanOrEqualTo = 3,
    EqualTo = 4,
    NotEqualTo = 5
}

public enum ConditionalObjectSourceType
{
    Object = 0,
    Activator = 1
}

public class TriggerConditional
{
    public int LeftId { get; set; }
    public int RightId { get; set; }
    public ConditionalType Type { get; set; }

    public static TriggerConditional Read(BinaryReader reader)
    {
        var result = new TriggerConditional
        {
            LeftId = reader.ReadInt32(),
            RightId = reader.ReadInt32(),
            Type = (ConditionalType)reader.ReadByte()
        };

        reader.BaseStream.Position += 3;

        return result;
    }

    public bool Check(ClonedObjectBase activator)
    {
        // Client FUN_00579160 / CVOGVariable_EvaluateComputed: var[LeftId] OP var[RightId].
        var character = activator?.GetAsCharacter() ?? activator?.GetSuperCharacter(false);
        var store = character?.EnsureLogicVariables();
        if (store == null)
            return false;

        var leftValue = store.Get(LeftId);
        var rightValue = store.Get(RightId);

        return Type switch
        {
            ConditionalType.LessThan => leftValue < rightValue,
            ConditionalType.GreaterThan => leftValue > rightValue,
            ConditionalType.LessThanOrEqualTo => leftValue <= rightValue,
            ConditionalType.GreaterThanOrEqualTo => leftValue >= rightValue,
            ConditionalType.EqualTo => leftValue == rightValue,
            ConditionalType.NotEqualTo => leftValue != rightValue,
            _ => false,
        };
    }
}
