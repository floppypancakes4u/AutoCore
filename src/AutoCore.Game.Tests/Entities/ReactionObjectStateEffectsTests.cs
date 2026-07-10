using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

[TestClass]
public class ReactionObjectStateEffectsTests
{
    private const int ContId = 833;

    [TestMethod]
    public void ApplyInvincible_NullTemplate_ReturnsTrue()
    {
        Assert.IsTrue(ReactionObjectStateEffects.ApplyInvincible(null, null, true));
    }

    [TestMethod]
    public void ApplyFactionFromVar_NullTemplate_ReturnsTrue()
    {
        Assert.IsTrue(ReactionObjectStateEffects.ApplyFactionFromVar(null, null));
    }

    [TestMethod]
    public void EnumerateTargets_NullTemplate_Empty()
    {
        Assert.AreEqual(0, ReactionObjectStateEffects.EnumerateTargets(null, null).Count());
    }

    [TestMethod]
    public void EnumerateTargets_ActOnActivator_YieldsActivatorOnly()
    {
        var (vehicle, map) = CreateVehicleOnMap();
        var other = new GraphicsObject(GraphicsObjectType.Graphics);
        other.InitializeHealthForTests(1);
        other.SetCoid(99, false);
        other.SetMap(map);

        var tpl = new ReactionTemplate
        {
            COID = 1,
            ReactionType = ReactionType.MakeNotInvincbile,
            ActOnActivator = true,
        };
        tpl.Objects.Add(99);

        var targets = ReactionObjectStateEffects.EnumerateTargets(tpl, vehicle).ToList();
        Assert.AreEqual(1, targets.Count);
        Assert.AreSame(vehicle, targets[0]);
    }

    [TestMethod]
    public void EnumerateTargets_NoMap_Empty()
    {
        var tpl = new ReactionTemplate { COID = 2, ReactionType = ReactionType.MakeInvincible };
        tpl.Objects.Add(1);
        Assert.AreEqual(0, ReactionObjectStateEffects.EnumerateTargets(tpl, null).Count());
    }

    [TestMethod]
    public void EnumerateTargets_MissingObject_LogsAndSkips()
    {
        var (vehicle, map) = CreateVehicleOnMap();
        var tpl = new ReactionTemplate { COID = 3, ReactionType = ReactionType.MakeInvincible };
        tpl.Objects.Add(404);
        Assert.AreEqual(0, ReactionObjectStateEffects.EnumerateTargets(tpl, vehicle).Count());
    }

    [TestMethod]
    public void ApplyInvincible_MultipleObjects()
    {
        var (vehicle, map) = CreateVehicleOnMap();
        var a = Place(map, 10);
        var b = Place(map, 11);
        a.SetInvincible(false);
        b.SetInvincible(false);

        var tpl = new ReactionTemplate { COID = 4, ReactionType = ReactionType.MakeInvincible };
        tpl.Objects.Add(10);
        tpl.Objects.Add(11);

        Assert.IsTrue(ReactionObjectStateEffects.ApplyInvincible(tpl, vehicle, invincible: true));
        Assert.IsTrue(a.IsInvincible);
        Assert.IsTrue(b.IsInvincible);
    }

    [TestMethod]
    public void ApplyFactionFromVar_NoCharacter_Skips()
    {
        var map = CreateMap();
        var lone = new Vehicle();
        lone.SetCoid(50, true);
        lone.SetMap(map);
        var prop = Place(map, 51);
        prop.Faction = 1;

        var tpl = new ReactionTemplate
        {
            COID = 5,
            ReactionType = ReactionType.SetFactionFromVar,
            GenericVar1 = 1,
        };
        tpl.Objects.Add(51);

        Assert.IsTrue(ReactionObjectStateEffects.ApplyFactionFromVar(tpl, lone));
        Assert.AreEqual(1, prop.Faction);
    }

    [TestMethod]
    public void ApplyFactionFromVar_WithStore_SetsFaction()
    {
        var (vehicle, map, character) = CreatePlayer();
        map.MapData.Variables[3] = Variable.CreateForTests(3, LogicVariableStore.TypeConstant, 0f, 0f);
        character.EnsureLogicVariables().Set(3, 88.4f);
        var prop = Place(map, 60);

        var tpl = new ReactionTemplate
        {
            COID = 6,
            ReactionType = ReactionType.SetFactionFromVar,
            GenericVar1 = 3,
        };
        tpl.Objects.Add(60);

        Assert.IsTrue(ReactionObjectStateEffects.ApplyFactionFromVar(tpl, vehicle));
        Assert.AreEqual(88, prop.Faction);
    }

    private static GraphicsObject Place(SectorMap map, long coid)
    {
        var o = new GraphicsObject(GraphicsObjectType.Graphics);
        o.InitializeHealthForTests(5);
        o.SetCoid(coid, false);
        o.SetMap(map);
        return o;
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_roe_{ContId}",
            DisplayName = "t",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static (Vehicle Vehicle, SectorMap Map) CreateVehicleOnMap()
    {
        var map = CreateMap();
        var vehicle = new Vehicle();
        vehicle.SetCoid(1, true);
        vehicle.SetMap(map);
        return (vehicle, map);
    }

    private static (Vehicle Vehicle, SectorMap Map, Character Character) CreatePlayer()
    {
        var map = CreateMap();
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        var character = new Character();
        character.SetCoid(2, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle();
        vehicle.SetCoid(3, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (vehicle, map, character);
    }
}
