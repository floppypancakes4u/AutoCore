namespace AutoCore.Game.Entities;

using AutoCore.Game.CloneBases;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Packets.Sector;

public class Armor : SimpleObject
{
    #region Properties
    #region Database Armor properties
    private SimpleObjectData DBData { get; set; }
    #endregion

    public CloneBaseArmor CloneBaseArmor => CloneBaseObject as CloneBaseArmor;
    #endregion

    public Armor()
    {
    }

    public override bool LoadFromDB(CharContext context, long coid)
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

        if (packet is CreateArmorPacket armorPacket)
        {
            armorPacket.ArmorSpecific = CloneBaseArmor.ArmorSpecific;
            armorPacket.Mass = CloneBaseArmor.SimpleObjectSpecific.Mass;
            armorPacket.Name = "";
            armorPacket.VarianceDefensiveBonus = 0;
        }
    }
}
