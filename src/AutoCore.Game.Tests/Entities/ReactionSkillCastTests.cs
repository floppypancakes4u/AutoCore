using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

[TestClass]
public class ReactionSkillCastTests
{
    private readonly List<string> _incomplete = new();
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        IncompleteHandlerLog.TestSink = message => _incomplete.Add(message);
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 857,
            Name = "repair_pad_heal",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, ValueBase = 125, ValuePerLevel = 25 }
            }
        });
    }

    [TestCleanup]
    public void TearDown()
    {
        IncompleteHandlerLog.TestSink = null;
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestSkills();
        _incomplete.Clear();
        _sent.Clear();
    }

    [TestMethod]
    public void SkillCast_RepairSkill_HealsActivatorAndSendsEffect()
    {
        var (_, vehicle, map) = CreatePlayer();
        vehicle.SetHPForTests(100);
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

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.AreEqual(225, vehicle.GetCurrentHP());
        var effect = _sent.OfType<SkillStatusEffectPacket>().Single();
        Assert.AreEqual(857, effect.SkillId);
        Assert.AreEqual((short)1, effect.SkillLevel);
        Assert.AreEqual(vehicle.ObjectId.Coid, effect.Targets.Single().Target.Coid);
        Assert.IsFalse(_incomplete.Any(message => message.Contains("[Reaction.Unhandled]")));
    }

    [TestMethod]
    public void SkillCast_UnknownSkill_FailsWithoutHealingOrUnhandledLog()
    {
        var (_, vehicle, map) = CreatePlayer();
        vehicle.SetHPForTests(100);
        var reaction = new Reaction(new ReactionTemplate
        {
            COID = 16447,
            Name = "missing_skill",
            ReactionType = ReactionType.SkillCast,
            GenericVar1 = 999999,
            GenericVar3 = 1,
        });
        reaction.SetCoid(16447, false);
        reaction.SetMap(map);

        Assert.IsFalse(reaction.TriggerIfPossible(vehicle));

        Assert.AreEqual(100, vehicle.GetCurrentHP());
        Assert.IsFalse(_incomplete.Any(message => message.Contains("[Reaction.Unhandled]")));
    }

    [TestMethod]
    public void SkillCast_PercentHealEquation_UsesMaximumHealth()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 857,
            Name = "INC Repair station heal",
            CategoryId = -1,
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = 0.15f }
            }
        });
        var (_, vehicle, map) = CreatePlayer();
        vehicle.SetHPForTests(1);
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

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.AreEqual(76, vehicle.GetCurrentHP(), "15% of the vehicle's 500 max HP should restore 75 HP");
    }

    private static (Character Character, Vehicle Vehicle, AutoCore.Game.Map.SectorMap Map) CreatePlayer()
    {
        var map = AutoCore.Game.Map.SectorMap.CreateForTests(new ContinentObject
        {
            Id = 987,
            MapFileName = "tm_skill_cast",
            DisplayName = "test",
            IsPersistent = true,
        }, new Vector4());
        var connection = new TNLConnection();
        var character = new Character();
        character.SetCoid(900, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle();
        vehicle.SetCoid(901, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}
