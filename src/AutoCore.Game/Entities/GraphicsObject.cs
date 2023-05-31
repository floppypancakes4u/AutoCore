using AutoCore.Game.TNL.Ghost;

namespace AutoCore.Game.Entities;

public enum GraphicsObjectType
{
    GraphicsPhysics,
    Graphics
}

public class GraphicsObject : ClonedObjectBase
{
    public GraphicsObjectType ObjectType { get; }

    public GraphicsObject(GraphicsObjectType objectType)
    {
        ObjectType = objectType;
    }

    public override int GetCurrentHP() => CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;
    public override int GetMaximumHP() => CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;
    public override int GetBareTeamFaction() => Faction;

    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

        Ghost = new GhostObject();
        Ghost.SetParent(this);
    }
}
