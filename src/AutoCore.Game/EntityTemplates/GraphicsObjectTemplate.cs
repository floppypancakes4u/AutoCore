namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public class GraphicsObjectTemplate : ObjectTemplate
{
    public GraphicsObjectType ObjectType { get; }

    public string ToolTip { get; set; }
    public string FxCreateExtraName { get; set; }
    public bool DistantDraw { get; set; }
    public int DistanceDrawOverride { get; set; }
    public Vector4 Location { get; set; }
    public Quaternion Rotation { get; set; }

    public GraphicsObjectTemplate(GraphicsObjectType objectType)
    {
        ObjectType = objectType;
    }

    public override void Read(BinaryReader reader, int mapVersion)
    {
        GraphicsBase_UnserializeCreateEffect(reader, mapVersion);
        GraphicsBase_UnserializeTooltip(reader, mapVersion);
        ReadTriggerEvents(reader, mapVersion);

        Location = Vector4.ReadNew(reader);
        Rotation = Quaternion.Read(reader);
        Scale = reader.ReadSingle();
        TerrainOffset = reader.ReadSingle();
        IsActive = reader.ReadBoolean();
    }

    public override ClonedObjectBase Create()
    {
        var obj = new GraphicsObject(ObjectType);
        obj.SetCoid(COID, false);
        // EnterPoints and other map-logic props often have CBID 0. LoadCloneBase(0) throws;
        // SectorMap.InitializeLocalObjects also skips materialization when CBID <= 0.
        if (CBID > 0)
            obj.LoadCloneBase(CBID);
        obj.Faction = Faction;
        obj.Scale = Scale;
        obj.Layer = Layer;
        obj.Position = Location.ToVector3();
        obj.Rotation = Rotation;

        return obj;
    }

    private void GraphicsBase_UnserializeCreateEffect(BinaryReader reader, int mapVersion)
    {
        if (mapVersion >= 21)
        {
            FxCreateExtraName = reader.ReadLengthedString();

            if (mapVersion >= 48)
                DistantDraw = reader.ReadBoolean(); // not 100% sure

            if (mapVersion >= 62)
                DistanceDrawOverride = reader.ReadInt32(); // not 100% sure
        }
    }

    private void GraphicsBase_UnserializeTooltip(BinaryReader reader, int mapVersion)
    {
        if (mapVersion >= 22)
            ToolTip = reader.ReadLengthedString();
    }
}
