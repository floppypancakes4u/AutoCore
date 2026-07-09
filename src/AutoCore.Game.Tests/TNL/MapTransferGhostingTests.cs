using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Game.Entities;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

[TestClass]
public class MapTransferGhostingTests
{
    [TestMethod]
    public void EnsureGhostsAndScopeAfterMapTransfer_NullCharacter_Throws()
    {
        var connection = CreateGhostingConnection();
        Assert.ThrowsException<ArgumentNullException>(() => connection.EnsureGhostsAndScopeAfterMapTransfer(null));
    }

    [TestMethod]
    public void EnsureGhostsAndScopeAfterMapTransfer_NoVehicle_Throws()
    {
        var connection = CreateGhostingConnection();
        var character = new Character();
        character.SetCoid(1, true);
        character.SetOwningConnection(connection);

        Assert.ThrowsException<InvalidOperationException>(() =>
            connection.EnsureGhostsAndScopeAfterMapTransfer(character));
    }

    [TestMethod]
    public void EnsureGhostsAndScopeAfterMapTransfer_CreatesGhostsAndRestartsGhosting()
    {
        var connection = CreateGhostingConnection();
        var character = CreateCharacterWithVehicle(connection);

        // Simulate post-ResetGhosting state: ghosting torn down.
        connection.ResetGhosting();
        Assert.IsFalse(connection.IsGhosting());

        connection.EnsureGhostsAndScopeAfterMapTransfer(character);

        Assert.IsNotNull(character.Ghost);
        Assert.IsInstanceOfType(character.Ghost, typeof(GhostCharacter));
        Assert.IsNotNull(character.CurrentVehicle.Ghost);
        Assert.IsInstanceOfType(character.CurrentVehicle.Ghost, typeof(GhostVehicle));
        Assert.AreSame(character.Ghost, connection.GetScopeObject());
    }

    [TestMethod]
    public void EnsureGhostsAndScopeAfterMapTransfer_IsIdempotentOnGhosts()
    {
        var connection = CreateGhostingConnection();
        var character = CreateCharacterWithVehicle(connection);

        connection.EnsureGhostsAndScopeAfterMapTransfer(character);
        var firstCharGhost = character.Ghost;
        var firstVehicleGhost = character.CurrentVehicle.Ghost;

        connection.ResetGhosting();
        connection.EnsureGhostsAndScopeAfterMapTransfer(character);

        Assert.AreSame(firstCharGhost, character.Ghost, "CreateGhost must not replace existing ghosts.");
        Assert.AreSame(firstVehicleGhost, character.CurrentVehicle.Ghost);
    }

    [TestMethod]
    public void ReestablishGhostingAfterMapTransfer_NullCharacter_Throws()
    {
        var connection = CreateGhostingConnection();
        Assert.ThrowsException<ArgumentNullException>(() =>
            connection.ReestablishGhostingAfterMapTransfer(null));
    }

    [TestMethod]
    public void ReestablishGhostingAfterMapTransfer_NoVehicle_Throws()
    {
        var connection = CreateGhostingConnection();
        var character = new Character();
        character.SetCoid(5, true);

        Assert.ThrowsException<InvalidOperationException>(() =>
            connection.ReestablishGhostingAfterMapTransfer(character));
    }

    [TestMethod]
    public void ReestablishGhostingAfterMapTransfer_WithoutCreatePackets_Succeeds()
    {
        var connection = CreateGhostingConnection();
        var character = CreateCharacterWithVehicle(connection);

        connection.ResetGhosting();
        connection.ReestablishGhostingAfterMapTransfer(character, sendCreatePackets: false);

        Assert.IsNotNull(character.Ghost);
        Assert.IsNotNull(character.CurrentVehicle.Ghost);
        Assert.AreSame(character.Ghost, connection.GetScopeObject());
    }

    [TestMethod]
    public void ResetGhosting_ThenEnsure_RestartsScopeObject()
    {
        // Regression: TransferCharacterToMap used to call ResetGhosting only,
        // leaving Scoping/Ghosting off so map entities never re-ghosted.
        var connection = CreateGhostingConnection();
        var character = CreateCharacterWithVehicle(connection);

        connection.EnsureGhostsAndScopeAfterMapTransfer(character);
        Assert.IsNotNull(connection.GetScopeObject());

        connection.ResetGhosting();
        // After reset, scope object reference may still be set on the connection field,
        // but ghosting sequence is inactive until Ensure runs again.
        connection.EnsureGhostsAndScopeAfterMapTransfer(character);

        Assert.AreSame(character.Ghost, connection.GetScopeObject());
        Assert.IsNotNull(character.CurrentVehicle.Ghost);
    }

    private static TNLConnection CreateGhostingConnection()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        return connection;
    }

    private static Character CreateCharacterWithVehicle(TNLConnection connection)
    {
        var character = new Character();
        character.SetCoid(200, true);
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(201, true);
        character.SetCurrentVehicleForTests(vehicle);
        return character;
    }
}
