namespace AutoCore.Game.Structures;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;

/// <summary>
/// Per-character runtime map logic variables (client CVOGVariable_EvaluateComputed @ 0x005afd40).
/// Map defines variables in <see cref="MapData.Variables"/>; Type selects evaluation:
///   0  = plain flag/constant (mutable via VariableSet; seeded from InitialValue)
///   9  = has completed mission Id in <see cref="Variable.Value"/>
///   11 = has active mission Id in Value (char mission hash / CurrentQuests)
///   12 = has active objective Id in Value
/// Conditions compare var[LeftId] OP var[RightId].
/// </summary>
public class LogicVariableStore
{
    public const byte TypeConstant = 0;
    public const byte TypeHasCompletedMission = 9;
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

            case TypeHasActiveMission:
                return _character.CurrentQuests.Any(q => q.MissionId == (int)def.Value) ? True : False;

            case TypeHasActiveObjective:
                return HasActiveObjective((int)def.Value) ? True : False;

            case TypeConstant:
            default:
                return _mutable.TryGetValue(id, out var value) ? value : def.InitialValue;
        }
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
}
