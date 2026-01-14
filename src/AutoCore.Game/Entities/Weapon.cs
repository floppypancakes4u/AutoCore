namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

public class Weapon : SimpleObject
{
    #region Properties
    #region Database Weapon properties
    private SimpleObjectData DBData { get; set; }
    #endregion

    public CloneBaseWeapon CloneBaseWeapon => CloneBaseObject as CloneBaseWeapon;
    #endregion

    public Weapon()
        : base(GraphicsObjectType.Graphics)
    {
    }

    public override bool LoadFromDB(CharContext context, long coid, bool isInCharacterSelection = false)
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

        if (packet is CreateWeaponPacket weaponPacket)
        {
            var weaponSpec = CloneBaseWeapon.WeaponSpecific;
            
            weaponPacket.VarianceRange = 0.0f;
            weaponPacket.VarianceRefireRate = 0.0f;
            weaponPacket.VarianceDamageMinimum = 0.0f;
            weaponPacket.VarianceDamageMaximum = 0.0f;
            weaponPacket.VarianceOffensiveBonus = 0;
            weaponPacket.PrefixAccuracyBonus = weaponSpec.AccucaryModifier;
            weaponPacket.PrefixPenetrationBonus = weaponSpec.PenetrationModifier;
            weaponPacket.RechargeTime = weaponSpec.RechargeTime;
            weaponPacket.Mass = CloneBaseWeapon.SimpleObjectSpecific.Mass;
            weaponPacket.RangeMinimum = weaponSpec.RangeMin;
            weaponPacket.RangeMaximum = weaponSpec.RangeMax;
            weaponPacket.ValidArc = weaponSpec.ValidArc;
            weaponPacket.MinimumDamage = weaponSpec.MinMin;
            weaponPacket.MaximumDamage = weaponSpec.MaxMax;
            weaponPacket.Name = "";
        }
    }
}
