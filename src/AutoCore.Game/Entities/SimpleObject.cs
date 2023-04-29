namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL.Ghost;

public class SimpleObject : ClonedObjectBase
{
    #region Properties
    #region Database SimpleObject data
    private SimpleObjectData DBData { get; set; }
    #endregion

    protected int[] Prefixes { get; set; }
    protected int[] Gadgets { get; set; }
    protected short MaxGadgets { get; set; }
    protected int TeamFaction { get; set; }
    protected int Quantity { get; set; }
    protected int HP { get; set; }
    protected int MaxHP { get; set; }
    protected int ItemTemplateId { get; set; }
    protected byte InventoryPositionX { get; set; }
    protected byte InventoryPositionY { get; set; }
    protected byte SkillLevel1 { get; set; }
    protected byte SkillLevel2 { get; set; }
    protected byte SkillLevel3 { get; set; }
    protected bool AlreadyAssembled { get; set; }
    #endregion

    public override int GetCurrentHP() => HP;
    public override int GetMaximumHP() => MaxHP;
    public override int GetBareTeamFaction() => TeamFaction;

    public SimpleObject()
        : base()
    {
        MaxGadgets = 0;
        TeamFaction = 0;
        HP = 0;
        MaxHP = 500;
        InventoryPositionX = 0;
        InventoryPositionY = 0;
        AlreadyAssembled = false;
        Quantity = 1;
        ItemTemplateId = -1;
        SkillLevel1 = 1;
        SkillLevel2 = 1;
        SkillLevel3 = 1;
    }

    public virtual bool LoadFromDB(CharContext context, long coid)
    {
        SetCoid(coid, true);

        DBData = context.SimpleObjects.FirstOrDefault(so => so.Coid == coid);
        if (DBData == null)
            return false;

        LoadCloneBase(DBData.CBID);

        HP = MaxHP = CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;

        return true;
    }

    public override void CreateGhost()
    {
        Ghost = new GhostObject();
        Ghost.SetParent(this);
    }

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        packet.CBID = CBID;
        packet.ObjectId = ObjectId;
        packet.CurrentHealth = HP;
        packet.MaximumHealth = MaxHP;
        packet.Quantity = Quantity;
        packet.InventoryPositionX = InventoryPositionX;
        packet.InventoryPositionY = InventoryPositionY;
        packet.Value = CloneBaseObject.CloneBaseSpecific.BaseValue;
        packet.Faction = Faction;
        packet.TeamFaction = TeamFaction;
        packet.CoidStore = -1;
        packet.IsCorpse = false;
        packet.SkillLevel1 = SkillLevel1;
        packet.SkillLevel2 = SkillLevel2;
        packet.SkillLevel3 = SkillLevel3;
        packet.IsIdentified = true;
        packet.PossibleMissionItem = false;
        packet.TempItem = false;
        packet.WillEquip = false;
        packet.IsInInventory = false;
        packet.IsItemLink = false;
        packet.IsBound = true;
        packet.UsesLeft = CloneBaseObject.SimpleObjectSpecific.MaxUses;
        packet.CustomizedName = string.Empty;
        packet.MadeFromMemory = false;
        packet.IsMail = false;
        packet.CustomValue = CustomValue;
        packet.IsKit = false;
        packet.IsInfinite = false;

        for (var i = 0; i < 5; ++i)
        {
            packet.Prefixes[i] = -1;
            packet.PrefixLevels[i] = 0;

            packet.Gadgets[i] = -1;
            packet.GadgetLevels[i] = 0;
        }

        packet.MaxGadgets = MaxGadgets;
        packet.ItemTemplateId = ItemTemplateId;
        packet.RequiredLevel = CloneBaseObject.SimpleObjectSpecific.RequiredLevel;
        packet.RequiredCombat = CloneBaseObject.SimpleObjectSpecific.RequiredCombat;
        packet.RequiredPerception = CloneBaseObject.SimpleObjectSpecific.RequiredPerception;
        packet.RequiredTech = CloneBaseObject.SimpleObjectSpecific.RequiredTech;
        packet.RequiredTheory = CloneBaseObject.SimpleObjectSpecific.RequiredTheory;
        packet.Scale = Scale;
        packet.Position = Position;
        packet.Rotation = Rotation;
    }
}
