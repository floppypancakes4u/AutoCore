using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Database.World.Models;
using AutoCore.Game.Chat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Utils.Logging;

/// <summary>
/// /teleporttopos — GM same-map teleport to absolute world coordinates.
/// </summary>
[TestClass]
public class TeleportToPosCommandTests
{
    private InMemoryLogSink _sink = null!;

    [TestInitialize]
    public void Init()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
    }

    [TestCleanup]
    public void Cleanup()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
    }

    [TestMethod]
    public void TeleportToPos_IsMutatingCommand()
    {
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/teleporttopos"));
        Assert.IsFalse(ChatAdminGate.IsMutatingCommand("/teleport"),
            "unimplemented /teleport must not remain as a dead mutating token");
        Assert.IsFalse(ChatAdminGate.IsMutatingCommand("/tp"),
            "unimplemented /tp must not remain as a dead mutating token");
    }

    [TestMethod]
    public void TeleportToPos_GmLevel0_Denied_DoesNotMove()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 0);
        vehicle.Position = new Vector3(1f, 0f, 1f);
        character.Position = vehicle.Position;
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(character, "/teleporttopos 10 20 30");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Permission denied");
        Assert.AreEqual(1f, vehicle.Position.X, 0.01f);
        Assert.AreEqual(1f, vehicle.Position.Z, 0.01f);
        Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandDenied"));
    }

    [TestMethod]
    public void TeleportToPos_NoCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/teleporttopos 1 2 3");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "No character");
    }

    [TestMethod]
    public void TeleportToPos_NoVehicle_ReturnsError()
    {
        var character = new Character();
        character.GMLevel = 1;
        character.SetCoid(1, true);

        var result = ChatCommandService.Instance.Execute(character, "/teleporttopos 1 2 3");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "vehicle");
    }

    [TestMethod]
    public void TeleportToPos_NoMap_ReturnsError()
    {
        var character = new Character();
        character.GMLevel = 1;
        character.SetCoid(1, true);
        var vehicle = new Vehicle();
        vehicle.SetCoid(2, true);
        character.SetCurrentVehicleForTests(vehicle);

        var result = ChatCommandService.Instance.Execute(character, "/teleporttopos 1 2 3");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "map");
    }

    [TestMethod]
    public void TeleportToPos_Usage_WhenArgsMissing()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 1);

        var result = ChatCommandService.Instance.Execute(character, "/teleporttopos 1 2");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Usage:");
    }

    [TestMethod]
    public void TeleportToPos_SameMap_AppliesPoseAndSendsTeleportPacket()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 1);
        vehicle.Position = new Vector3(0f, 0f, 0f);
        character.Position = vehicle.Position;
        var dest = new Vector3(123.5f, 4f, -67.25f);

        var result = ChatCommandService.Instance.Execute(
            character,
            $"/teleporttopos {dest.X} {dest.Y} {dest.Z}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Teleported");
        var tp = result.Packets.OfType<TeleportCharacterPacket>().SingleOrDefault();
        Assert.IsNotNull(tp);
        Assert.AreEqual(dest.X, tp.Position.X, 0.01f);
        Assert.AreEqual(dest.Y, tp.Position.Y, 0.01f);
        Assert.AreEqual(dest.Z, tp.Position.Z, 0.01f);
        Assert.AreEqual(dest.X, vehicle.Position.X, 0.01f);
        Assert.AreEqual(dest.Y, vehicle.Position.Y, 0.01f);
        Assert.AreEqual(dest.Z, vehicle.Position.Z, 0.01f);
        Assert.AreEqual(dest.X, character.Position.X, 0.01f);
    }

    static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer(int gmLevel)
    {
        var map = SectorMap.CreateForTests(
            new ContinentObject
            {
                Id = 707,
                MapFileName = "tm_teleporttopos_test",
                DisplayName = "tp-pos-test",
                IsTown = false,
                IsPersistent = true,
            },
            new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SuppressCreatePacketsForTests = true;

        var character = new Character();
        character.SetCoid(900001, true);
        character.GMLevel = (byte)gmLevel;
        character.AttachTestDataForTests("TpPosTester");
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(900002, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = vehicle.Position;

        return (character, vehicle, map);
    }
}
