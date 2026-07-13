using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// AutoPatrol (0x20B3) C2S: progress active AutoComplete patrol objectives.
/// Synthetic mission ids only.
/// </summary>
[TestClass]
public class AutoPatrolTests
{
    private const int MissionId = 91200;
    private const int ObjectiveIdA = 92200;
    private const int ObjectiveIdB = 92201;
    private const long WaypointCoid = 98001;
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
    public void AutoPatrolPacket_Read_PadAndTfid()
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(0); // pad
        w.Write(WaypointCoid);
        w.Write(false);
        w.Write(new byte[7]);
        w.Flush();
        ms.Position = 0;

        using var r = new BinaryReader(ms);
        var packet = new AutoPatrolPacket();
        packet.Read(r);

        Assert.AreEqual(GameOpcode.AutoPatrol, packet.Opcode);
        Assert.AreEqual(WaypointCoid, packet.Target.Coid);
        Assert.IsFalse(packet.Target.Global);
    }

    [TestMethod]
    public void HandleAutoPatrol_InRange_CompletesSingleObjectiveMission()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(10, 0, 0));
        character.CurrentVehicle.Position = new Vector3(10, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveIdA));
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void HandleAutoPatrol_InRange_AdvancesToNextObjective()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: ObjectiveIdB);
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(5, 0, 0));
        character.CurrentVehicle.Position = new Vector3(5, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveIdA));
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjectiveIdB));
    }

    [TestMethod]
    public void HandleAutoPatrol_OutOfRange_NoProgress()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 10f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(100, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_UnknownTarget_NoProgress()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 50f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(99999, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_NoActivePatrol_NoProgress()
    {
        // Mission with no patrol requirement
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(MissionId, MissionObjective.CreateForTests(ObjectiveIdA, 0, MissionId, 1)));
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void HandleAutoPatrol_NullGuards_NoThrow()
    {
        NpcInteractHandler.HandleAutoPatrol(null, new AutoPatrolPacket());
        var (conn, _, _) = CreatePlayer();
        NpcInteractHandler.HandleAutoPatrol(conn, null);
    }

    [TestMethod]
    public void HandleAutoPatrol_Town_UsesCharacterFootPosition_Completes()
    {
        // Town: character on foot at waypoint; vehicle parked far (garage/entry).
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer(isTown: true);
        PlaceWaypoint(map, WaypointCoid, new Vector3(5, 0, 0));
        character.Position = new Vector3(5, 0, 0);
        character.CurrentVehicle.Position = new Vector3(500, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count,
            "Town AutoPatrol must use character foot pose, not parked vehicle.");
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveIdA));
    }

    [TestMethod]
    public void HandleAutoPatrol_Field_UsesVehiclePosition_Completes()
    {
        // Field: vehicle at waypoint; character body pose stale/far.
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer(isTown: false);
        PlaceWaypoint(map, WaypointCoid, new Vector3(5, 0, 0));
        character.Position = new Vector3(500, 0, 0);
        character.CurrentVehicle.Position = new Vector3(5, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count,
            "Field AutoPatrol must use vehicle chassis pose, not character body.");
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void HandleAutoPatrol_Town_CharacterFar_VehicleNear_NoProgress()
    {
        // Town: vehicle sitting on waypoint must not complete while avatar is far.
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer(isTown: true);
        PlaceWaypoint(map, WaypointCoid, new Vector3(5, 0, 0));
        character.Position = new Vector3(500, 0, 0);
        character.CurrentVehicle.Position = new Vector3(5, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count,
            "Town AutoPatrol must not credit vehicle pose when character is far.");
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_NoMap_NoProgress()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, _) = CreatePlayer();
        character.SetMap(null);
        character.CurrentVehicle.SetMap(null);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_InvalidTargetCoid_NoProgress()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(0, false),
        });
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(-1, false),
        });
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = null,
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_AlreadyCompletedMissionInList_Skips()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        character.CompletedMissionIds.Add(MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "Completed-id skip must not re-complete/remove quest");
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_UnknownMissionOrBadSequence_NoProgress()
    {
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        // No AssetManager mission for this id.
        character.CurrentQuests.Add(new CharacterQuest(999001, 0));

        // Mission exists but sequence is out of range.
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var badSeq = new CharacterQuest(MissionId, 99);
        character.CurrentQuests.Add(badSeq);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(2, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_ListedTargetMissingFromWorld_NoProgress()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 50f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        // Do not PlaceWaypoint — TryGetWorldPosition must fail.
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_SiblingDeliver_SkipsInvalidDeliverEntries()
    {
        // One blocking deliver (valid) plus invalid sibling rows must not throw / complete.
        var obj = MissionObjective.CreateForTests(ObjectiveIdA, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = WaypointCoid;
        obj.Requirements.Add(patrol);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 0, // invalid — continue branch
            NPCTargetCompletes = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 4242,
            NPCTargetCompletes = false, // invalid — continue branch
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 4243,
            NPCTargetCompletes = true, // blocking valid deliver
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "Sibling deliver must block mission complete");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_Town_NoVehicle_UsesCharacterPosition()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer(isTown: true);
        PlaceWaypoint(map, WaypointCoid, new Vector3(5, 0, 0));
        character.Position = new Vector3(5, 0, 0);
        character.SetCurrentVehicleForTests(null);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count,
            "Town on-foot with no vehicle must still complete via character pose.");
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void HandleAutoPatrol_DefaultRadius_WhenAutoCompleteDistanceZero()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 0f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        // Default radius is 25f — place just inside.
        PlaceWaypoint(map, WaypointCoid, new Vector3(20, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count, "Zero AutoCompleteDistance falls back to 25u");
    }

    private static void SeedPatrolMission(
        int missionId,
        int objectiveId,
        long waypointCoid,
        float autoCompleteDist,
        int? nextObjectiveId)
    {
        var objA = MissionObjective.CreateForTests(objectiveId, 0, missionId, 1);
        var patrol = new ObjectiveRequirementPatrol(objA)
        {
            AutoComplete = true,
            AutoCompleteDistance = autoCompleteDist,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = waypointCoid;
        // TargetCount is private-set via UnSerialize; force via reflection-free path:
        // PatrolListsTarget uses Max(TargetCount,0) and falls back to array scan when 0 —
        // set TargetCount by re-adding through the public field isn't possible.
        // Use the array: TargetCount defaults 0 → code uses GenericTargets.Length.
        // Our loop checks GenericTargets[i] == coid for i < length when count==0 after Max...
        // Actually: count = Max(0,0)=0 then count = GenericTargets.Length. Good.
        objA.Requirements.Add(patrol);

        if (nextObjectiveId.HasValue)
        {
            var objB = MissionObjective.CreateForTests(nextObjectiveId.Value, 1, missionId, 1);
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, objA, objB));
        }
        else
        {
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, objA));
        }
    }

    private static void GiveQuest(Character character, int missionId)
    {
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void PlaceWaypoint(SectorMap map, long coid, Vector3 position)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = position;
        obj.SetMap(map);
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer(bool isTown = false)
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_mission_{ContId}",
            DisplayName = "test",
            IsTown = isTown,
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
        return (connection, character, map);
    }
}
