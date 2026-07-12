namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Structures;
using AutoCore.Utils;
using AutoCore.Utils.Extensions;

public class ObjectTemplate
{
    public byte Layer { get; set; }
    public int CBID { get; set; }
    public int COID { get; set; }
    public int Faction { get; set; }
    public long[] TriggerEvents { get; set; }

    /// <summary>
    /// Runtime / create-time active flag. Must not be treated as fam-authored for map load —
    /// Create/Activate used to set this true on the shared <see cref="MapData"/> template and
    /// permanently broke inactive spawns (combat Gunny visible before mission).
    /// Prefer <see cref="OriginalIsActive"/> for load-time decisions.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Fam-authored IsActive captured at map load. Map load and map reset always use this;
    /// reaction Create/Activate must not mutate it.
    /// </summary>
    public bool OriginalIsActive { get; set; }

    public float Scale { get; set; }
    public float TerrainOffset { get; set; }

    public virtual void Read(BinaryReader reader, int mapVersion)
    {
    }

    public virtual ClonedObjectBase Create()
    {
        return null; // TODO
    }

    protected virtual void ReadTriggerEvents(BinaryReader reader, int mapVersion)
    {
        TriggerEvents = reader.ReadConstArray(3, reader.ReadInt64);
    }

    protected static long ReadCOIDFromFile(BinaryReader reader)
    {
        return reader.ReadInt32();
    }

    protected static TFID ReadFIDFromFile(BinaryReader reader)
    {
        return new TFID
        {
            Global = reader.ReadBoolean(),
            Coid = reader.ReadInt32()
        };
    }

    public static ObjectTemplate AllocateTemplateFromCBID(int cbid)
    {
        var cloneBaseObj = AssetManager.Instance.GetCloneBase(cbid);
        if (cloneBaseObj == null)
            return null;

        switch (cloneBaseObj.Type)
        {
            case CloneBaseObjectType.Reaction:
                return new ReactionTemplate();

            case CloneBaseObjectType.Trigger:
                return new TriggerTemplate();

            case CloneBaseObjectType.SpawnPoint:
                return new SpawnPointTemplate();

            case CloneBaseObjectType.Store:
                return new StoreTemplate();

            case CloneBaseObjectType.MapPath:
                return new MapPathTemplate();

            case CloneBaseObjectType.EnterPoint:
                return new EnterPointTemplate();

            case CloneBaseObjectType.Outpost:
                return new OutpostTemplate();

            case CloneBaseObjectType.Object:
                return new GraphicsObjectTemplate(GraphicsObjectType.Graphics);

            case CloneBaseObjectType.ObjectGraphicsPhysics:
                return new GraphicsObjectTemplate(GraphicsObjectType.GraphicsPhysics);

            case CloneBaseObjectType.QuestObject:
                return new QuestObjectTemplate();

            default:
                Logger.WriteLog(LogType.Error, $"Unhandled template type: {cloneBaseObj.Type}!");
                return null;
        }
    }
}
