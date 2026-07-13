namespace AutoCore.Game.Structures;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;

/// <summary>
/// Per-character runtime map logic variables (client CVOGVariable_EvaluateComputed @ 0x005afd40).
/// Map defines variables in <see cref="MapData.Variables"/>; Type selects evaluation:
///   0  = plain flag/constant (mutable via VariableSet; seeded from InitialValue)
///   7  = player vehicle health percent (current/max, clamped 0..1) — e.g. SCAB pad full-heal gates
///   9  = has completed mission Id in <see cref="Variable.Value"/>
///   10 = has completed objective Id in Value (FUN_0052c9d0: mission done or advanced past seq)
///   11 = has active mission Id in Value (char mission hash / CurrentQuests)
///   12 = has active objective Id in Value
/// Conditions compare var[LeftId] OP var[RightId].
/// </summary>
public class LogicVariableStore
{
    public const byte TypeConstant = 0;
    public const byte TypePlayerHealthPercent = 7;
    public const byte TypeHasCompletedMission = 9;
    public const byte TypeHasCompletedObjective = 10;
    public const byte TypeHasActiveMission = 11;
    public const byte TypeHasActiveObjective = 12;

    private const float True = 1.0f;
    private const float False = 0.0f;

    public SectorMap Map { get; }

    private readonly Character _character;
    private readonly Dictionary<int, float> _mutable = new();

    public LogicVariableStore(SectorMap map, Character character)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        _character = character ?? throw new ArgumentNullException(nameof(character));

        foreach (var kvp in map.MapData.Variables)
            _mutable[kvp.Key] = kvp.Value.InitialValue;
    }

    public float Get(int id)
    {
        if (!Map.MapData.Variables.TryGetValue(id, out var def))
            return False;

        switch (def.Type)
        {
            case TypeHasCompletedMission:
                return _character.CompletedMissionIds.Contains((int)def.Value) ? True : False;

            case TypeHasCompletedObjective:
                // Client FUN_0052c9d0 @ 0x0052c9d0 — not a mutable seed.
                return HasCompletedObjective((int)def.Value) ? True : False;

            case TypeHasActiveMission:
                return _character.CurrentQuests.Any(q => q.MissionId == (int)def.Value) ? True : False;

            case TypeHasActiveObjective:
                return HasActiveObjective((int)def.Value) ? True : False;

            case TypePlayerHealthPercent:
                return GetPlayerHealthPercent();

            case TypeConstant:
            default:
                return _mutable.TryGetValue(id, out var value) ? value : def.InitialValue;
        }
    }

    /// <summary>
    /// Vehicle current/max HP as 0..1. Full HP returns exactly 1 so conditions like
    /// health_percent == const_1 work with float equality.
    /// </summary>
    private float GetPlayerHealthPercent()
    {
        var target = (ClonedObjectBase)_character.CurrentVehicle ?? _character;
        var max = target.GetMaximumHP();
        if (max <= 0)
            return False;

        var current = target.GetCurrentHP();
        if (current >= max)
            return True;
        if (current <= 0)
            return False;

        return Math.Clamp(current / (float)max, 0f, 1f);
    }

    public void Set(int id, float value) => _mutable[id] = value;

    private bool HasActiveObjective(int objectiveId)
    {
        foreach (var quest in _character.CurrentQuests)
        {
            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission != null
                && mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective)
                && objective.ObjectiveId == objectiveId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Client <c>FUN_0052c9d0</c>: objective is "completed" if its parent mission is completed,
    /// or the mission is active and the character's active objective sequence is strictly greater
    /// than the target objective's sequence (player has advanced past it).
    /// </summary>
    private bool HasCompletedObjective(int objectiveId)
    {
        var mission = AssetManager.Instance.GetMissionByObjectiveId(objectiveId);
        if (mission == null)
            return false;

        if (_character.CompletedMissionIds.Contains(mission.Id))
            return true;

        // Prefer GetObjectiveById so sequence comes from the same asset resolution as the mission.
        var target = AssetManager.Instance.GetObjectiveById(objectiveId);
        if (target == null)
            return false;

        foreach (var quest in _character.CurrentQuests)
        {
            if (quest.MissionId != mission.Id)
                continue;

            // Client: target.sequence < activeObjective.sequence → completed past this step.
            if (target.Sequence < quest.ActiveObjectiveSequence)
                return true;
        }

        return false;
    }
}
