namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Packets.Sector;

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
    protected uint HP { get; set; }
    protected uint MaxHP { get; set; }
    protected int ItemTemplateId { get; set; }
    protected byte InventoryPositionX { get; set; }
    protected byte InventoryPositionY { get; set; }
    protected byte SkillLevel1 { get; set; }
    protected byte SkillLevel2 { get; set; }
    protected byte SkillLevel3 { get; set; }
    protected bool AlreadyAssembled { get; set; }
    #endregion

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

        return true;
    }

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        packet.MaxGadgets = MaxGadgets;
        packet.TeamFaction = TeamFaction;
        packet.InventoryPositionX = InventoryPositionX;
        packet.InventoryPositionY = InventoryPositionY;
        packet.Quantity = Quantity;
        packet.ItemTemplateId = ItemTemplateId;
        packet.SkillLevel1 = SkillLevel1;
        packet.SkillLevel2 = SkillLevel2;
        packet.SkillLevel3 = SkillLevel3;
    }
}
