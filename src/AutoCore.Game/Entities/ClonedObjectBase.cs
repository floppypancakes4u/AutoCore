namespace AutoCore.Game.Entities;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

public abstract class ClonedObjectBase
{
    public CloneBaseObject CloneBaseObject { get; private set; }
    public CloneBaseObjectType Type => CloneBaseObject.Type;
    public int CBID => CloneBaseObject.CloneBaseSpecific.CloneBaseId;
    
    public int Faction { get; protected set; }
    public int CustomValue { get; protected set; }
    public Vector3 Position { get; protected set; }
    public Quaternion Rotation { get; protected set; }
    public float Scale { get; protected set; }
    public TFID ObjectId { get; protected set; }

    public ClonedObjectBase()
    {
        Faction = -1;
        Scale = 1.0f;
        CustomValue = -1;
        ObjectId = new TFID
        {
            Coid = -1L,
            Global = false
        };
        Position = new Vector3(0.0f, 0.0f, 0.0f);
        Rotation = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
    }

    protected void LoadCloneBase(int cbid)
    {
        CloneBaseObject = AssetManager.Instance.GetCloneBase<CloneBaseObject>(cbid);
    }

    protected void SetCoid(long coid, bool global)
    {
        ObjectId.Coid = coid;
        ObjectId.Global = global;
    }

    public virtual void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        // TODO: only fill fields which are ClonedObjectBase specific!

        packet.CBID = CBID;
        packet.CoidStore = -1;
        packet.CurrentHealth = 100;
        packet.MaximumHealth = 100;
        packet.Value = CloneBaseObject.CloneBaseSpecific.BaseValue;
        packet.Faction = Faction;
        packet.CustomValue = CustomValue;

        for (var i = 0; i < 5; ++i)
        {
            packet.Prefixes[i] = -1;
            packet.PrefixLevels[i] = 0;

            packet.Gadgets[i] = -1;
            packet.GadgetLevels[i] = 0;
        }

        packet.Position = Position;
        packet.Rotation = Rotation;
        packet.Scale = Scale;
        packet.IsCorpse = false;
        packet.ObjectId = ObjectId;
        packet.WillEquip = false;
        packet.IsItemLink = false;
        packet.IsInInventory = false;
        packet.IsIdentified = false;
        packet.PossibleMissionItem = false;
        packet.TempItem = false;
        packet.IsKit = false;
        packet.IsInfinite = false;
        packet.IsBound = false;
        packet.UsesLeft = CloneBaseObject.SimpleObjectSpecific.MaxUses;
        packet.CustomizedName = "";
        packet.MadeFromMemory = false;
        packet.IsMail = false;
        packet.RequiredLevel = CloneBaseObject.SimpleObjectSpecific.RequiredLevel;
        packet.RequiredCombat = CloneBaseObject.SimpleObjectSpecific.RequiredCombat;
        packet.RequiredPerception = CloneBaseObject.SimpleObjectSpecific.RequiredPerception;
        packet.RequiredTech = CloneBaseObject.SimpleObjectSpecific.RequiredTech;
        packet.RequiredTheory = CloneBaseObject.SimpleObjectSpecific.RequiredTheory;
    }
}
