using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory;
using AutoCore.Game.TNL;

/// <summary>
/// Shared setup for heavy mission/sequence regression suites. Synthetic ids only.
/// </summary>
public sealed class MissionHeavyRegressionFixture : IDisposable
{
    public const int ContId = 693;
    public List<BasePacket> Sent { get; } = new();

    public MissionHeavyRegressionFixture()
    {
        Sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => Sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        MissionPersistence.Instance.ResetPersistenceForTests();
        MissionPersistence.Instance.AutoFlushOnEnqueue = false;
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
    }

    public void Dispose()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        MissionPersistence.Instance.ResetPersistenceForTests();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        Sent.Clear();
    }

    public int FlushPersist() => MissionPersistence.Instance.FlushPending();

    public (TNLConnection Conn, Character Character, SectorMap Map, Vehicle Vehicle) CreatePlayer(
        long charCoid = 18000,
        long vehicleCoid = 18001,
        bool isTown = false)
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_heavy_{ContId}",
            DisplayName = "heavy-test",
            IsTown = isTown,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(charCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = new Vector3(0, 0, 0);
        return (connection, character, map, vehicle);
    }

    public static void PlaceWaypoint(SectorMap map, long coid, Vector3 position)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = position;
        obj.SetMap(map);
    }

    public static void GiveQuest(Character character, int missionId, byte sequence = 0)
    {
        var quest = new CharacterQuest(missionId, sequence);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    /// <summary>Live-and-Direct shape: one pad per objective sequence.</summary>
    public void SeedOnePadPerSequence(
        int missionId,
        params (int ObjectiveId, long Pad)[] steps)
    {
        var objectives = new List<MissionObjective>();
        for (var i = 0; i < steps.Length; i++)
        {
            var (objId, pad) = steps[i];
            var obj = MissionObjective.CreateForTests(objId, (byte)i, missionId, 1);
            var patrol = new ObjectiveRequirementPatrol(obj)
            {
                AutoComplete = true,
                AutoCompleteDistance = 30f,
                TargetCount = 1,
                Sequential = true,
                Laps = 1,
                FirstStateSlot = 0,
            };
            patrol.GenericTargets[0] = pad;
            obj.Requirements.Add(patrol);
            objectives.Add(obj);
        }

        AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, objectives.ToArray()));
    }

    /// <summary>LOA shape: multi-pad sequential patrol then optional deliver.</summary>
    public void SeedLoaShaped(
        int missionId,
        int patrolObjId,
        int deliverObjId,
        long[] pads,
        int deliverNpcCbid = 2472,
        bool sequential = true,
        int laps = 1)
    {
        var patrolObj = MissionObjective.CreateForTests(patrolObjId, 0, missionId, 1);
        var patrol = new ObjectiveRequirementPatrol(patrolObj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            Sequential = sequential,
            Laps = laps,
            TargetCount = pads.Length,
            FirstStateSlot = 0,
        };
        for (var i = 0; i < pads.Length; i++)
            patrol.GenericTargets[i] = pads[i];
        patrolObj.Requirements.Add(patrol);

        var deliverObj = MissionObjective.CreateForTests(deliverObjId, 1, missionId, 1);
        deliverObj.Requirements.Add(new ObjectiveRequirementDeliver(deliverObj)
        {
            NPCTargetCBID = deliverNpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(missionId, patrolObj, deliverObj));
    }

    public static ObjectiveRequirementPatrol MakePatrol(
        MissionObjective owner,
        bool sequential,
        int laps,
        params long[] targets)
    {
        var patrol = new ObjectiveRequirementPatrol(owner)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            Sequential = sequential,
            Laps = laps,
            TargetCount = targets.Length,
            FirstStateSlot = 0,
        };
        for (var i = 0; i < targets.Length; i++)
            patrol.GenericTargets[i] = targets[i];
        return patrol;
    }

    public void AutoPatrol(TNLConnection conn, long padCoid)
    {
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(padCoid, false),
        });
    }

    public int CountComplete(int objectiveId)
        => Sent.OfType<CompleteDynamicObjectivePacket>().Count(p => p.ObjectiveId == objectiveId);

    public int CountObjectiveState(int objectiveId)
        => Sent.OfType<ObjectiveStatePacket>().Count(p => p.ObjectiveId == objectiveId);

    public ObjectiveStatePacket LastObjectiveState(int objectiveId)
        => Sent.OfType<ObjectiveStatePacket>().LastOrDefault(p => p.ObjectiveId == objectiveId);

    /// <summary>
    /// Attach a recording cargo inventory so mission gear grant/take paths can run.
    /// </summary>
    public InventoryTestHarness AttachInventory(Character character, long? characterCoid = null)
    {
        var harness = new InventoryTestHarness(characterCoid ?? character.ObjectId.Coid);
        character.AttachInventoryForTests(harness.Inventory);
        return harness;
    }

    public static void PlaceWorldObject(SectorMap map, long coid, int cbid = 0, Vector3? position = null)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        if (cbid > 0)
            obj.SetCbidForTests(cbid);
        obj.Position = position ?? new Vector3(0, 0, 0);
        obj.SetMap(map);
    }

    public static UseObjectPacket UsePacket(long targetCoid, int objectiveId = -1) => new()
    {
        Target = new TFID(targetCoid, false),
        ObjectiveId = objectiveId,
    };

    public void UseObject(TNLConnection conn, long targetCoid, int objectiveId = -1)
        => NpcInteractHandler.HandleUseObject(conn, UsePacket(targetCoid, objectiveId));

    public static void GrantMissionCargo(
        Character character,
        int cbid,
        int quantity = 1,
        long? itemCoid = null)
    {
        long coid;
        if (itemCoid.HasValue)
            coid = itemCoid.Value;
        else if (character.Map != null)
            coid = character.Map.LocalCoidCounter++;
        else
            coid = character.ObjectId.Coid + 10_000;

        character.Inventory.GrantMissionCargoItem(
            cbid,
            CloneBaseObjectType.QuestObject,
            $"mission_{cbid}",
            coid,
            character.ObjectId.Coid,
            quantity);
    }

    /// <summary>
    /// Generic multi-site UseItem shape (not retail-specific):
    /// seq0 give multi-use secondary (no destroy), seq1 consume secondary, optional deliver turn-in.
    /// </summary>
    public void SeedMultiSiteUseItem(
        int missionId,
        int obj0Id,
        int obj1Id,
        int? deliverObjId,
        long siteA,
        long siteB,
        int worldPrimaryCbid,
        int secondaryCbid,
        int deliverNpcCbid = 0)
    {
        var o0 = MissionObjective.CreateForTests(obj0Id, 0, missionId, 0);
        o0.Requirements.Add(new ObjectiveRequirementUseItem(o0)
        {
            PrimaryCBID = worldPrimaryCbid,
            PrimaryInWorld = true,
            PrimaryDestroy = true,
            PrimaryExplode = true,
            SecondaryCBID = secondaryCbid,
            SecondaryGiveAtStart = true,
            SecondaryMultipleUse = true,
            SecondaryDestroy = false,
            RepeatCount = 1,
            FirstStateSlot = 0,
        });

        var o1 = MissionObjective.CreateForTests(obj1Id, 1, missionId, 0);
        o1.Requirements.Add(new ObjectiveRequirementUseItem(o1)
        {
            PrimaryCBID = worldPrimaryCbid,
            PrimaryInWorld = true,
            PrimaryDestroy = true,
            PrimaryExplode = true,
            SecondaryCBID = secondaryCbid,
            SecondaryGiveAtStart = false,
            SecondaryMultipleUse = false,
            SecondaryDestroy = true,
            RepeatCount = 1,
            FirstStateSlot = 0,
        });

        if (deliverObjId is int delId && deliverNpcCbid > 0)
        {
            var o2 = MissionObjective.CreateForTests(delId, 2, missionId, 0);
            o2.Requirements.Add(new ObjectiveRequirementDeliver(o2)
            {
                NPCTargetCBID = deliverNpcCbid,
                NPCTargetCompletes = true,
                TakeItemAtEnd = true,
                ItemCBID = -1,
                FirstStateSlot = 0,
            });
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, o0, o1, o2));
        }
        else
        {
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, o0, o1));
        }

        // Site COIDs are placed by the test; CBID matching uses worldPrimaryCbid.
        _ = siteA;
        _ = siteB;
    }

    /// <summary>Single-objective UseItem with configurable requirement flags.</summary>
    public void SeedSingleUseItem(
        int missionId,
        int objectiveId,
        Action<ObjectiveRequirementUseItem> configure)
    {
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, 0);
        var use = new ObjectiveRequirementUseItem(obj)
        {
            RepeatCount = 1,
            FirstStateSlot = 0,
        };
        configure?.Invoke(use);
        obj.Requirements.Add(use);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, obj));
    }
}
