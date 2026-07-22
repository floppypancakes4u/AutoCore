using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Mission.Read applies GLM XML during WAD load. If GLM is not ready yet, deliver
/// TargetNPCCBID never attaches — Rogers (New Day) UseObject then finds no dialog.
/// </summary>
[TestClass]
public class MissionGlmXmlApplyTests
{
    private const int MissionId = 91101;
    private const int ObjectiveId = 92101;
    private const int NpcCbid = 93101;
    private const long NpcCoid = 94101;
    private const int ContinentId = 707;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void ApplyGlmXml_AttachesDeliverTarget_EnablesRogersStyleUseObject()
    {
        // Simulate WAD-before-GLM: binary objective exists, requirements empty.
        var objective = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 0);
        var mission = Mission.CreateForTests(MissionId, objective);
        mission.NPC = -1;
        mission.Name = "h_test_rogers_newday";
        mission.Continent = ContinentId;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveId,
        });
        Assert.AreEqual(0, _sent.OfType<NpcMissionDialogPacket>().Count(),
            "Without GLM deliver target, UseObject must not open turn-in.");

        _sent.Clear();
        var xml = XDocument.Parse($"""
            <Mission name="{mission.Name}" ID="{MissionId}">
              <Title>New Day</Title>
              <Objective name="obj" map="obj" ID="{ObjectiveId}" sequence="0">
                <Requirement type="deliver" slot="0">
                  <CBIDItem>-1</CBIDItem>
                  <TargetNPCCBID>{NpcCbid}</TargetNPCCBID>
                  <ContinentID>{ContinentId}</ContinentID>
                  <NPCTargetCompletes>1</NPCTargetCompletes>
                </Requirement>
              </Objective>
            </Mission>
            """);

        Assert.IsTrue(mission.ApplyGlmXml(xml.Root), "ApplyGlmXml must attach deliver requirement.");
        Assert.IsTrue(
            objective.Requirements.OfType<ObjectiveRequirementDeliver>()
                .Any(d => d.NPCTargetCBID == NpcCbid && d.NPCTargetCompletes),
            "Deliver TargetNPCCBID must come from GLM XML.");

        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveId,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog, "After GLM XML apply, Rogers-style deliver UseObject must open dialog.");
        CollectionAssert.Contains(dialog.MissionIds, MissionId);
    }

    private static void GiveQuest(Character character, int missionId)
    {
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContinentId,
            MapFileName = $"tm_mission_{ContinentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var map = CreateMap();
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

    private static void PlaceNpc(SectorMap map, long coid, int cbid, Vector3 position)
    {
        var npc = new Creature();
        npc.SetCoid(coid, false);
        npc.SetCbidForTests(cbid);
        npc.Position = position;
        npc.SetMap(map);
    }
}
