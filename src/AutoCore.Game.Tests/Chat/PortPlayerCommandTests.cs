using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Database.World.Models;
using AutoCore.Game.Chat;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Utils.Logging;

/// <summary>
/// /portto and /porttome — GM teleport to / summon a fuzzy-matched online player.
/// </summary>
[TestClass]
public class PortPlayerCommandTests
{
    private const long AdminCoid = 9_200_001_001L;
    private const long AdminVehicleCoid = 9_200_001_002L;
    private const long TargetCoid = 9_200_001_003L;
    private const long TargetVehicleCoid = 9_200_001_004L;
    private const long OtherCoid = 9_200_001_005L;
    private const long OtherVehicleCoid = 9_200_001_006L;

    private InMemoryLogSink _sink = null!;
    private readonly List<BasePacketCapture> _sent = new();

    private sealed class BasePacketCapture
    {
        public TNLConnection Connection { get; init; } = null!;
        public AutoCore.Game.Packets.BasePacket Packet { get; init; } = null!;
    }

    [TestInitialize]
    public void Init()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
        _sent.Clear();
        TNLConnection.TestPacketSink = (conn, p) => _sent.Add(new BasePacketCapture
        {
            Connection = conn,
            Packet = p,
        });
        MapManager.Instance.ClearMapsForTests();
        MapManager.Instance.ResolveMapForTests = null;
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        PlayerPortService.Instance.ResetForTests();
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlayerPortService.Instance.ResetForTests();
        MapManager.Instance.SuppressCreatePacketsForTests = false;
        MapManager.Instance.ResolveMapForTests = null;
        MapManager.Instance.ClearMapsForTests();
        TNLConnection.TestPacketSink = null;
        GameLog.ResetForTests();
        LogContext.ClearForTests();
    }

    [TestMethod]
    public void PortCommands_AreMutating()
    {
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/portto"));
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/porttome"));
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/portTo"));
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/portToMe"));
    }

    [TestMethod]
    public void PortTo_GmLevel0_Denied_DoesNotMove()
    {
        var adminMap = CreateMap(7001, "admin");
        var (admin, adminVeh, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 0, adminMap, new Vector3(1, 0, 1));
        var (target, _, _) = CreatePlayer(TargetCoid, TargetVehicleCoid, "Bobby", gmLevel: 0, adminMap, new Vector3(100, 5, 200));
        WireOnline(admin, target);
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(admin, "/portto Bobby");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Permission denied");
        Assert.AreEqual(1f, adminVeh.Position.X, 0.01f);
        Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandDenied"));
    }

    [TestMethod]
    public void PortTo_Usage_WhenNoName()
    {
        var (admin, _, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 1, CreateMap(7002, "a"), new Vector3(0, 0, 0));

        var result = ChatCommandService.Instance.Execute(admin, "/portto");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Usage");
        StringAssert.Contains(result.Message, "/portto");
    }

    [TestMethod]
    public void PortTo_NoMatch_ReturnsError()
    {
        var map = CreateMap(7003, "m");
        var (admin, _, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 1, map, new Vector3(0, 0, 0));
        WireOnline(admin);

        var result = ChatCommandService.Instance.Execute(admin, "/portto Nobody");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "No player matching");
    }

    [TestMethod]
    public void PortTo_Ambiguous_ReturnsAmbiguous()
    {
        var map = CreateMap(7004, "m");
        var (admin, _, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 1, map, new Vector3(0, 0, 0));
        var (a, _, _) = CreatePlayer(TargetCoid, TargetVehicleCoid, "Bobby", gmLevel: 0, map, new Vector3(10, 0, 10));
        var (b, _, _) = CreatePlayer(OtherCoid, OtherVehicleCoid, "BobbyJoe", gmLevel: 0, map, new Vector3(20, 0, 20));
        // Starts-with "Bo" matches both at same score → ambiguous when both start with Bo? 
        // "Bobby" exact vs "BobbyJoe" starts — exact wins. Use contains that ties:
        // Score("ob", "Bobby") = contains 60, Score("ob", "Rob") = contains 60 — use Bob and Rob? 
        // Better: two exact-different names that both contain "er": "Hero" and "Zero"
        a.AttachTestDataForTests("Hero");
        b.AttachTestDataForTests("Zero");
        WireOnline(admin, a, b);

        var result = ChatCommandService.Instance.Execute(admin, "/portto er");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Ambiguous");
    }

    [TestMethod]
    public void PortTo_SameMap_SnapsAdminToTarget_SendsTeleportCharacter()
    {
        var map = CreateMap(7005, "shared");
        var dest = new Vector3(250f, 12f, -80f);
        var (admin, adminVeh, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 1, map, new Vector3(1, 0, 1));
        var (target, _, _) = CreatePlayer(TargetCoid, TargetVehicleCoid, "TargetPilot", gmLevel: 0, map, dest);
        WireOnline(admin, target);

        var result = ChatCommandService.Instance.Execute(admin, "/portto Target");

        Assert.IsTrue(result.Handled);
        Assert.IsFalse(result.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(dest.X, adminVeh.Position.X, 0.01f);
        Assert.AreEqual(dest.Y, adminVeh.Position.Y, 0.01f);
        Assert.AreEqual(dest.Z, adminVeh.Position.Z, 0.01f);
        Assert.AreEqual(dest.X, admin.Position.X, 0.01f);
        Assert.AreSame(map, admin.Map);
        AssertSendsClientSnap(result, admin, dest);
        StringAssert.Contains(result.Message, "TargetPilot");
    }

    [TestMethod]
    public void PortTo_CrossMap_JoinsTargetMapInstanceAtPose()
    {
        var adminMap = CreateMap(7006, "admin-map");
        var targetMap = CreateMap(7007, "target-map");
        MapManager.Instance.RegisterMapForTests(adminMap);
        MapManager.Instance.RegisterMapForTests(targetMap);

        var dest = new Vector3(400f, 3f, 500f);
        var (admin, adminVeh, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 1, adminMap, new Vector3(0, 0, 0));
        var (target, _, _) = CreatePlayer(TargetCoid, TargetVehicleCoid, "Remote", gmLevel: 0, targetMap, dest);
        WireOnline(admin, target);

        var result = ChatCommandService.Instance.Execute(admin, "/portto Remote");

        Assert.IsTrue(result.Handled);
        Assert.IsFalse(result.Message.Contains("Failed", StringComparison.OrdinalIgnoreCase), result.Message);
        Assert.AreSame(targetMap, admin.Map, "Must join the target's map instance, not a fresh continent copy.");
        Assert.AreSame(targetMap, adminVeh.Map);
        Assert.AreEqual(dest.X, admin.Position.X, 0.01f);
        Assert.AreEqual(dest.Z, admin.Position.Z, 0.01f);
        Assert.AreEqual(dest.X, adminVeh.Position.X, 0.01f);
        StringAssert.Contains(result.Message, "Remote");
    }

    [TestMethod]
    public void PortToMe_SameMap_SnapsTargetToAdmin_SendsTeleportOnTargetConnection()
    {
        var map = CreateMap(7008, "shared");
        var adminPos = new Vector3(50f, 2f, 60f);
        var (admin, _, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 1, map, adminPos);
        var (target, targetVeh, _) = CreatePlayer(TargetCoid, TargetVehicleCoid, "SummonMe", gmLevel: 0, map, new Vector3(1, 0, 1));
        WireOnline(admin, target);
        _sent.Clear();

        var result = ChatCommandService.Instance.Execute(admin, "/porttome Summon");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(adminPos.X, targetVeh.Position.X, 0.01f);
        Assert.AreEqual(adminPos.Z, targetVeh.Position.Z, 0.01f);
        Assert.AreEqual(adminPos.X, target.Position.X, 0.01f);
        // Target (not admin) must receive TeleportCharacter — ChatManager only forwards result packets to issuer.
        var tp = _sent
            .Where(s => ReferenceEquals(s.Connection, target.OwningConnection))
            .Select(s => s.Packet)
            .OfType<TeleportCharacterPacket>()
            .SingleOrDefault();
        Assert.IsNotNull(tp, "target connection must receive TeleportCharacter (0x8058)");
        Assert.AreEqual(adminPos.X, tp.Position.X, 0.01f);
        Assert.AreEqual(adminPos.Z, tp.Position.Z, 0.01f);
        StringAssert.Contains(result.Message, "SummonMe");
    }

    [TestMethod]
    public void PortToMe_CrossMap_MovesTargetOntoAdminMapAtPose()
    {
        var adminMap = CreateMap(7009, "admin-here");
        var targetMap = CreateMap(7010, "target-there");
        MapManager.Instance.RegisterMapForTests(adminMap);
        MapManager.Instance.RegisterMapForTests(targetMap);

        var adminPos = new Vector3(11f, 1f, 22f);
        var (admin, _, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 1, adminMap, adminPos);
        var (target, targetVeh, _) = CreatePlayer(TargetCoid, TargetVehicleCoid, "FarAway", gmLevel: 0, targetMap, new Vector3(900, 0, 900));
        WireOnline(admin, target);

        var result = ChatCommandService.Instance.Execute(admin, "/porttome Far");

        Assert.IsTrue(result.Handled);
        Assert.IsFalse(result.Message.Contains("Failed", StringComparison.OrdinalIgnoreCase), result.Message);
        Assert.AreSame(adminMap, target.Map);
        Assert.AreSame(adminMap, targetVeh.Map);
        Assert.AreEqual(adminPos.X, target.Position.X, 0.01f);
        Assert.AreEqual(adminPos.Z, targetVeh.Position.Z, 0.01f);
    }

    [TestMethod]
    public void PortTo_Self_Rejected()
    {
        var map = CreateMap(7011, "m");
        var (admin, adminVeh, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Solo", gmLevel: 1, map, new Vector3(3, 0, 3));
        WireOnline(admin);

        var result = ChatCommandService.Instance.Execute(admin, "/portto Solo");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "yourself");
        Assert.AreEqual(3f, adminVeh.Position.X, 0.01f);
    }

    [TestMethod]
    public void PortTo_FuzzyStartsWith_Resolves()
    {
        var map = CreateMap(7012, "m");
        var dest = new Vector3(77f, 0f, 88f);
        var (admin, adminVeh, _) = CreatePlayer(AdminCoid, AdminVehicleCoid, "Admin", gmLevel: 1, map, new Vector3(0, 0, 0));
        var (target, _, _) = CreatePlayer(TargetCoid, TargetVehicleCoid, "Alexandra", gmLevel: 0, map, dest);
        WireOnline(admin, target);

        var result = ChatCommandService.Instance.Execute(admin, "/portto Alex");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(dest.X, adminVeh.Position.X, 0.01f);
        Assert.AreEqual(dest.Z, adminVeh.Position.Z, 0.01f);
    }

    static void AssertSendsClientSnap(ChatCommandExecutionResult result, Character moved, Vector3 dest)
    {
        Assert.AreEqual(0, result.Packets.OfType<SpecialEventPacket>().Count(),
            "SpecialEvent Respawn must not be used for living GM player port");
        var tp = result.Packets.OfType<TeleportCharacterPacket>().SingleOrDefault();
        Assert.IsNotNull(tp, "same-map portto must return TeleportCharacter for admin connection delivery");
        Assert.AreEqual(dest.X, tp.Position.X, 0.01f);
        Assert.AreEqual(dest.Y, tp.Position.Y, 0.01f);
        Assert.AreEqual(dest.Z, tp.Position.Z, 0.01f);
        Assert.AreEqual(dest.X, moved.CurrentVehicle.Position.X, 0.01f);
    }

    static void WireOnline(params Character[] players)
    {
        var snaps = players.Select(p => new OnlinePlayerSnapshot(
            accountId: (uint)(p.ObjectId.Coid & 0xFFFF),
            accountName: string.Empty,
            characterCoid: p.ObjectId.Coid,
            characterName: p.Name ?? string.Empty,
            connection: p.OwningConnection)).ToList();
        PlayerPortService.Instance.ListOnline = () => snaps;
    }

    static SectorMap CreateMap(int continentId, string label) =>
        SectorMap.CreateForTests(
            new ContinentObject
            {
                Id = continentId,
                MapFileName = $"tm_port_{continentId}_{label}",
                DisplayName = label,
                IsTown = false,
                IsPersistent = true,
            },
            new Vector4(0, 0, 0, 0));

    static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer(
        long charCoid,
        long vehicleCoid,
        string name,
        int gmLevel,
        SectorMap map,
        Vector3 position)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SuppressCreatePacketsForTests = true;

        var character = new Character();
        character.SetCoid(charCoid, true);
        character.GMLevel = (byte)gmLevel;
        character.AttachTestDataForTests(name);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = position;
        character.Position = position;
        return (character, vehicle, map);
    }
}
