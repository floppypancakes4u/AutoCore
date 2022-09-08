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

        if (packet is CreateWeaponPacket weaponPacket)
        {
            weaponPacket.VarianceRange = 0.0f;
            weaponPacket.VarianceRefireRate = 0.0f;
            weaponPacket.VarianceDamageMinimum = 0.0f;
            weaponPacket.VarianceDamageMaximum = 0.0f;
            weaponPacket.VarianceOffensiveBonus = 0;
            weaponPacket.PrefixAccuracyBonus = 0.0f;
            weaponPacket.PrefixPenetrationBonus = 0;
            weaponPacket.RechargeTime = 1;
            weaponPacket.Mass = CloneBaseWeapon.SimpleObjectSpecific.Mass;
            weaponPacket.RangeMinimum = CloneBaseWeapon.WeaponSpecific.RangeMin;
            weaponPacket.RangeMaximum = CloneBaseWeapon.WeaponSpecific.RangeMax;
            weaponPacket.ValidArc = CloneBaseWeapon.WeaponSpecific.ValidArc;
            weaponPacket.MinimumDamage = DamageSpecific.CreateEmpty();
            weaponPacket.MaximumDamage = DamageSpecific.CreateEmpty();
            weaponPacket.Name = "";
        }
    }
}
