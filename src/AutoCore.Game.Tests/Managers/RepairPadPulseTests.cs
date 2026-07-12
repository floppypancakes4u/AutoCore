using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

[TestClass]
public class RepairPadPulseTests
{
    [TestInitialize]
    public void SetUp()
    {
        TriggerManager.Instance.ClearAllForTests();
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 857,
            Name = "INC Repair station heal",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = 0.15f }
            }
        });
    }

    [TestCleanup]
    public void TearDown()
    {
        TriggerManager.Instance.ClearAllForTests();
        AssetManager.Instance.ClearTestSkills();
    }

    [TestMethod]
    public void RepairPad_PulsesEachVehicleIndependentlyEverySecondUntilFullOrExit()
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = 988,
            MapFileName = "tm_repair_pulse",
            DisplayName = "test",
            IsPersistent = true,
        }, new Vector4());
        var reaction = new Reaction(new ReactionTemplate
        {
            COID = 16446,
            Name = "l1_skillcast_heal",
            ReactionType = ReactionType.SkillCast,
            GenericVar1 = 857,
            GenericVar3 = 1,
        });
        reaction.SetCoid(16446, false);
        reaction.SetMap(map);
        var triggerTemplate = new TriggerTemplate
        {
            COID = 16475,
            Name = "l1_coll_humanrepairpad_3",
            TargetType = TriggerTargetType.Players,
            Scale = 10,
            DoCollision = true,
            ActivationCount = -1,
        };
        triggerTemplate.Reactions.Add(16446);
        var trigger = new Trigger(triggerTemplate) { Position = new Vector3(), Scale = 10 };
        trigger.SetCoid(16475, false);
        trigger.SetMap(map);

        var first = CreatePlayerVehicle(map, 1000, 1001);
        var second = CreatePlayerVehicle(map, 2000, 2001);
        first.InitializeHealthForTests(100);
        second.InitializeHealthForTests(100);
        first.SetHPForTests(1);
        second.SetHPForTests(1);

        TriggerManager.Instance.CheckTriggersFor(first, nowMs: 0);
        TriggerManager.Instance.CheckTriggersFor(second, nowMs: 0);
        Assert.AreEqual(16, first.GetCurrentHP());
        Assert.AreEqual(16, second.GetCurrentHP());

        TriggerManager.Instance.CheckTriggersFor(first, nowMs: 999);
        TriggerManager.Instance.CheckTriggersFor(second, nowMs: 999);
        Assert.AreEqual(16, first.GetCurrentHP());
        Assert.AreEqual(16, second.GetCurrentHP());

        TriggerManager.Instance.CheckTriggersFor(first, nowMs: 1000);
        TriggerManager.Instance.CheckTriggersFor(second, nowMs: 1000);
        Assert.AreEqual(31, first.GetCurrentHP());
        Assert.AreEqual(31, second.GetCurrentHP());

        first.SetHPForTests(100);
        second.Position = new Vector3(100, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(first, nowMs: 2000);
        TriggerManager.Instance.CheckTriggersFor(second, nowMs: 2000);
        Assert.AreEqual(100, first.GetCurrentHP(), "full vehicles must stop receiving repair casts");
        Assert.AreEqual(31, second.GetCurrentHP(), "vehicles outside the pad must stop receiving repair casts");
    }

    private static Vehicle CreatePlayerVehicle(SectorMap map, long characterCoid, long vehicleCoid)
    {
        var connection = new TNLConnection();
        var character = new Character();
        character.SetCoid(characterCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle { Position = new Vector3() };
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return vehicle;
    }
}
