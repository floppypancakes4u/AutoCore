using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Wrong-type clonebase on a Weapon must not NRE; log and ignore weapon-stat fill.
/// </summary>
[TestClass]
public class WeaponWriteToPacketTests
{
    [TestMethod]
    public void WriteToPacket_WrongCloneType_DoesNotThrow_LeavesWeaponFieldsDefault()
    {
        var wrongType = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        wrongType.CloneBaseSpecific = new CloneBaseSpecific
        {
            Type = (int)CloneBaseObjectType.Item,
            CloneBaseId = 10479,
        };
        wrongType.SimpleObjectSpecific = new SimpleObjectSpecific();

        var weapon = new Weapon();
        weapon.SetCoid(18383, true);
        weapon.AssignCloneBaseForTests(wrongType);

        var packet = new CreateWeaponPacket();
        weapon.WriteToPacket(packet);

        // Ignored: no base/stat fill from wrong-type clonebase.
        Assert.AreEqual(0, packet.CBID);
        Assert.AreEqual(0f, packet.Mass);
        Assert.IsNull(packet.MinimumDamage);
        Assert.IsNull(packet.MaximumDamage);
    }

    [TestMethod]
    public void WriteToPacket_ValidWeapon_StillMapsSpec()
    {
        var clone = (CloneBaseWeapon)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseWeapon));
        clone.CloneBaseSpecific = new CloneBaseSpecific
        {
            Type = (int)CloneBaseObjectType.Weapon,
            CloneBaseId = 9001,
        };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { Mass = 3.5f };
        clone.WeaponSpecific = new WeaponSpecific
        {
            AccucaryModifier = 1.1f,
            RechargeTime = 500,
            RangeMin = 1f,
            RangeMax = 20f,
            ValidArc = 45f,
            PenetrationModifier = 2,
            MinMin = DamageSpecific.CreateEmpty(),
            MaxMax = DamageSpecific.CreateEmpty(),
        };

        var weapon = new Weapon();
        weapon.SetCoid(1, true);
        weapon.AssignCloneBaseForTests(clone);

        var packet = new CreateWeaponPacket();
        weapon.WriteToPacket(packet);

        Assert.AreEqual(9001, packet.CBID);
        Assert.AreEqual(3.5f, packet.Mass);
        Assert.AreEqual(500, packet.RechargeTime);
        Assert.AreEqual(1.1f, packet.PrefixAccuracyBonus);
    }
}
