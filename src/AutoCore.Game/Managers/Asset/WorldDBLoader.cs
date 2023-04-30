namespace AutoCore.Game.Managers.Asset;

using AutoCore.Database.World;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;

public class WorldDBLoader
{
    public IDictionary<Tuple<byte, byte>, ConfigNewCharacter> ConfigNewCharacters { get; set; }
    public IDictionary<Tuple<int, byte>, ContinentArea> ContinentAreas { get; set; }
    public IDictionary<int, ContinentObject> ContinentObjects { get; set; }
    public IDictionary<byte, ExperienceLevel> ExperienceLevels { get; set; }

    public bool Load()
    {
        using var worldContext = new WorldContext();

        ContinentObjects = worldContext.ContinentObjects.Where(ContinentObjectValidator).ToDictionary(co => co.Id);

        if (AssetManager.Instance.ServerType == ServerType.Global || AssetManager.Instance.ServerType == ServerType.Both)
        {
            ConfigNewCharacters = worldContext.ConfigNewCharacters.ToDictionary(cnc => Tuple.Create(cnc.Race, cnc.Class));
        }

        if (AssetManager.Instance.ServerType == ServerType.Sector || AssetManager.Instance.ServerType == ServerType.Both)
        {
            ContinentAreas = worldContext.ContinentAreas.ToDictionary(ca => Tuple.Create(ca.ContinentObjectId, ca.Area));
            ExperienceLevels = worldContext.ExperienceLevels.ToDictionary(el => el.Level);
        }

        return true;
    }

    private static bool ContinentObjectValidator(ContinentObject continentObject)
    {
        if (continentObject == null)
            return false;

        return AssetManager.Instance.HasFileInGLMs($"{continentObject.MapFileName}.fam");
    }
}
