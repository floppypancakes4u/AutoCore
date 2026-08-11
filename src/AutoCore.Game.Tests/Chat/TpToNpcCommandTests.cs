using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Database.World.Models;
using AutoCore.Game.Chat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Utils.Logging;

/// <summary>
/// /tptonpc — GM teleport to a live object by clonebase id (same-map or cross-map).
/// </summary>
[TestClass]
public class TpToNpcCommandTests
{
    private const int NpcCbid = 44001;
    private const long NpcCoid = 44010;

    private InMemoryLogSink _sink = null!;

    [TestInitialize]
    public void Init()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
        MapManager.Instance.ClearMapsForTests();
        MapManager.Instance.ResolveMapForTests = null;
        MapManager.Instance.SuppressCreatePacketsForTests = true;
    }

    [TestCleanup]
    public void Cleanup()
    {
        MapManager.Instance.SuppressCreatePacketsForTests = false;
        MapManager.Instance.ResolveMapForTests = null;
        MapManager.Instance.ClearMapsForTests();
        GameLog.ResetForTests();
        LogContext.ClearForTests();
    }

    [TestMethod]
    public void TpToNpc_IsMutatingCommand()
    {
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/tptonpc"));
    }

    [TestMethod]
    public void TpToNpc_GmLevel0_Denied()
    {
        var (character, vehicle, map) = CreatePlayer(gmLevel: 0);
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(50f, 1f, 60f));
        vehicle.Position = new Vector3(1f, 0f, 1f);
        character.Position = vehicle.Position;
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(character, $"/tptonpc {NpcCbid}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Permission denied");
        Assert.AreEqual(1f, vehicle.Position.X, 0.01f);
    }

    [TestMethod]
    public void TpToNpc_Usage_WhenArgsMissing()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 1);
        var result = ChatCommandService.Instance.Execute(character, "/tptonpc");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Usage:");
    }

    [TestMethod]
    public void TpToNpc_MissingEverywhere_ReturnsError()
    {
        var (character, _, map) = CreatePlayer(gmLevel: 1);
        MapManager.Instance.RegisterMapForTests(map);

        var result = ChatCommandService.Instance.Execute(character, $"/tptonpc {NpcCbid}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "not found");
    }

    [TestMethod]
    public void TpToNpc_Found_TeleportsAndSendsPacket()
    {
        var (character, vehicle, map) = CreatePlayer(gmLevel: 1);
        MapManager.Instance.RegisterMapForTests(map);
        var dest = new Vector3(88f, 3f, -12f);
        PlaceNpc(map, NpcCoid, NpcCbid, dest);

        var result = ChatCommandService.Instance.Execute(character, $"/tptonpc {NpcCbid}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Teleported");
        StringAssert.Contains(result.Message, NpcCbid.ToString());
        var tp = result.Packets.OfType<TeleportCharacterPacket>().SingleOrDefault();
        Assert.IsNotNull(tp);
        Assert.AreEqual(dest.X, tp.Position.X, 0.01f);
        Assert.AreEqual(dest.X, vehicle.Position.X, 0.01f);
        Assert.AreEqual(dest.Z, vehicle.Position.Z, 0.01f);
    }

    [TestMethod]
    public void TpToNpc_CrossMap_TransfersToContinentAtNpcPose()
    {
        const int homeContinent = 8901;
        const int destContinent = 8902;

        var homeMap = CreateContinentMap(homeContinent, "tptonpc-home");
        var destMap = CreateContinentMap(destContinent, "tptonpc-dest");
        MapManager.Instance.RegisterMapForTests(homeMap);
        MapManager.Instance.RegisterMapForTests(destMap);

        var (character, vehicle, _) = CreatePlayerOnMap(gmLevel: 1, homeMap, new Vector3(1f, 0f, 1f));
        var npcPos = new Vector3(333f, 5f, 444f);
        PlaceNpc(destMap, NpcCoid, NpcCbid, npcPos);

        var result = ChatCommandService.Instance.Execute(character, $"/tptonpc {NpcCbid}");

        Assert.IsTrue(result.Handled);
        Assert.IsFalse(result.Message.Contains("Failed", StringComparison.OrdinalIgnoreCase), result.Message);
        Assert.AreSame(destMap, character.Map);
        Assert.AreSame(destMap, vehicle.Map);
        Assert.AreEqual(npcPos.X, character.Position.X, 0.01f);
        Assert.AreEqual(npcPos.Z, character.Position.Z, 0.01f);
        Assert.AreEqual(npcPos.X, vehicle.Position.X, 0.01f);
        Assert.AreEqual(npcPos.Z, vehicle.Position.Z, 0.01f);
        Assert.AreEqual(0, result.Packets.OfType<TeleportCharacterPacket>().Count(),
            "cross-map transfer uses MapInfo/ghosting; must not also send same-map TeleportCharacter");
        StringAssert.Contains(result.Message, "Transferred");
        StringAssert.Contains(result.Message, destContinent.ToString());
        StringAssert.Contains(result.Message, NpcCbid.ToString());
    }

    static void PlaceNpc(SectorMap map, long coid, int cbid, Vector3 position)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.SetCbidForTests(cbid);
        obj.Position = position;
        obj.SetMap(map);
    }

    static SectorMap CreateContinentMap(int continentId, string label) =>
        SectorMap.CreateForTests(
            new ContinentObject
            {
                Id = continentId,
                MapFileName = $"tm_tptonpc_{continentId}_{label}",
                DisplayName = label,
                IsTown = false,
                IsPersistent = true,
            },
            new Vector4(0, 0, 0, 0));

    static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer(int gmLevel)
        => CreatePlayerOnMap(
            gmLevel,
            CreateContinentMap(707, "tp-npc-test"),
            new Vector3(0, 0, 0));

    static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayerOnMap(
        int gmLevel,
        SectorMap map,
        Vector3 position)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SuppressCreatePacketsForTests = true;

        var character = new Character();
        character.SetCoid(910101, true);
        character.GMLevel = (byte)gmLevel;
        character.AttachTestDataForTests("TpNpcTester");
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(910102, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = position;
        character.Position = position;
        return (character, vehicle, map);
    }
}
