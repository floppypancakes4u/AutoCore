using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

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
}
