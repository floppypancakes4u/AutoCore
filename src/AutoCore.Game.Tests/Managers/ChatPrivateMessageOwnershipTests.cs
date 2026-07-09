using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.TNL;
using TerminationReason = global::TNL.Entities.TerminationReason;

/// <summary>
/// SS-04: stale OwningConnection after disconnect must not NRE private chat delivery.
/// </summary>
[TestClass]
public class ChatPrivateMessageOwnershipTests
{
    private static Character CreateCharacterWithConnection(long charCoid = 100, long vehicleCoid = 101)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        // Required so OnConnectionTerminated logging (GetNetAddressString) does not NRE in unit tests.
        connection.SetNetAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));

        var character = new Character();
        character.SetCoid(charCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);

        return character;
    }

    [TestMethod]
    public void TryDeliverPrivateMessage_NullTarget_ReturnsFalseWithoutDelivering()
    {
        var delivered = false;

        var ok = ChatManager.TryDeliverPrivateMessage(null, () => delivered = true);

        Assert.IsFalse(ok);
        Assert.IsFalse(delivered);
    }

    [TestMethod]
    public void TryDeliverPrivateMessage_NullOwningConnection_ReturnsFalseWithoutDelivering()
    {
        var target = new Character();
        target.SetCoid(50, true);
        Assert.IsNull(target.OwningConnection);

        var delivered = false;
        var ok = ChatManager.TryDeliverPrivateMessage(target, () => delivered = true);

        Assert.IsFalse(ok, "SS-04: offline / unowned characters must not receive private messages.");
        Assert.IsFalse(delivered);
    }

    [TestMethod]
    public void TryDeliverPrivateMessage_OnlineTarget_DeliversAndReturnsTrue()
    {
        var target = CreateCharacterWithConnection();
        Assert.IsNotNull(target.OwningConnection);

        var delivered = false;
        var ok = ChatManager.TryDeliverPrivateMessage(target, () => delivered = true);

        Assert.IsTrue(ok);
        Assert.IsTrue(delivered);
    }

    [TestMethod]
    public void TryDeliverPrivateMessage_NullDeliverAction_ReturnsFalseWhenOffline()
    {
        var target = new Character();
        target.SetCoid(51, true);

        Assert.IsFalse(ChatManager.TryDeliverPrivateMessage(target, null));
    }

    [TestMethod]
    public void TryDeliverPrivateMessage_NullDeliverAction_ReturnsTrueWhenOnline()
    {
        var target = CreateCharacterWithConnection(charCoid: 52, vehicleCoid: 53);

        // Online path still succeeds; deliver is optional no-op when null.
        Assert.IsTrue(ChatManager.TryDeliverPrivateMessage(target, null));
    }

    /// <summary>
    /// SS-04 tripwire: disconnect must clear Character.OwningConnection so later
    /// private-message delivery cannot send on a dead TNL connection.
    /// </summary>
    [TestMethod]
    public void OnConnectionTerminated_ClearsOwningConnection()
    {
        var character = CreateCharacterWithConnection(charCoid: 200, vehicleCoid: 201);
        var connection = character.OwningConnection;
        Assert.IsNotNull(connection);
        Assert.AreSame(character, connection.CurrentCharacter);

        connection.OnConnectionTerminated(TerminationReason.ReasonRemoteDisconnectPacket, "SS-04 test disconnect");

        Assert.IsNull(
            character.OwningConnection,
            "SS-04: OnConnectionTerminated must call SetOwningConnection(null) before clearing CurrentCharacter.");
        Assert.IsNull(connection.CurrentCharacter);
    }

    /// <summary>
    /// End-to-end SS-04 verification: PM to a character after its connection terminated
    /// must not attempt delivery (no NRE on target.OwningConnection.SendGamePacket).
    /// </summary>
    [TestMethod]
    public void PrivateMessage_AfterDisconnect_DoesNotDeliverOrThrow()
    {
        var character = CreateCharacterWithConnection(charCoid: 300, vehicleCoid: 301);
        var connection = character.OwningConnection;

        connection.OnConnectionTerminated(TerminationReason.ReasonTimedOut, "SS-04 offline");

        Assert.IsNull(character.OwningConnection);

        var delivered = false;
        var ok = ChatManager.TryDeliverPrivateMessage(character, () =>
        {
            // Would NRE if OwningConnection were still a dead connection and callers sent without null-check.
            delivered = true;
            character.OwningConnection.SendGamePacket(null!);
        });

        Assert.IsFalse(ok);
        Assert.IsFalse(delivered);
    }
}
