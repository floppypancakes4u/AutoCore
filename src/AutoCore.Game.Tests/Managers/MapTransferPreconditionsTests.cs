using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

[TestClass]
public class MapTransferPreconditionsTests
{
    [TestMethod]
    public void Validate_NullCharacter()
    {
        var failure = MapTransferPreconditions.Validate(null);
        Assert.AreEqual(MapTransferPreconditions.Failure.CharacterNull, failure);
        Assert.IsFalse(MapTransferPreconditions.TryValidate(null, out _));
        Assert.IsTrue(MapTransferPreconditions.Describe(failure).Contains("null"));
    }

    [TestMethod]
    public void Validate_NoConnection()
    {
        var character = new Character();
        character.SetCoid(1, true);

        var failure = MapTransferPreconditions.Validate(character);
        Assert.AreEqual(MapTransferPreconditions.Failure.NoConnection, failure);
    }

    [TestMethod]
    public void Validate_NoVehicle()
    {
        var character = new Character();
        character.SetCoid(2, true);
        character.SetOwningConnection(new TNLConnection());

        var failure = MapTransferPreconditions.Validate(character);
        Assert.AreEqual(MapTransferPreconditions.Failure.NoVehicle, failure);
        Assert.IsTrue(MapTransferPreconditions.Describe(failure).Contains("vehicle"));
    }

    [TestMethod]
    public void Validate_ReadyForTransfer()
    {
        var character = CreateCharacterWithVehicleAndConnection();

        Assert.AreEqual(MapTransferPreconditions.Failure.None, MapTransferPreconditions.Validate(character));
        Assert.IsTrue(MapTransferPreconditions.TryValidate(character, out var failure));
        Assert.AreEqual(MapTransferPreconditions.Failure.None, failure);
        Assert.IsNull(MapTransferPreconditions.Describe(MapTransferPreconditions.Failure.None));
    }

    [TestMethod]
    public void Describe_AllKnownFailures_HaveMessages()
    {
        Assert.IsNull(MapTransferPreconditions.Describe(MapTransferPreconditions.Failure.None));
        Assert.IsTrue(MapTransferPreconditions.Describe(MapTransferPreconditions.Failure.CharacterNull).Contains("null"));
        Assert.IsTrue(MapTransferPreconditions.Describe(MapTransferPreconditions.Failure.NoConnection).Contains("connection"));
        Assert.IsTrue(MapTransferPreconditions.Describe(MapTransferPreconditions.Failure.NoVehicle).Contains("vehicle"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            MapTransferPreconditions.Describe((MapTransferPreconditions.Failure)999)));
    }

    [TestMethod]
    public void MapManager_TransferCharacterToMap_RejectsInvalidPreconditions()
    {
        Assert.IsFalse(MapManager.Instance.TransferCharacterToMap(null, 1));

        var noConnection = new Character();
        noConnection.SetCoid(10, true);
        Assert.IsFalse(MapManager.Instance.TransferCharacterToMap(noConnection, 1));

        var noVehicle = new Character();
        noVehicle.SetCoid(11, true);
        noVehicle.SetOwningConnection(new TNLConnection());
        Assert.IsFalse(MapManager.Instance.TransferCharacterToMap(noVehicle, 1));
    }

    [TestMethod]
    public void MapManager_TransferCharacterToMap_UnknownMap_ReturnsFalse()
    {
        // Preconditions pass; GetMap throws for an unknown continent and is caught.
        var character = CreateCharacterWithVehicleAndConnection();
        Assert.IsFalse(MapManager.Instance.TransferCharacterToMap(character, continentId: int.MaxValue));
    }

    [TestMethod]
    public void MapManager_TransferCharacterToMap_WithStubMap_RunsFullTransferSequence()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(300, true);
        character.AttachTestDataForTests();
        character.SetLastTownIdForTests(1);
        character.SetOwningConnection(connection);
        character.SetCurrentVehicleForTests(new Vehicle());
        character.CurrentVehicle.SetCoid(301, true);
        character.CurrentVehicle.AttachTestDataForTests();

        var continent = new ContinentObject
        {
            Id = 693,
            MapFileName = "sec_f_h_map_hwy_j2_backrange",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(10f, 20f, 30f, 0f));

        var previousResolver = MapManager.Instance.ResolveMapForTests;
        var previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        try
        {
            MapManager.Instance.ResolveMapForTests = id =>
            {
                Assert.AreEqual(693, id);
                return map;
            };
            MapManager.Instance.SuppressCreatePacketsForTests = true;

            Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, 693));
            Assert.AreSame(map, character.Map);
            Assert.AreEqual(10f, character.Position.X);
            Assert.AreEqual(20f, character.Position.Y);
            Assert.AreEqual(30f, character.Position.Z);
            Assert.AreEqual(693, character.LastTownId,
                "Transfer must update LastTownId so logout resumes on destination map.");
            Assert.AreEqual(10f, character.GetDbPositionXForTests());
            Assert.AreEqual(20f, character.GetDbPositionYForTests());
            Assert.AreEqual(30f, character.GetDbPositionZForTests());
            Assert.IsNotNull(character.Ghost);
            Assert.IsNotNull(character.CurrentVehicle.Ghost);
            Assert.AreSame(character.Ghost, connection.GetScopeObject());

            // Also exercise TransferCharacterToMap null-map resolver path.
            MapManager.Instance.ResolveMapForTests = _ => null;
            Assert.IsFalse(MapManager.Instance.TransferCharacterToMap(character, 693));
        }
        finally
        {
            MapManager.Instance.ResolveMapForTests = previousResolver;
            MapManager.Instance.SuppressCreatePacketsForTests = previousSuppress;
        }
    }

    [TestMethod]
    public void MapManager_TransferCharacterToMap_ResolverPath_CoversMapLookup()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);

        var character = CreateCharacterWithVehicleAndConnection();
        character.SetOwningConnection(connection);

        var continent = new ContinentObject { Id = 1, MapFileName = "test", DisplayName = "t", IsTown = true, IsPersistent = true };
        var map = SectorMap.CreateForTests(continent, new Vector4(1, 2, 3, 0));

        var previous = MapManager.Instance.ResolveMapForTests;
        try
        {
            MapManager.Instance.ResolveMapForTests = _ => map;
            // Create packets fail without clonebase, so overall transfer returns false,
            // but the transfer body (ResetGhosting / SetMap / Reestablish) still runs.
            var result = MapManager.Instance.TransferCharacterToMap(character, 1);
            Assert.IsFalse(result);
            Assert.AreSame(map, character.Map);
            Assert.IsNotNull(character.Ghost);
            Assert.IsNotNull(character.CurrentVehicle.Ghost);
        }
        finally
        {
            MapManager.Instance.ResolveMapForTests = previous;
        }
    }

    private static Character CreateCharacterWithVehicleAndConnection()
    {
        var character = new Character();
        character.SetCoid(100, true);
        character.SetOwningConnection(new TNLConnection());
        character.SetCurrentVehicleForTests(new Vehicle());
        character.CurrentVehicle.SetCoid(101, true);
        return character;
    }
}
