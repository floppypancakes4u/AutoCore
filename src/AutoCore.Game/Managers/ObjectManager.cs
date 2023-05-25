namespace AutoCore.Game.Managers;

using AutoCore.Database.Char;
using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
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

    public Character GetOrLoadCharacter(long coid, CharContext context)
    {
        var character = GetCharacter(coid);
        if (character != null)
            return character;

        context ??= new CharContext();

        character = new Character();
        if (!character.LoadFromDB(context, coid))
            return null;

        if (!character.LoadCurrentVehicle(context))
            return null;

        Add(character);
        Add(character.CurrentVehicle);

        return character;
    }

    public static Character LoadCharacterForSelection(long coid, CharContext context)
    {
        var character = new Character();
        if (!character.LoadFromDB(context, coid, true))
            return null;

        if (!character.LoadCurrentVehicle(context, true))
            return null;

        return character;
    }

    public ClonedObjectBase? GetObject(TFID fid) => GetObject(fid.Coid, fid.Global);

    public ClonedObjectBase? GetObject(long coid, bool global)
    {
        if (Objects.TryGetValue(coid, out var obj))
            return obj;

        return null;
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
