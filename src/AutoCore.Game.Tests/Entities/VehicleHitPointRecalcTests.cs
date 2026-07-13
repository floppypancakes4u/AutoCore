using System.Reflection;
using AutoCore.Database.Char.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

/// <summary>
/// Callisto X and other player chassis store MaxHitPoint=1 in clonebase.wad.
/// Live max HP must come from Vehicle_CalcMaxHitPoints (armor + Tech + race/class).
/// </summary>
[TestClass]
public class VehicleHitPointRecalcTests
{
    private const int HumanRaiderBodyCbid = 900_101;
    private const int CallistoLikeVehicleCbid = 900_102;
    private const int StarterArmorCbid = 900_103;

    [TestInitialize]
    public void Init()
    {
        CharacterLevelManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        RegisterHumanRaiderBody();
        RegisterCallistoLikeVehicle();
        RegisterStarterArmor();
    }

    [TestCleanup]
    public void Cleanup()
    {
        CharacterLevelManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void RecalculateMaximumHitPoints_CallistoLike_HumanRaider_StarterArmor()
    {
        var character = MakeCharacter(50_001, HumanRaiderBodyCbid);
        var vehicle = new Vehicle();
        vehicle.SetCoid(50_002, true);
        vehicle.LoadCloneBase(CallistoLikeVehicleCbid);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);

        // Clonebase stub leaves MaxHP at 1 until recalc.
        Assert.AreEqual(1, vehicle.GetMaximumHP());

        var armor = new Armor();
        armor.SetCoid(50_003, true);
        armor.LoadCloneBase(StarterArmorCbid);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _));

        // tech=0 → pool tech 1; race=0 class=3; AF=13; ArmorAdd=7 → 162
        Assert.AreEqual(162, vehicle.GetMaximumHP());
        Assert.AreEqual(162, vehicle.GetCurrentHP(), "equip path preserves ratio from full stub → full after refill=false from 1/1");
    }

    [TestMethod]
    public void BuildPacket_AfterRecalc_SendsComputedVehicleHealth()
    {
        var character = MakeCharacter(50_011, HumanRaiderBodyCbid);
        var vehicle = new Vehicle();
        vehicle.SetCoid(50_012, true);
        vehicle.LoadCloneBase(CallistoLikeVehicleCbid);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetOwner(character);

        var armor = new Armor();
        armor.SetCoid(50_013, true);
        armor.LoadCloneBase(StarterArmorCbid);
        vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _);
        vehicle.RecalculateMaximumHitPoints(refillCurrent: true, triggerGhostUpdate: false);

        var packet = CharacterLevelManager.Instance.BuildPacket(character);
        Assert.AreEqual(162, packet.HealthMaximum);
        Assert.AreEqual(162, packet.Health);
    }

    private static Character MakeCharacter(long coid, int bodyCbid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.LoadCloneBase(bodyCbid);
        var dbData = new CharacterData { Coid = coid, Name = "HpTest", Level = 1, BodyId = bodyCbid };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
    }

    private static void RegisterHumanRaiderBody()
    {
        // Minimal CloneBaseCharacter-shaped registration: use helper if available, else fake vehicle path.
        // Character race/class come from CloneBaseCharacter.CharacterSpecific.
        AssetManagerTestHelper.RegisterCharacterCloneBase(HumanRaiderBodyCbid, race: 0, classId: 3);
    }

    private static void RegisterCallistoLikeVehicle()
    {
        AssetManagerTestHelper.RegisterVehicleCloneBase(
            CallistoLikeVehicleCbid,
            maxHitPoint: 1,
            armorAdd: 7);
    }

    private static void RegisterStarterArmor()
    {
        AssetManagerTestHelper.RegisterArmorCloneBase(StarterArmorCbid, armorFactor: 13);
    }
}
