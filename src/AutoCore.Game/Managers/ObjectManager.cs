namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Utils.Memory;

public class ObjectManager : Singleton<ObjectManager>
{
    private Dictionary<long, ClonedObjectBase> Objects { get; } = new();

    public bool Add(ClonedObjectBase obj)
    {
        if (!obj.ObjectId.Global)
            throw new Exception("Not sure how global/local TFID works, use only global!");

        if (Objects.ContainsKey(obj.ObjectId.Coid))
            return false;

        Objects.Add(obj.ObjectId.Coid, obj);
        return true;
    }

    public Character? GetCharacter(long coid)
    {
        if (Objects.TryGetValue(coid, out var obj) && obj is Character character)
            return character;

        return null;
    }

    public Vehicle? GetVehicle(long coid)
    {
        if (Objects.TryGetValue(coid, out var obj) && obj is Vehicle vehicle)
            return vehicle;

        return null;
    }
}
