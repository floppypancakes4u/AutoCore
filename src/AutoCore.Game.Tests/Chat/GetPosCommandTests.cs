using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Database.World.Models;
using AutoCore.Game.Chat;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Utils.Logging;

/// <summary>
/// /getpos — GM diagnostic: print map name, map id, and world X Y Z to chat and console.
/// </summary>
[TestClass]
public class GetPosCommandTests
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
    public void GetPos_IsMutatingCommand()
    {
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/getpos"));
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/GetPos"));
    }

    [TestMethod]
    public void GetPos_GmLevel0_Denied()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 0);
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(character, "/getpos");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Permission denied");
        Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandDenied"));
    }

    [TestMethod]
    public void GetPos_NoCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/getpos");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "No character");
    }

    [TestMethod]
    public void GetPos_NoMap_ReturnsError()
    {
        var character = new Character();
        character.GMLevel = 1;
        character.SetCoid(1, true);

        var result = ChatCommandService.Instance.Execute(character, "/getpos");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "map");
    }

    [TestMethod]
    public void GetPos_ReportsMapNameIdAndXyz()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 1);
        vehicle.Position = new Vector3(123.5f, 4f, -67.25f);
        character.Position = vehicle.Position;

        var result = ChatCommandService.Instance.Execute(character, "/getpos");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "getpos-test");
        StringAssert.Contains(result.Message, "707");
        StringAssert.Contains(result.Message, "123.50");
        StringAssert.Contains(result.Message, "4.00");
        StringAssert.Contains(result.Message, "-67.25");
    }

    [TestMethod]
    public void GetPos_UsesVehiclePositionWhenPresent()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 1);
        character.Position = new Vector3(1f, 2f, 3f);
        vehicle.Position = new Vector3(10f, 20f, 30f);

        var result = ChatCommandService.Instance.Execute(character, "/getpos");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "10.00");
        StringAssert.Contains(result.Message, "20.00");
        StringAssert.Contains(result.Message, "30.00");
        Assert.IsFalse(result.Message.Contains("1.00") && result.Message.Contains("2.00"),
            "must report vehicle pose, not the stale character pose");
    }

    [TestMethod]
    public void GetPos_FallsBackToCharacterPositionWithoutVehicle()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 1);
        character.SetCurrentVehicleForTests(null);
        character.Position = new Vector3(8.25f, 1.5f, 9.75f);

        var result = ChatCommandService.Instance.Execute(character, "/getpos");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "8.25");
        StringAssert.Contains(result.Message, "1.50");
        StringAssert.Contains(result.Message, "9.75");
    }

    [TestMethod]
    public void GetPos_WritesSameMessageToServerConsole()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 1);
        vehicle.Position = new Vector3(11f, 22f, 33f);
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(character, "/getpos");

        Assert.IsTrue(result.Handled);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Message));
        Assert.IsTrue(
            _sink.Records.Any(r =>
                r.EventName == "Legacy" &&
                (r.Message?.Contains(result.Message, StringComparison.Ordinal) ?? false)),
            "server console must echo the same map/id/xyz line that went to chat");
    }

    [TestMethod]
    public void GetPos_IsCaseInsensitive()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 1);

        var result = ChatCommandService.Instance.Execute(character, "/GETPOS");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "getpos-test");
    }

    [TestMethod]
    public void GetPos_EmptyDisplayName_FallsBackToMapFileName()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 1);
        character.Map.ContinentObject.DisplayName = "";
        character.Map.ContinentObject.MapFileName = "tm_fallback";

        var result = ChatCommandService.Instance.Execute(character, "/getpos");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "tm_fallback");
    }

    [TestMethod]
    public void GetPos_EmptyNames_ReportsUnnamed()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 1);
        character.Map.ContinentObject.DisplayName = "";
        character.Map.ContinentObject.MapFileName = "";

        var result = ChatCommandService.Instance.Execute(character, "/getpos");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Unnamed");
    }

    static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer(int gmLevel)
    {
        var map = SectorMap.CreateForTests(
            new ContinentObject
            {
                Id = 707,
                MapFileName = "tm_getpos_test",
                DisplayName = "getpos-test",
                IsTown = false,
                IsPersistent = true,
            },
            new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SuppressCreatePacketsForTests = true;

        var character = new Character();
        character.SetCoid(910001, true);
        character.GMLevel = (byte)gmLevel;
        character.AttachTestDataForTests("GetPosTester");
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(910002, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = vehicle.Position;

        return (character, vehicle, map);
    }
}
