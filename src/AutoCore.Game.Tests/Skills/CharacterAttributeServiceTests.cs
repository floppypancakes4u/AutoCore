using System.Reflection;
using AutoCore.Database.Char.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Skills;

[TestClass]
public class CharacterAttributeServiceTests
{
    private const int HumanRaiderBodyCbid = 910_101;
    private const int CallistoLikeVehicleCbid = 910_102;
    private const int StarterArmorCbid = 910_103;
    private const int PowerPlantCbid = 910_104;

    private int _persistCalls;

    [TestInitialize]
    public void Init()
    {
        _persistCalls = 0;
        CharacterAttributeService.PersistForTests = _ => _persistCalls++;
        CharacterLevelManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCharacterCloneBase(HumanRaiderBodyCbid, race: 0, classId: 3);
        AssetManagerTestHelper.RegisterVehicleCloneBase(CallistoLikeVehicleCbid, maxHitPoint: 1, armorAdd: 7);
        AssetManagerTestHelper.RegisterArmorCloneBase(StarterArmorCbid, armorFactor: 13);
        AssetManagerTestHelper.RegisterPowerPlantCloneBase(PowerPlantCbid);
        var pp = (CloneBasePowerPlant)AssetManager.Instance.GetCloneBase(PowerPlantCbid)!;
        pp.PowerPlantSpecific = new PowerPlantSpecific
        {
            HeatMaximum = 100,
            PowerMaximum = 100,
            PowerRegenRate = 10,
            CoolRate = 10,
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        CharacterAttributeService.PersistForTests = null;
        CharacterLevelManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void TryIncrement_NullCharacter_Fails()
    {
        Assert.IsFalse(CharacterAttributeService.Instance.TryIncrement(null, CharacterAttributeKind.Tech.ToMask(), out var error));
        Assert.IsFalse(string.IsNullOrEmpty(error));
    }

    [TestMethod]
    public void TryIncrement_NoPoints_Fails()
    {
        var character = MakeCharacter(1, attributePoints: 0);
        character.SetAttributeTech(5);

        Assert.IsFalse(CharacterAttributeService.Instance.TryIncrement(character, CharacterAttributeKind.Tech.ToMask(), out var error));
        Assert.AreEqual(5, character.AttributeTech);
        Assert.AreEqual(0, character.AttributePoints);
        StringAssert.Contains(error, "point");
    }

    [TestMethod]
    public void TryIncrement_UnknownMask_Fails()
    {
        var character = MakeCharacter(2, attributePoints: 3);
        Assert.IsFalse(CharacterAttributeService.Instance.TryIncrement(character, 0xDEADBEEFu, out var error));
        Assert.AreEqual(3, character.AttributePoints);
        StringAssert.Contains(error, "Unknown");
    }

    [TestMethod]
    public void TryIncrement_Tech_SpendsPointAndRaisesTech()
    {
        var character = MakeCharacter(3, attributePoints: 2);
        character.SetAttributeTech(5);

        Assert.IsTrue(CharacterAttributeService.Instance.TryIncrement(character, CharacterAttributeKind.Tech.ToMask(), out _));
        Assert.AreEqual(6, character.AttributeTech);
        Assert.AreEqual(1, character.AttributePoints);
        Assert.AreEqual(1, _persistCalls);
        // Spent attrs are on CharacterData for progress snapshot persistence.
        Assert.AreEqual((short)6, character.ToProgressSnapshot().AttributeTech);
    }

    [TestMethod]
    public void ToProgressSnapshot_IncludesAllFourSpentAttributes()
    {
        var character = MakeCharacter(30, attributePoints: 0);
        character.SetAttributeTech(4);
        character.SetAttributeCombat(3);
        character.SetAttributeTheory(2);
        character.SetAttributePerception(1);

        var snap = character.ToProgressSnapshot();
        Assert.AreEqual((short)4, snap.AttributeTech);
        Assert.AreEqual((short)3, snap.AttributeCombat);
        Assert.AreEqual((short)2, snap.AttributeTheory);
        Assert.AreEqual((short)1, snap.AttributePerception);
    }

    [TestMethod]
    public void TryIncrement_Combat_StoresOnly_NoHpOrHeatChange()
    {
        var (character, vehicle) = MakeCharacterWithVehicle(4, tech: 5, attributePoints: 1);
        var hpBefore = vehicle.GetMaximumHP();
        var heatBefore = vehicle.MaxHeat;

        Assert.IsTrue(CharacterAttributeService.Instance.TryIncrement(character, CharacterAttributeKind.Combat.ToMask(), out _));
        Assert.AreEqual(2, character.AttributeCombat); // default floor 1 + spend
        Assert.AreEqual(0, character.AttributePoints);
        Assert.AreEqual(5, character.AttributeTech);
        Assert.AreEqual(hpBefore, vehicle.GetMaximumHP());
        Assert.AreEqual(heatBefore, vehicle.MaxHeat);
    }

    [TestMethod]
    public void TryIncrement_Tech_RecalculatesHpAndHeat()
    {
        // tech 6→7: HP +3 always; heat +1 when tech*0.5 crosses a half-integer ceil boundary.
        var (character, vehicle) = MakeCharacterWithVehicle(5, tech: 6, attributePoints: 1);
        var hpBefore = vehicle.GetMaximumHP();
        var heatBefore = vehicle.MaxHeat;

        Assert.IsTrue(CharacterAttributeService.Instance.TryIncrement(character, CharacterAttributeKind.Tech.ToMask(), out _));

        Assert.AreEqual(7, character.AttributeTech);
        Assert.IsTrue(vehicle.GetMaximumHP() > hpBefore, "Tech spend must raise max HP");
        Assert.IsTrue(vehicle.MaxHeat > heatBefore, "Tech spend must raise heat cap on ceil boundary");
        Assert.AreEqual(hpBefore + 3, vehicle.GetMaximumHP(), "tech pool +1 → +3 HP (TechScale 3)");
        Assert.AreEqual(heatBefore + 1, vehicle.MaxHeat);
    }

    [TestMethod]
    public void TryIncrement_Perception_StoresOnly_NoHpHeatOrPowerChange()
    {
        var (character, vehicle) = MakeCharacterWithVehicle(6, tech: 5, attributePoints: 1);
        vehicle.ApplyPowerPlantCapacities(startPowerAtFull: true, clearHeat: true);
        var powerBefore = CharacterLevelManager.Instance.GetPower(character.ObjectId.Coid).Maximum;

        Assert.IsTrue(CharacterAttributeService.Instance.TryIncrement(character, CharacterAttributeKind.Perception.ToMask(), out _));
        Assert.AreEqual(2, character.AttributePerception); // default floor 1 + spend
        Assert.AreEqual(powerBefore, CharacterLevelManager.Instance.GetPower(character.ObjectId.Coid).Maximum);
    }

    [TestMethod]
    public void TryIncrement_Theory_RaisesMaxPower()
    {
        var (character, vehicle) = MakeCharacterWithVehicle(7, tech: 5, attributePoints: 1);
        character.SetAttributeTheory(1);
        vehicle.ApplyPowerPlantCapacities(startPowerAtFull: true, clearHeat: true);
        var powerBefore = CharacterLevelManager.Instance.GetPower(character.ObjectId.Coid).Maximum;

        Assert.IsTrue(CharacterAttributeService.Instance.TryIncrement(character, CharacterAttributeKind.Theory.ToMask(), out _));
        Assert.AreEqual(2, character.AttributeTheory);
        var powerAfter = CharacterLevelManager.Instance.GetPower(character.ObjectId.Coid).Maximum;
        Assert.IsTrue(powerAfter > powerBefore, "Theory spend must raise max power (+2 per point)");
        Assert.AreEqual(powerBefore + 2, powerAfter);
    }

    [TestMethod]
    public void TryIncrement_Theory_WithoutVehicle_StillStores()
    {
        var character = MakeCharacter(8, attributePoints: 1);
        character.SetAttributeTheory(1);
        Assert.IsTrue(CharacterAttributeService.Instance.TryIncrement(character, CharacterAttributeKind.Theory.ToMask(), out _));
        Assert.AreEqual(2, character.AttributeTheory);
        Assert.AreEqual(1, _persistCalls);
    }

    private static Character MakeCharacter(long coid, short attributePoints = 0)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.LoadCloneBase(HumanRaiderBodyCbid);
        var dbData = new CharacterData
        {
            Coid = coid,
            Name = "AttrTest",
            Level = 1,
            BodyId = HumanRaiderBodyCbid,
            AttributePoints = attributePoints,
        };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
    }

    private (Character character, Vehicle vehicle) MakeCharacterWithVehicle(long coid, short tech, short attributePoints)
    {
        var character = MakeCharacter(coid, attributePoints);
        character.SetAttributeTech(tech);

        var vehicle = new Vehicle();
        vehicle.SetCoid(coid + 1000, true);
        vehicle.LoadCloneBase(CallistoLikeVehicleCbid);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);

        var armor = new Armor();
        armor.SetCoid(coid + 2000, true);
        armor.LoadCloneBase(StarterArmorCbid);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _));

        var plant = new PowerPlant();
        plant.SetCoid(coid + 3000, true);
        plant.LoadCloneBase(PowerPlantCbid);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.PowerPlant, plant, out _));
        vehicle.ApplyPowerPlantCapacities(startPowerAtFull: true, clearHeat: true);
        vehicle.RecalculateMaximumHitPoints(refillCurrent: true, triggerGhostUpdate: false);
        vehicle.RecalculateMaximumHeat(triggerGhostUpdate: false);

        return (character, vehicle);
    }
}
