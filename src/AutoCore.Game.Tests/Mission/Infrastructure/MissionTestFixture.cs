using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Infrastructure;

using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Experience;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Shared, deterministic setup for mission tests: packet sink, asset missions, persistence,
/// XP service, and soft-pedal delay. Prefer this over ad-hoc per-class initialization.
/// </summary>
public sealed class MissionTestFixture : IDisposable
{
    public List<BasePacket> Sent { get; } = new();

    public List<(long Coid, int MissionId, QuestPersistKind Kind, byte Seq)> PersistWrites { get; } = new();

    private long _nextCoid = 700_000;

    public MissionTestFixture()
    {
        Sent.Clear();
        PersistWrites.Clear();
        TNLConnection.TestPacketSink = (_, packet) => Sent.Add(packet);
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
        TriggerManager.Instance.ClearAllForTests();
        MissionPersistence.Instance.ResetPersistenceForTests();
        MissionPersistence.Instance.AutoFlushOnEnqueue = false;
        MissionPersistence.Instance.PersistQuestRow = (coid, missionId, op) =>
            PersistWrites.Add((coid, missionId, op.Kind, op.ActiveObjectiveSequence));
        ExperienceService.Instance.ResetForTests();
        ExperienceService.Instance.PersistOnGrant = false;
        ExperienceService.Instance.SendPacketsOnGrant = false;
        SectorMap.SendGroupReactionCall = true;
    }

    public void Dispose()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        TriggerManager.Instance.ClearAllForTests();
        MissionPersistence.Instance.ResetPersistenceForTests();
        ExperienceService.Instance.ResetForTests();
        SectorMap.SendGroupReactionCall = true;
        Sent.Clear();
        PersistWrites.Clear();
    }

    public long NextCoid() => Interlocked.Increment(ref _nextCoid);

    public void SeedMission(int missionId, short isRepeatable = 0, params MissionObjective[] objectives)
    {
        var mission = Mission.CreateForTests(missionId, objectives);
        mission.IsRepeatable = isRepeatable;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }

    public MissionObjective CreateSimpleObjective(int objectiveId, byte sequence, int missionId, int completeCount = 1)
        => MissionObjective.CreateForTests(objectiveId, sequence, missionId, completeCount);

    public MissionObjective CreateKillObjective(
        int objectiveId,
        byte sequence,
        int missionId,
        int targetCbid,
        int numToKill = 1)
    {
        var obj = MissionObjective.CreateForTests(objectiveId, sequence, missionId, Math.Max(1, numToKill));
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = targetCbid,
            NumToKill = numToKill,
        });
        return obj;
    }

    public PlayerMissionContext CreatePlayer(int continentId = 707, long? characterCoid = null, long? vehicleCoid = null)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_mission_fix_{continentId}",
            DisplayName = "mission-test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(characterCoid ?? NextCoid(), true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid ?? NextCoid(), true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);

        return new PlayerMissionContext(connection, character, vehicle, map);
    }

    public Reaction PlaceReaction(SectorMap map, long coid, ReactionType type, int genericVar1)
    {
        var template = new ReactionTemplate
        {
            COID = (int)coid,
            ReactionType = type,
            GenericVar1 = genericVar1,
        };
        var reaction = new Reaction(template);
        reaction.SetCoid(coid, false);
        reaction.SetMap(map);
        return reaction;
    }

    public GraphicsObject PlaceKillTarget(SectorMap map, long coid, int cbid)
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.InitializeHealthForTests(15);
        prop.SetCbidForTests(cbid);
        prop.SetCoid(coid, false);
        prop.SetInvincible(false);
        prop.SetMap(map);
        return prop;
    }

    public void GiveQuest(Character character, int missionId, byte sequence = 0)
    {
        var quest = new CharacterQuest(missionId, sequence);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    public void LoadFromRows(
        Character character,
        IEnumerable<CharacterQuestData> active,
        IEnumerable<CharacterCompletedMissionData> completed)
    {
        character.SetMissionsForTests(active, completed);
    }

    public int CountPackets<T>() where T : BasePacket => Sent.OfType<T>().Count();

    /// <summary>
    /// Drain the mission persistence queue into <see cref="PersistWrites"/>.
    /// Call before asserting persist side effects when AutoFlushOnEnqueue is false.
    /// </summary>
    public int FlushPersist() => MissionPersistence.Instance.FlushPending();
}

public sealed class PlayerMissionContext
{
    public TNLConnection Connection { get; }
    public Character Character { get; }
    public Vehicle Vehicle { get; }
    public SectorMap Map { get; }

    public PlayerMissionContext(TNLConnection connection, Character character, Vehicle vehicle, SectorMap map)
    {
        Connection = connection;
        Character = character;
        Vehicle = vehicle;
        Map = map;
    }
}
