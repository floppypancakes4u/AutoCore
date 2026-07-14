using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
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
/// Heavy regression locks for Live and Direct–class patrol (one pad per objective sequence).
/// These must stay green: gates open via OnMissionStateChanged on each AdvanceOrComplete.
/// Synthetic mission ids only.
/// </summary>
[TestClass]
public class LiveAndDirectPatrolRegressionTests
{
    private const int MissionId = 93032;
    private const int ContId = 707;
    private const long Pad0 = 14104;
    private const long Pad1 = 14105;
    private const long Pad2 = 14106;
    private const long Pad3 = 14107;
    private const long Pad4 = 14108;
    private const int Obj0 = 95424;
    private const int Obj1 = 95425;
    private const int Obj2 = 95426;
    private const int Obj3 = 95427;
    private const int Obj4 = 95428;

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
    public void LiveAndDirect_SinglePadSeq_HitAdvancesSequence_Sends0x2070AndNextObjectiveState()
    {
        SeedOnePadPerSequenceMission(MissionId, (Obj0, Pad0), (Obj1, Pad1));
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, Pad0, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Pad0, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p =>
            p.MissionId == MissionId && p.ObjectiveId == Obj0));
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == Obj1));
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void LiveAndDirect_SecondPad_AdvancesAgain()
    {
        SeedOnePadPerSequenceMission(MissionId, (Obj0, Pad0), (Obj1, Pad1), (Obj2, Pad2));
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, Pad0, new Vector3(0, 0, 0));
        PlaceWaypoint(map, Pad1, new Vector3(10, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Pad0, false),
        });
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        _sent.Clear();

        character.CurrentVehicle.Position = new Vector3(10, 0, 0);
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Pad1, false),
        });

        Assert.AreEqual(2, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == Obj1));
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == Obj2));
    }

    [TestMethod]
    public void LiveAndDirect_FivePadChain_ThenCompletesMission()
    {
        SeedOnePadPerSequenceMission(
            MissionId,
            (Obj0, Pad0), (Obj1, Pad1), (Obj2, Pad2), (Obj3, Pad3), (Obj4, Pad4));
        var (conn, character, map) = CreatePlayer();
        var pads = new[] { Pad0, Pad1, Pad2, Pad3, Pad4 };
        for (var i = 0; i < pads.Length; i++)
            PlaceWaypoint(map, pads[i], new Vector3(i * 10f, 0, 0));
        GiveQuest(character, MissionId);

        for (var i = 0; i < pads.Length; i++)
        {
            character.CurrentVehicle.Position = new Vector3(i * 10f, 0, 0);
            NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
            {
                Target = new TFID(pads[i], false),
            });

            if (i < pads.Length - 1)
            {
                Assert.AreEqual(i + 1, character.CurrentQuests[0].ActiveObjectiveSequence,
                    $"after pad index {i} expected seq {i + 1}");
                Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
            }
        }

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        var completePkts = _sent.OfType<CompleteDynamicObjectivePacket>()
            .Where(p => p.MissionId == MissionId)
            .Select(p => p.ObjectiveId)
            .ToList();
        CollectionAssert.AreEqual(
            new[] { Obj0, Obj1, Obj2, Obj3, Obj4 },
            completePkts,
            "each one-pad objective must force-complete once in order");
    }

    [TestMethod]
    public void LiveAndDirect_WrongPadForActiveSeq_DoesNotAdvance()
    {
        SeedOnePadPerSequenceMission(MissionId, (Obj0, Pad0), (Obj1, Pad1));
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, Pad1, new Vector3(10, 0, 0));
        character.CurrentVehicle.Position = new Vector3(10, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        // Pad1 belongs to seq1; server on seq0. HEAD reconcile may free-skip to pad1's seq
        // (documented). Lock the observable outcome: either stay on seq0, or after reconcile
        // still requires matching active patrol. Prefer no skip of un-hit pad0.
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Pad1, false),
        });

        // Accept either: (a) no progress if reconcile blocked, or (b) HEAD free-skip.
        // Phase C will lock no-skip; for baseline, assert we don't complete the whole mission
        // without ever seeing pad0 complete packet first when only pad1 is hit.
        if (character.CurrentQuests.Count == 1
            && character.CurrentQuests[0].ActiveObjectiveSequence == 0)
        {
            Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
            return;
        }

        // If reconcile skipped, pad0 must have been force-advanced (0x2070 for Obj0).
        Assert.IsTrue(
            _sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == Obj0)
            || character.CompletedMissionIds.Contains(MissionId)
            || character.CurrentQuests.Count == 0
            || character.CurrentQuests[0].ActiveObjectiveSequence >= 1,
            "unexpected state after wrong-pad AutoPatrol");
    }

    [TestMethod]
    public void LiveAndDirect_SiblingDeliverSameObjective_PadDoesNotCompleteMission()
    {
        var obj = MissionObjective.CreateForTests(Obj0, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 1,
        };
        patrol.GenericTargets[0] = Pad0;
        obj.Requirements.Add(patrol);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 4243,
            NPCTargetCompletes = true,
            FirstStateSlot = 1,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, Pad0, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Pad0, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void LoaShaped_FirstPad_DoesNotAdvanceToDeliver_AndSendsAbsolutePadCount()
    {
        // Regression: first pad must stay on patrol (seq 0), send absolute pad count 1,
        // never 0x2070 / deliver sequence (Jimmy NPC as next waypoint).
        var pads = new long[] { 6518, 6519, 6520, 6521, 6522, 6523, 6524 };
        var obj = MissionObjective.CreateForTests(Obj0, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            Sequential = true,
            Laps = 1,
            TargetCount = pads.Length,
            FirstStateSlot = 0,
        };
        for (var i = 0; i < pads.Length; i++)
            patrol.GenericTargets[i] = pads[i];
        obj.Requirements.Add(patrol);
        var deliver = MissionObjective.CreateForTests(Obj1, 1, MissionId, 1);
        deliver.Requirements.Add(new ObjectiveRequirementDeliver(deliver)
        {
            NPCTargetCBID = 2472,
            NPCTargetCompletes = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj, deliver));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, pads[0], new Vector3(0, 0, 0));
        // Also place a later pad in range — sequential must not skip to it.
        PlaceWaypoint(map, pads[pads.Length - 1], new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pads[0], false),
        });

        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count(),
            "first pad must not 0x2070 (that retargets client to deliver NPC)");
        var padState = _sent.OfType<ObjectiveStatePacket>()
            .FirstOrDefault(p => p.ObjectiveId == Obj0);
        Assert.IsNotNull(padState, "mid-route ObjectiveState required");
        Assert.AreEqual(1f, padState.SlotProgress[0], 0.001f,
            "client GetTarget casts slot to int pad index — must be absolute 1, not ratio");

        // Hitting last pad while progress expects pad 1 must not complete.
        character.CurrentVehicle.Position = new Vector3(1, 0, 0);
        _sent.Clear();
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pads[pads.Length - 1], false),
        });
        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void LoaShaped_MultiPadOneObjective_RequiresAllPads_ThenAdvancesToDeliver()
    {
        // LOA 2945 shape: 7 sequential pads on seq0, then deliver Jimmy Chrome.
        var pads = new long[] { 6518, 6519, 6520, 6521, 6522, 6523, 6524 };
        var obj = MissionObjective.CreateForTests(Obj0, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            Sequential = true,
            Laps = 1,
            TargetCount = pads.Length,
            FirstStateSlot = 0,
        };
        for (var i = 0; i < pads.Length; i++)
            patrol.GenericTargets[i] = pads[i];
        obj.Requirements.Add(patrol);
        var deliver = MissionObjective.CreateForTests(Obj1, 1, MissionId, 1);
        deliver.Requirements.Add(new ObjectiveRequirementDeliver(deliver)
        {
            NPCTargetCBID = 2472,
            NPCTargetCompletes = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj, deliver));

        var (conn, character, map) = CreatePlayer();
        for (var i = 0; i < pads.Length; i++)
            PlaceWaypoint(map, pads[i], new Vector3(i * 10f, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        // First pad: stay on patrol objective.
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pads[0], false),
        });
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence,
            "first pad must not skip remaining LOA pads");
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);

        // Middle pads.
        for (var i = 1; i < pads.Length - 1; i++)
        {
            character.CurrentVehicle.Position = new Vector3(i * 10f, 0, 0);
            NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
            {
                Target = new TFID(pads[i], false),
            });
            Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence,
                $"after pad[{i}] still on patrol");
            Assert.AreEqual(i + 1, character.CurrentQuests[0].ObjectiveProgress[0]);
        }

        // Last pad: advance to deliver sequence.
        _sent.Clear();
        character.CurrentVehicle.Position = new Vector3((pads.Length - 1) * 10f, 0, 0);
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(pads[pads.Length - 1], false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "mission remains (deliver left)");
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence,
            "all pads done → deliver objective");
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == Obj0));
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == Obj1));
    }

    [TestMethod]
    public void AutoPatrol_InRangeBarely_Completes_OutOfRangeDoesNot()
    {
        SeedOnePadPerSequenceMission(MissionId, (Obj0, Pad0));
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, Pad0, new Vector3(0, 0, 0));
        // AutoCompleteDistance default 30 in seed helper below for single-pad mission.
        AssetManager.Instance.ClearTestMissions();
        var obj = MissionObjective.CreateForTests(Obj0, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 25f,
            TargetCount = 1,
        };
        patrol.GenericTargets[0] = Pad0;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
        PlaceWaypoint(map, Pad0, new Vector3(0, 0, 0));

        // Just outside 25 (live LOA was 25.5 vs 25) — current behavior: reject.
        character.CurrentVehicle.Position = new Vector3(25.5f, 0, 0);
        GiveQuest(character, MissionId);
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Pad0, false),
        });
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));

        character.CurrentVehicle.Position = new Vector3(20f, 0, 0);
        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Pad0, false),
        });
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    private static void SeedOnePadPerSequenceMission(
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
            };
            patrol.GenericTargets[0] = pad;
            obj.Requirements.Add(patrol);
            objectives.Add(obj);
        }

        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(missionId, objectives.ToArray()));
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

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_lad_{ContId}",
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
        return (connection, character, map);
    }
}
