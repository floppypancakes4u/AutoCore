namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Utils.Memory;

public class TriggerManager : Singleton<TriggerManager>
{
    public void CheckTriggersFor(ClonedObjectBase clonedObject)
    {
        if (clonedObject is null)
            return;

        var map = clonedObject.Map;
        if (map is null)
            return;

        foreach (var trigger in map.Triggers)
            trigger.Value.TriggerIfPossible(clonedObject);
    }
}
