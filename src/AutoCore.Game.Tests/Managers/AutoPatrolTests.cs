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
    private readonly List<string> _diag = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        _diag.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        MissionFlowDiag.TestSink = msg => _diag.Add(msg);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        MissionFlowDiag.TestSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
        _diag.Clear();
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
        Assert.IsFalse(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
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
    public void HandleAutoPatrol_ClientAheadOnPatrol_WhileServerStillOnPriorDeliver_Advances()
    {
        // Track This class: client finished deliver dialog locally; server still seq=0 deliver;
        // AutoPatrol on the next objective's waypoint must reconcile then complete/advance patrol.
        const int deliverObj = ObjectiveIdA;
        const int patrolObj = ObjectiveIdB;
        const int afterPatrolObj = 92202;
        const long pad = WaypointCoid;
        const int deliverNpc = 93400;

        var d0 = MissionObjective.CreateForTests(deliverObj, 0, MissionId, 1);
        d0.Requirements.Add(new ObjectiveRequirementDeliver(d0)
        {
            NPCTargetCBID = deliverNpc,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = false,
        });
        var patrol = MissionObjective.CreateForTests(patrolObj, 1, MissionId, 1);
        var patrolReq = new ObjectiveRequirementPatrol(patrol)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 1,
        };
        patrolReq.GenericTargets[0] = pad;
        patrol.Requirements.Add(patrolReq);
        var d2 = MissionObjective.CreateForTests(afterPatrolObj, 2, MissionId, 1);
        d2.Requirements.Add(new ObjectiveRequirementDeliver(d2)
        {
            NPCTargetCBID = deliverNpc,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, d0, patrol, d2));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, pad, new Vector3(5, 0, 0));
        character.CurrentVehicle.Position = new Vector3(5, 0, 0);
        GiveQuest(character, MissionId);
        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(2, character.CurrentQuests[0].ActiveObjectiveSequence,
            "reconcile deliver → complete patrol → active final deliver");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void HandleAutoPatrol_HeightDelta_StillInXZRange_Completes()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 25f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        // Vehicle high above pad (map-Y mismatch) but same XZ — must still count.
        character.CurrentVehicle.Position = new Vector3(0, 40, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId),
            "AutoPatrol range must use XZ so map height skew does not block pads");
    }

    [TestMethod]
    public void HandleAutoPatrol_UnlistedTargetMissingFromMap_NoProgress()
    {
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(999_888, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_PastPatrolPadWhileOnLaterDeliver_ResyncsClientOnce()
    {
        // Track This: server on final deliver (seq2); client still AutoPatrols finished pad.
        const int deliverObj = ObjectiveIdA;
        const int patrolObj = ObjectiveIdB;
        const int finalDeliverObj = 92202;
        const long pad = WaypointCoid;
        const int deliverNpc = 93400;

        var d0 = MissionObjective.CreateForTests(deliverObj, 0, MissionId, 1);
        d0.Requirements.Add(new ObjectiveRequirementDeliver(d0)
        {
            NPCTargetCBID = deliverNpc,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var patrol = MissionObjective.CreateForTests(patrolObj, 1, MissionId, 1);
        var patrolReq = new ObjectiveRequirementPatrol(patrol)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 1,
        };
        patrolReq.GenericTargets[0] = pad;
        patrol.Requirements.Add(patrolReq);
        var d2 = MissionObjective.CreateForTests(finalDeliverObj, 2, MissionId, 1);
        d2.Requirements.Add(new ObjectiveRequirementDeliver(d2)
        {
            NPCTargetCBID = deliverNpc,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, d0, patrol, d2));

        var (conn, character, map) = CreatePlayer();
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        var quest = new CharacterQuest(MissionId, 2); // already past patrol
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
        character.MapPresence.EnsureContinent(ContId, map.InstanceSerial);
        _sent.Clear();

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad, false),
        });

        Assert.AreEqual(2, character.CurrentQuests[0].ActiveObjectiveSequence,
            "must not re-advance server past final deliver");
        Assert.IsTrue(
            _sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == patrolObj),
            "must force-complete finished patrol so client clears waypoints");
        Assert.IsTrue(
            _sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == finalDeliverObj),
            "must resync active deliver objective");
        Assert.IsTrue(character.MapPresence.HasStalePatrolResync(MissionId));

        _sent.Clear();
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad, false),
        });
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count(),
            "stale patrol resync is one-shot per mission/map");
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
    public void HandleAutoPatrol_ListedTargetMissingFromWorld_TrustsClientAndCompletes()
    {
        // Track This class: GenericTargetCOIDs often absent from continent .fam Templates.
        // Client only sends AutoPatrol when in AutoCompleteDistance — trust that gate.
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, autoCompleteDist: 50f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        // Do not PlaceWaypoint — TryGetWorldPosition fails; still must advance.
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveIdA));
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
    public void HandleAutoPatrol_Town_CharacterFar_VehicleNear_NoProgress()
    {
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
    public void HandleAutoPatrol_MultiWaypoint_FirstPad_DoesNotComplete_SecondPad_Completes()
    {
        const long pad0 = 98010;
        const long pad1 = 98011;
        var objA = MissionObjective.CreateForTests(ObjectiveIdA, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(objA)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 2,
            Sequential = true,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = pad0;
        patrol.GenericTargets[1] = pad1;
        objA.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, pad0, new Vector3(0, 0, 0));
        PlaceWaypoint(map, pad1, new Vector3(10, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad0, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "first pad must not finish the mission");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsTrue(
            _sent.OfType<ObjectiveStatePacket>().Any(p =>
                p.ObjectiveId == ObjectiveIdA && p.SlotProgress[0] >= 1f),
            "mid-route pad count ObjectiveState expected");

        character.CurrentVehicle.Position = new Vector3(10, 0, 0);
        _sent.Clear();
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad1, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveIdA));
    }

    [TestMethod]
    public void HandleAutoPatrol_MultiWaypoint_RehitSamePad_DoesNotAdvance()
    {
        const long pad0 = 98020;
        const long pad1 = 98021;
        var objA = MissionObjective.CreateForTests(ObjectiveIdA, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(objA)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 2,
            Sequential = true,
        };
        patrol.GenericTargets[0] = pad0;
        patrol.GenericTargets[1] = pad1;
        objA.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, pad0, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad0, false),
        });
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);

        _sent.Clear();
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad0, false),
        });

        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0], "re-hit must not double-count");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleAutoPatrol_SinglePad_StillCompletesImmediately()
    {
        // Live and Direct class: one GenericTarget per objective — must not wait for multi-pad.
        SeedPatrolMission(MissionId, ObjectiveIdA, WaypointCoid, 30f, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void HandleAutoPatrol_AlreadyCountedPad_RepeatedPackets_DoNotSpamDiag()
    {
        // Client re-sends AutoPatrol every tick while standing in a pad volume.
        // After the first hit is applied, further identical packets must not flood MISSION-DIAG
        // or re-send mid-route ObjectiveState.
        const long pad0 = 98100;
        const long pad1 = 98101;
        var objA = MissionObjective.CreateForTests(ObjectiveIdA, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(objA)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 2,
            Sequential = true,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = pad0;
        patrol.GenericTargets[1] = pad1;
        objA.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, pad0, new Vector3(0, 0, 0));
        PlaceWaypoint(map, pad1, new Vector3(10, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad0, false),
        });
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        var diagAfterFirst = _diag.Count;
        var stateAfterFirst = _sent.OfType<ObjectiveStatePacket>().Count();

        _diag.Clear();
        _sent.Clear();
        for (var i = 0; i < 50; i++)
        {
            NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
            {
                Target = new TFID(pad0, false),
            });
        }

        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0], "re-hits must not double-count");
        Assert.AreEqual(0, _diag.Count,
            "redundant AutoPatrol ticks must not emit MISSION-DIAG (was " + string.Join(" | ", _diag) + ")");
        Assert.AreEqual(0, _sent.OfType<ObjectiveStatePacket>().Count(),
            "redundant ticks must not re-send ObjectiveState");
        Assert.IsTrue(diagAfterFirst > 0 || stateAfterFirst > 0,
            "sanity: first hit should have produced progress work");
    }

    [TestMethod]
    public void HandleAutoPatrol_NewPadAfterDedupe_StillProgresses()
    {
        const long pad0 = 98110;
        const long pad1 = 98111;
        var objA = MissionObjective.CreateForTests(ObjectiveIdA, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(objA)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 2,
            Sequential = true,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = pad0;
        patrol.GenericTargets[1] = pad1;
        objA.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, pad0, new Vector3(0, 0, 0));
        PlaceWaypoint(map, pad1, new Vector3(10, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad0, false),
        });
        // Spam pad0 so presence dedupe arms.
        for (var i = 0; i < 10; i++)
        {
            NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
            {
                Target = new TFID(pad0, false),
            });
        }

        character.CurrentVehicle.Position = new Vector3(10, 0, 0);
        _diag.Clear();
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad1, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId),
            "next pad must still complete after prior pad was deduped");
    }

    [TestMethod]
    public void HandleAutoPatrol_AfterAdvance_SameCoidOnLaterSeq_StillWorks()
    {
        // Two sequential single-pad objectives that reuse the same waypoint COID.
        // Dedupe key must include seq/progress so advance is not blocked.
        const long pad = 98120;
        var objA = MissionObjective.CreateForTests(ObjectiveIdA, 0, MissionId, 1);
        var patrolA = new ObjectiveRequirementPatrol(objA)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 1,
        };
        patrolA.GenericTargets[0] = pad;
        objA.Requirements.Add(patrolA);

        var objB = MissionObjective.CreateForTests(ObjectiveIdB, 1, MissionId, 1);
        var patrolB = new ObjectiveRequirementPatrol(objB)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 1,
        };
        patrolB.GenericTargets[0] = pad;
        objB.Requirements.Add(patrolB);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA, objB));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, pad, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad, false),
        });
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);

        // Client still spamming finished pad while server is on seq1 with same COID.
        for (var i = 0; i < 5; i++)
        {
            NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
            {
                Target = new TFID(pad, false),
            });
        }

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId),
            "same COID on next sequence must still complete (dedupe must include seq)");
    }

    [TestMethod]
    public void HandleAutoPatrol_StaleNoMatch_RepeatedPackets_DoNotSpamDiag()
    {
        // After one-shot stale resync, further AutoPatrols on the finished pad must stay quiet.
        const int deliverObj = ObjectiveIdA;
        const int patrolObj = ObjectiveIdB;
        const int finalDeliverObj = 92202;
        const long pad = WaypointCoid;
        const int deliverNpc = 93400;

        var d0 = MissionObjective.CreateForTests(deliverObj, 0, MissionId, 1);
        d0.Requirements.Add(new ObjectiveRequirementDeliver(d0)
        {
            NPCTargetCBID = deliverNpc,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var patrol = MissionObjective.CreateForTests(patrolObj, 1, MissionId, 1);
        var patrolReq = new ObjectiveRequirementPatrol(patrol)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 1,
        };
        patrolReq.GenericTargets[0] = pad;
        patrol.Requirements.Add(patrolReq);
        var d2 = MissionObjective.CreateForTests(finalDeliverObj, 2, MissionId, 1);
        d2.Requirements.Add(new ObjectiveRequirementDeliver(d2)
        {
            NPCTargetCBID = deliverNpc,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, d0, patrol, d2));

        var (conn, character, map) = CreatePlayer();
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        var quest = new CharacterQuest(MissionId, 2);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
        character.MapPresence.EnsureContinent(ContId, map.InstanceSerial);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pad, false),
        });
        Assert.IsTrue(character.MapPresence.HasStalePatrolResync(MissionId));

        _diag.Clear();
        _sent.Clear();
        for (var i = 0; i < 40; i++)
        {
            NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
            {
                Target = new TFID(pad, false),
            });
        }

        Assert.AreEqual(0, _diag.Count,
            "stale pad spam after resync must not flood MISSION-DIAG");
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count(),
            "stale pad spam must not re-send CompleteDynamicObjective");
    }

    [TestMethod]
    public void HandleAutoPatrol_SiblingDeliverAlreadyReady_RepeatedPackets_Quiet()
    {
        const int deliverCbid = 4244;
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
            NPCTargetCBID = deliverCbid,
            NPCTargetCompletes = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        // Pretend pad NPC was already set up (client still AutoPatrols every tick).
        character.MapPresence.EnsureContinent(ContId, map.InstanceSerial);
        character.MapPresence.MarkDeliverTurnInReady(deliverCbid);
        // MapHasPresentEntityWithCbidForTests needs a live entity with that CBID.
        var npc = new Creature();
        npc.SetCoid(88_600, false);
        npc.SetCbidForTests(deliverCbid);
        npc.SetMap(map);

        _diag.Clear();
        for (var i = 0; i < 40; i++)
        {
            NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
            {
                Target = new TFID(WaypointCoid, false),
            });
        }

        Assert.AreEqual(1, character.CurrentQuests.Count, "sibling deliver must keep mission active");
        // At most one SIBLING-DELIVER (or zero if already ready on first tick); never linear in N.
        var siblingLogs = _diag.Count(l => l.Contains("SIBLING-DELIVER", StringComparison.Ordinal));
        Assert.IsTrue(siblingLogs <= 1,
            $"expected ≤1 sibling-deliver diag lines, got {siblingLogs}: {string.Join(" | ", _diag)}");
        var autoPatrolDiag = _diag.Count(l => l.Contains("AutoPatrol", StringComparison.Ordinal));
        Assert.IsTrue(autoPatrolDiag <= 1,
            $"expected ≤1 AutoPatrol diag after ready, got {autoPatrolDiag}");
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
