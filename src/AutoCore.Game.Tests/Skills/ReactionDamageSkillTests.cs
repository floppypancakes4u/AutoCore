using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Skills;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Reaction SkillCast (e.g. Ark Bay l1_coll_dealsdamage / skill 2567 "Damage 50%"):
/// heal element type 10 with EquationType 1 and negative base is percent-of-max damage.
/// </summary>
[TestClass]
public class ReactionDamageSkillTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestSkills();
        SkillService.ClearCooldownsForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestSkills();
        SkillService.ClearCooldownsForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void TryCastReaction_PercentDamageHealElement_AppliesHalfMaxHpDamage()
    {
        // Retail skill 2567 "Damage 50%": elements=[10:1:-0.5]
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2567,
            Name = "Damage 50%",
            Elements = new List<SkillElement>
            {
                new()
                {
                    ElementType = SkillElementTypes.Heal,
                    EquationType = 1,
                    ValueBase = -0.5f,
                },
            }
        });

        var (character, vehicle, _) = CreatePlayer();
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(200);

        Assert.IsTrue(SkillService.TryCastReaction(vehicle, 2567, 1),
            "Negative percent heal must be supported as damage, not rejected");

        Assert.AreEqual(100, vehicle.GetCurrentHP(), "50% of 200 max = 100 damage");
        Assert.IsTrue(_sent.OfType<SkillStatusEffectPacket>().Any(),
            "Client must receive skill effect for VFX");
        Assert.IsTrue(_sent.OfType<DamagePacket>().Any() || _sent.Any(p => p.GetType().Name.Contains("Damage")),
            "Client must receive damage so vehicle HP bar updates. Packets: "
            + string.Join(", ", _sent.Select(p => p.GetType().Name)));
    }

    [TestMethod]
    public void TryCastReaction_FlatNegativeHeal_AppliesDamage()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2568,
            Name = "pain flat",
            Elements = new List<SkillElement>
            {
                new()
                {
                    ElementType = SkillElementTypes.Heal,
                    EquationType = 0,
                    ValueBase = -40f,
                },
            }
        });

        var (_, vehicle, _) = CreatePlayer(characterCoid: 912, vehicleCoid: 913);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(150);

        Assert.IsTrue(SkillService.TryCastReaction(vehicle, 2568, 1));
        Assert.AreEqual(110, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TryCastReaction_PositivePercentHeal_StillWorks()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2569,
            Name = "pad heal",
            Elements = new List<SkillElement>
            {
                new()
                {
                    ElementType = SkillElementTypes.Heal,
                    EquationType = 1,
                    ValueBase = 0.25f,
                },
            }
        });

        var (_, vehicle, _) = CreatePlayer(characterCoid: 914, vehicleCoid: 915);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(10);

        Assert.IsTrue(SkillService.TryCastReaction(vehicle, 2569, 1));
        Assert.AreEqual(60, vehicle.GetCurrentHP(), "25% of 200 = 50 restored");
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer(
        long characterCoid = 910,
        long vehicleCoid = 911)
    {
        var continent = new ContinentObject
        {
            Id = 8901,
            MapFileName = "tm_pain_skill",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(characterCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}
