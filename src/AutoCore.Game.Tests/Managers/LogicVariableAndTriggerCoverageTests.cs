using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Coverage for logic vars, trigger conditions, and TriggerManager edge paths.
/// Synthetic ids only.
/// </summary>
[TestClass]
public class LogicVariableAndTriggerCoverageTests
{
    private const int MissionId = 91100;
    private const int ObjectiveId = 92100;
    private const int ContId = 707;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void LogicVariableStore_Type12_ActiveObjective()
    {
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(MissionId, MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1)));

        var (character, _, map) = CreatePlayer();
        map.MapData.Variables[50] = Variable.CreateForTests(
            50, LogicVariableStore.TypeHasActiveObjective, ObjectiveId, 0f, "obj");
        map.MapData.Variables[51] = Variable.CreateForTests(
            51, LogicVariableStore.TypeHasActiveObjective, ObjectiveId + 99, 0f, "other");

        var store = character.EnsureLogicVariables();
        Assert.AreEqual(0f, store.Get(50));

        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        Assert.AreEqual(1f, store.Get(50));
        Assert.AreEqual(0f, store.Get(51));
        Assert.AreEqual(0f, store.Get(9999)); // missing id
    }

    [TestMethod]
    public void LogicVariableStore_Ctor_RejectsNulls()
    {
        var (character, _, map) = CreatePlayer();
        Assert.ThrowsException<ArgumentNullException>(() => new LogicVariableStore(null, character));
        Assert.ThrowsException<ArgumentNullException>(() => new LogicVariableStore(map, null));
    }

    [TestMethod]
    public void LogicVariableStore_Set_MutableConstant()
    {
        var (character, _, map) = CreatePlayer();
        map.MapData.Variables[1] = Variable.CreateForTests(1, LogicVariableStore.TypeConstant, 0f, 0f);
        var store = character.EnsureLogicVariables();
        store.Set(1, 42f);
        Assert.AreEqual(42f, store.Get(1));
    }

    [TestMethod]
    public void Character_EnsureLogicVariables_NullWithoutMap()
    {
        var character = new Character();
        character.SetCoid(1, true);
        Assert.IsNull(character.EnsureLogicVariables());
    }

    [TestMethod]
    public void TriggerConditional_AllComparisonOps()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Variables[10] = Variable.CreateForTests(10, LogicVariableStore.TypeConstant, 0f, 2f);
        map.MapData.Variables[11] = Variable.CreateForTests(11, LogicVariableStore.TypeConstant, 0f, 5f);
        character.EnsureLogicVariables();

        Assert.IsTrue(new TriggerConditional { LeftId = 10, RightId = 11, Type = ConditionalType.LessThan }.Check(vehicle));
        Assert.IsFalse(new TriggerConditional { LeftId = 11, RightId = 10, Type = ConditionalType.LessThan }.Check(vehicle));
        Assert.IsTrue(new TriggerConditional { LeftId = 11, RightId = 10, Type = ConditionalType.GreaterThan }.Check(vehicle));
        Assert.IsTrue(new TriggerConditional { LeftId = 10, RightId = 10, Type = ConditionalType.LessThanOrEqualTo }.Check(vehicle));
        Assert.IsTrue(new TriggerConditional { LeftId = 11, RightId = 10, Type = ConditionalType.GreaterThanOrEqualTo }.Check(vehicle));
        Assert.IsTrue(new TriggerConditional { LeftId = 10, RightId = 10, Type = ConditionalType.EqualTo }.Check(vehicle));
        Assert.IsTrue(new TriggerConditional { LeftId = 10, RightId = 11, Type = ConditionalType.NotEqualTo }.Check(vehicle));
        Assert.IsFalse(new TriggerConditional { LeftId = 10, RightId = 11, Type = (ConditionalType)99 }.Check(vehicle));
        Assert.IsFalse(new TriggerConditional { LeftId = 10, RightId = 11, Type = ConditionalType.EqualTo }.Check(null));
    }

    [TestMethod]
    public void TriggerConditional_Read_WireLayout()
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(7);
        w.Write(8);
        w.Write((byte)ConditionalType.NotEqualTo);
        w.Write(new byte[3]);
        w.Flush();
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        var c = TriggerConditional.Read(r);
        Assert.AreEqual(7, c.LeftId);
        Assert.AreEqual(8, c.RightId);
        Assert.AreEqual(ConditionalType.NotEqualTo, c.Type);
    }

    [TestMethod]
    public void Trigger_CanTrigger_TargetTypeAndRangeGuards()
    {
        var (character, vehicle, map) = CreatePlayer();
        var tpl = new TriggerTemplate
        {
            COID = 1,
            TargetType = TriggerTargetType.Creatures,
            Scale = 5f,
            ActivationCount = -1,
        };
        var trigger = new Trigger(tpl);
        trigger.SetCoid(1, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 5f;
        trigger.SetMap(map);

        Assert.IsFalse(trigger.CanTrigger(null));
        Assert.IsFalse(trigger.CanTrigger(character)); // Character + Creatures type
        vehicle.Position = new Vector3(0, 0, 0);
        Assert.IsFalse(trigger.CanTrigger(vehicle)); // player vehicle + Creatures

        tpl.TargetType = TriggerTargetType.Vehicles;
        Assert.IsTrue(trigger.CanTrigger(vehicle));

        vehicle.Position = new Vector3(100, 0, 0);
        Assert.IsFalse(trigger.CanTrigger(vehicle)); // out of range
    }

    [TestMethod]
    public void Trigger_ConditionsPass_AnyConditionMode()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Variables[1] = Variable.CreateForTests(1, LogicVariableStore.TypeConstant, 0f, 1f);
        map.MapData.Variables[2] = Variable.CreateForTests(2, LogicVariableStore.TypeConstant, 0f, 0f);
        map.MapData.Variables[3] = Variable.CreateForTests(3, LogicVariableStore.TypeConstant, 0f, 1f);
        character.EnsureLogicVariables();

        var tpl = new TriggerTemplate
        {
            COID = 2,
            TargetType = TriggerTargetType.Players,
            AllConditionsNeeded = false,
            Scale = 10f,
        };
        // First fails (1==0), second succeeds (1==1) → any-mode true
        tpl.Conditions.Add(new TriggerConditional { LeftId = 1, RightId = 2, Type = ConditionalType.EqualTo });
        tpl.Conditions.Add(new TriggerConditional { LeftId = 1, RightId = 3, Type = ConditionalType.EqualTo });
        var trigger = new Trigger(tpl);
        Assert.IsTrue(trigger.ConditionsPass(vehicle));

        tpl.AllConditionsNeeded = true;
        Assert.IsFalse(trigger.ConditionsPass(vehicle));
    }

    [TestMethod]
    public void Trigger_TriggerIfPossible_FiresReactions()
    {
        var (character, vehicle, map) = CreatePlayer();
        const long reactionCoid = 97001;
        const long objCoid = 97002;

        var delTpl = new ReactionTemplate { COID = (int)reactionCoid, ReactionType = ReactionType.Delete };
        delTpl.Objects.Add(objCoid);
        var del = new Reaction(delTpl);
        del.SetCoid(reactionCoid, false);
        del.SetMap(map);

        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(objCoid, false);
        obj.SetMap(map);

        var tpl = new TriggerTemplate
        {
            COID = 3,
            TargetType = TriggerTargetType.Players,
            Scale = 20f,
            ActivationCount = -1,
        };
        tpl.Reactions.Add(reactionCoid);
        var trigger = new Trigger(tpl);
        trigger.SetCoid(3, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 20f;
        trigger.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        Assert.IsTrue(trigger.TriggerIfPossible(vehicle));
        Assert.IsNull(map.GetObjectByCoid(objCoid));
        Assert.IsFalse(trigger.TriggerIfPossible(null));
    }

    [TestMethod]
    public void Trigger_TriggerIfPossible_PlayerActivator_LogsTriggerOccurrence()
    {
        var (character, vehicle, map) = CreatePlayer();

        var tpl = new TriggerTemplate
        {
            COID = 305,
            Name = "direct_trigger_log",
            TargetType = TriggerTargetType.Players,
            Scale = 20f,
            ActivationCount = -1,
        };
        var trigger = new Trigger(tpl);
        trigger.SetCoid(305, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 20f;
        trigger.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);
        try
        {
            Assert.IsTrue(trigger.TriggerIfPossible(vehicle));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.IsTrue(output.Contains("Player trigger occurred"));
        Assert.IsTrue(output.Contains("trigger=305"));
        Assert.IsTrue(output.Contains("playerCoid=150"));
        Assert.IsTrue(output.Contains("activatorCoid=151"));
    }

    [TestMethod]
    public void TriggerManager_ActivationCountZero_DoesNotFire()
    {
        var (character, vehicle, map) = CreatePlayer();
        var tpl = new TriggerTemplate
        {
            COID = 4,
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            ActivationCount = 0,
        };
        var trigger = new Trigger(tpl);
        trigger.SetCoid(4, false);
        trigger.SetMap(map);
        TriggerManager.Instance.FireTriggerReactions(vehicle, trigger);
        Assert.AreEqual(0, trigger.FireCount);
    }

    [TestMethod]
    public void TriggerManager_ActivationCountCap_StopsFiring()
    {
        var (character, vehicle, map) = CreatePlayer();
        var tpl = new TriggerTemplate
        {
            COID = 5,
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            ActivationCount = 1,
        };
        var trigger = new Trigger(tpl);
        trigger.SetCoid(5, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 10f;
        trigger.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        TriggerManager.Instance.FireTriggerReactions(vehicle, trigger);
        TriggerManager.Instance.FireTriggerReactions(vehicle, trigger);
        Assert.AreEqual(1, trigger.FireCount);
    }

    [TestMethod]
    public void TriggerManager_FireTriggerReactions_PlayerActivator_LogsTriggerOccurrence()
    {
        var (character, vehicle, map) = CreatePlayer();
        var tpl = new TriggerTemplate
        {
            COID = 505,
            Name = "managed_trigger_log",
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            ActivationCount = -1,
        };
        var trigger = new Trigger(tpl);
        trigger.SetCoid(505, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 10f;
        trigger.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);
        try
        {
            TriggerManager.Instance.FireTriggerReactions(vehicle, trigger);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.IsTrue(output.Contains("Player trigger occurred"));
        Assert.IsTrue(output.Contains("trigger=505"));
        Assert.IsTrue(output.Contains("playerCoid=150"));
        Assert.IsTrue(output.Contains("activatorCoid=151"));
    }

    [TestMethod]
    public void TriggerManager_CheckTriggersFor_EnterAndLeave()
    {
        var (character, vehicle, map) = CreatePlayer();
        const long reactionCoid = 97101;
        var delTpl = new ReactionTemplate { COID = (int)reactionCoid, ReactionType = ReactionType.Delete };
        var del = new Reaction(delTpl);
        del.SetCoid(reactionCoid, false);
        del.SetMap(map);

        var tpl = new TriggerTemplate
        {
            COID = 6,
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            DoCollision = true,
            ActivationCount = -1,
        };
        tpl.Reactions.Add(reactionCoid);
        var trigger = new Trigger(tpl);
        trigger.SetCoid(6, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 10f;
        trigger.SetMap(map);

        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.AreEqual(1, trigger.FireCount);

        // still inside — no re-fire
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.AreEqual(1, trigger.FireCount);

        // leave volume
        vehicle.Position = new Vector3(100, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);

        // re-enter
        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.AreEqual(2, trigger.FireCount);
    }

    [TestMethod]
    public void TriggerManager_ClearAndResetApis()
    {
        var (character, vehicle, map) = CreatePlayer();
        var tpl = new TriggerTemplate
        {
            COID = 7,
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            DoCollision = true,
            ActivationCount = -1,
        };
        var trigger = new Trigger(tpl);
        trigger.SetCoid(7, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 10f;
        trigger.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        TriggerManager.Instance.CheckTriggersFor(vehicle);
        TriggerManager.Instance.ClearTriggersFor(vehicle.ObjectId.Coid);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.IsTrue(trigger.FireCount >= 2);

        TriggerManager.Instance.ResetTriggerFor(vehicle.ObjectId.Coid, 7);
        TriggerManager.Instance.ClearTrigger(7);
        TriggerManager.Instance.FireTriggerReactions(null, trigger);
        TriggerManager.Instance.FireTriggerReactions(vehicle, null);
        TriggerManager.Instance.CheckTriggersFor(null);
        TriggerManager.Instance.OnMissionStateChanged(null);
        TriggerManager.Instance.OnVariableChanged(null, 1);
    }

    [TestMethod]
    public void TriggerManager_OnMissionStateChanged_UsesCharacterWhenNoVehicle()
    {
        var (character, vehicle, map) = CreatePlayer();
        character.SetCurrentVehicleForTests(null);
        character.Position = new Vector3(0, 0, 0);
        // Should not throw
        TriggerManager.Instance.OnMissionStateChanged(character);
    }

    [TestMethod]
    public void TriggerManager_OnVariableChanged_NullMap_NoOp()
    {
        var character = new Character();
        character.SetCoid(9, true);
        TriggerManager.Instance.OnVariableChanged(character, 1);
    }

    [TestMethod]
    public void Variable_CreateForTests_AndRead()
    {
        var v = Variable.CreateForTests(3, 9, 100f, 0f, "done");
        Assert.AreEqual(3, v.Id);
        Assert.AreEqual(9, v.Type);
        Assert.AreEqual(100f, v.Value);

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(5); // id
        w.Write((byte)11); // type
        w.Write(3032f); // value
        w.Write(0f); // initial
        // name 64 utf8
        var name = new byte[64];
        System.Text.Encoding.UTF8.GetBytes("active").CopyTo(name, 0);
        w.Write(name);
        w.Flush();
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        var read = Variable.Read(r, mapVersion: 45);
        Assert.AreEqual(5, read.Id);
        Assert.AreEqual(11, read.Type);
        Assert.AreEqual(3032f, read.Value);
    }

    [TestMethod]
    public void Trigger_CanTrigger_UnownedVehicleRequiresVehiclesType()
    {
        var (character, vehicle, map) = CreatePlayer();
        vehicle.SetOwner(null);
        var tpl = new TriggerTemplate
        {
            COID = 8,
            TargetType = TriggerTargetType.Players,
            Scale = 20f,
            ActivationCount = -1,
        };
        var trigger = new Trigger(tpl);
        trigger.SetCoid(8, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 20f;
        trigger.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        Assert.IsFalse(trigger.CanTrigger(vehicle));
        tpl.TargetType = TriggerTargetType.Vehicles;
        Assert.IsTrue(trigger.CanTrigger(vehicle));
    }

    [TestMethod]
    public void Trigger_CanTrigger_CreatureTarget()
    {
        var (_, _, map) = CreatePlayer();
        var creature = new Creature();
        creature.SetCoid(200, false);
        creature.Position = new Vector3(0, 0, 0);
        creature.SetMap(map);

        var tpl = new TriggerTemplate
        {
            COID = 9,
            TargetType = TriggerTargetType.Creatures,
            Scale = 10f,
            ActivationCount = -1,
        };
        var trigger = new Trigger(tpl);
        trigger.SetCoid(9, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 10f;
        trigger.SetMap(map);

        Assert.IsTrue(trigger.CanTrigger(creature));
        tpl.TargetType = TriggerTargetType.Players;
        Assert.IsFalse(trigger.CanTrigger(creature));
    }

    [TestMethod]
    public void TriggerManager_FireFailsWhenConditionsFail()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Variables[1] = Variable.CreateForTests(1, LogicVariableStore.TypeConstant, 0f, 0f);
        map.MapData.Variables[2] = Variable.CreateForTests(2, LogicVariableStore.TypeConstant, 0f, 1f);
        character.EnsureLogicVariables();

        var tpl = new TriggerTemplate
        {
            COID = 10,
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            ActivationCount = -1,
            AllConditionsNeeded = true,
        };
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = 1,
            RightId = 2,
            Type = ConditionalType.EqualTo, // 0 == 1 false
        });
        var trigger = new Trigger(tpl);
        trigger.SetCoid(10, false);
        trigger.SetMap(map);

        TriggerManager.Instance.FireTriggerReactions(vehicle, trigger);
        Assert.AreEqual(0, trigger.FireCount);
    }

    [TestMethod]
    public void TriggerManager_RemoteWatchVarFilter_SkipsUnrelated()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Variables[30] = Variable.CreateForTests(30, LogicVariableStore.TypeConstant, 0f, 0f);
        map.MapData.Variables[31] = Variable.CreateForTests(31, LogicVariableStore.TypeConstant, 0f, 1f);
        character.EnsureLogicVariables().Set(30, 1f);

        const long reactionCoid = 97200;
        const long objCoid = 97201;
        var delTpl = new ReactionTemplate { COID = (int)reactionCoid, ReactionType = ReactionType.Delete };
        delTpl.Objects.Add(objCoid);
        var del = new Reaction(delTpl);
        del.SetCoid(reactionCoid, false);
        del.SetMap(map);
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(objCoid, false);
        obj.SetMap(map);

        var tpl = new TriggerTemplate
        {
            COID = 11,
            TargetType = TriggerTargetType.Players,
            Scale = 1f,
            DoCollision = false,
            DoConditionals = true,
            ActivationCount = -1,
            AllConditionsNeeded = true,
        };
        tpl.Reactions.Add(reactionCoid);
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = 30,
            RightId = 31,
            Type = ConditionalType.EqualTo,
        });
        var trigger = new Trigger(tpl);
        trigger.SetCoid(11, false);
        trigger.Scale = 1f;
        trigger.SetMap(map);

        // Unrelated var change — should not fire
        TriggerManager.Instance.OnVariableChanged(vehicle, 999);
        Assert.IsNotNull(map.GetObjectByCoid(objCoid));

        TriggerManager.Instance.OnVariableChanged(vehicle, 30);
        Assert.IsNull(map.GetObjectByCoid(objCoid));
    }

    [TestMethod]
    public void VariableArithmetic_Reactions_UpdateStore()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Variables[20] = Variable.CreateForTests(20, LogicVariableStore.TypeConstant, 0f, 10f);
        map.MapData.Variables[21] = Variable.CreateForTests(21, LogicVariableStore.TypeConstant, 0f, 3f);
        var store = character.EnsureLogicVariables();

        void Run(ReactionType type)
        {
            var tpl = new ReactionTemplate
            {
                COID = 1000 + (int)type,
                ReactionType = type,
                GenericVar1 = 20,
                GenericVar3 = 21,
            };
            var reaction = new Reaction(tpl);
            reaction.SetCoid(1000 + (int)type, false);
            reaction.SetMap(map);
            Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        }

        store.Set(20, 10f);
        Run(ReactionType.VariableAdd);
        Assert.AreEqual(13f, store.Get(20));

        store.Set(20, 10f);
        Run(ReactionType.VariableSub);
        Assert.AreEqual(7f, store.Get(20));

        store.Set(20, 10f);
        Run(ReactionType.VariableMul);
        Assert.AreEqual(30f, store.Get(20));

        store.Set(20, 10f);
        Run(ReactionType.VariableDiv);
        Assert.AreEqual(10f / 3f, store.Get(20), 0.001f);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_mission_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(150, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(151, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}
