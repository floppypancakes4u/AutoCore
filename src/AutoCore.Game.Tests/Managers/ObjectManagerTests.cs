using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using System.Net;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using global::TNL.Entities;

/// <summary>
/// SS-03: Characters/vehicles must be unregistered from ObjectManager on disconnect
/// so reconnect does not receive a stale living entity.
/// </summary>
[TestClass]
public class ObjectManagerTests
{
    // Unique COIDs well clear of other suite fixtures (RespawnManager uses ~100+).
    private const long CharCoidA = 9_030_000_101L;
    private const long VehicleCoidA = 9_030_000_102L;
    private const long CharCoidB = 9_030_000_201L;
    private const long VehicleCoidB = 9_030_000_202L;
    private const long MissingCoid = 9_030_000_999L;

    private static Character CreateCharacterWithVehicle(long charCoid, long vehicleCoid)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(charCoid, true);
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);

        connection.CurrentCharacter = character;
        return character;
    }

    private static void ForceRemove(params long[] coids)
    {
        foreach (var coid in coids)
            ObjectManager.Instance.Remove(coid);
    }

    [TestMethod]
    public void Remove_ByCoid_MissingKey_ReturnsFalse_NoThrow()
    {
        Assert.IsFalse(ObjectManager.Instance.Remove(MissingCoid));
        Assert.IsNull(ObjectManager.Instance.GetObject(MissingCoid, true));
    }

    [TestMethod]
    public void Remove_ByObject_MissingObject_ReturnsFalse_NoThrow()
    {
        var orphan = new Character();
        orphan.SetCoid(MissingCoid - 1, true);

        Assert.IsFalse(ObjectManager.Instance.Remove(orphan));
    }

    [TestMethod]
    public void Remove_ByObject_Null_ReturnsFalse_NoThrow()
    {
        Assert.IsFalse(ObjectManager.Instance.Remove((ClonedObjectBase)null!));
    }

    [TestMethod]
    public void Add_DuplicateCoid_ReturnsFalse()
    {
        const long coid = 9_030_000_501L;
        var first = new Character();
        first.SetCoid(coid, true);
        var second = new Character();
        second.SetCoid(coid, true);

        try
        {
            Assert.IsTrue(ObjectManager.Instance.Add(first));
            Assert.IsFalse(ObjectManager.Instance.Add(second));
            Assert.AreSame(first, ObjectManager.Instance.GetCharacter(coid));
        }
        finally
        {
            ForceRemove(coid);
        }
    }

    [TestMethod]
    public void Add_NonGlobalObject_Throws()
    {
        var local = new Character();
        local.SetCoid(9_030_000_502L, false);

        Assert.ThrowsException<Exception>(() => ObjectManager.Instance.Add(local));
    }

    [TestMethod]
    public void GetOrLoadCharacter_ReturnsCachedInstance_WithoutDb()
    {
        const long coid = 9_030_000_503L;
        var character = new Character();
        character.SetCoid(coid, true);

        try
        {
            Assert.IsTrue(ObjectManager.Instance.Add(character));
            var loaded = ObjectManager.Instance.GetOrLoadCharacter(coid, context: null!);
            Assert.AreSame(character, loaded);
        }
        finally
        {
            ForceRemove(coid);
        }
    }

    [TestMethod]
    public void GetObject_ByTfid_And_TypeFilters()
    {
        const long charCoid = 9_030_000_504L;
        const long vehicleCoid = 9_030_000_505L;
        var character = CreateCharacterWithVehicle(charCoid, vehicleCoid);

        try
        {
            Assert.IsTrue(ObjectManager.Instance.Add(character));
            Assert.IsTrue(ObjectManager.Instance.Add(character.CurrentVehicle));

            Assert.AreSame(character, ObjectManager.Instance.GetObject(new TFID(charCoid, true)));
            Assert.IsNull(ObjectManager.Instance.GetCharacter(vehicleCoid),
                "Vehicle coid must not resolve as Character.");
            Assert.IsNull(ObjectManager.Instance.GetVehicle(charCoid),
                "Character coid must not resolve as Vehicle.");
            Assert.AreSame(character.CurrentVehicle, ObjectManager.Instance.GetVehicle(vehicleCoid));
        }
        finally
        {
            ForceRemove(charCoid, vehicleCoid);
        }
    }

    [TestMethod]
    public void UnregisterCharacterSession_RemovesCharacterAndVehicle_AllowsReAdd()
    {
        var character = CreateCharacterWithVehicle(CharCoidA, VehicleCoidA);
        var vehicle = character.CurrentVehicle;

        try
        {
            Assert.IsTrue(ObjectManager.Instance.Add(character));
            Assert.IsTrue(ObjectManager.Instance.Add(vehicle));
            Assert.IsNotNull(ObjectManager.Instance.GetCharacter(CharCoidA));
            Assert.IsNotNull(ObjectManager.Instance.GetVehicle(VehicleCoidA));

            ObjectManager.Instance.UnregisterCharacterSession(character);

            Assert.IsNull(ObjectManager.Instance.GetCharacter(CharCoidA),
                "Character must leave ObjectManager after session unregister (SS-03).");
            Assert.IsNull(ObjectManager.Instance.GetVehicle(VehicleCoidA),
                "Vehicle must leave ObjectManager after session unregister (SS-03).");
            Assert.IsNull(ObjectManager.Instance.GetObject(CharCoidA, true));
            Assert.IsNull(ObjectManager.Instance.GetObject(VehicleCoidA, true));

            // Re-register same COIDs must succeed (reconnect / fresh binding).
            Assert.IsTrue(ObjectManager.Instance.Add(character));
            Assert.IsTrue(ObjectManager.Instance.Add(vehicle));
            Assert.AreSame(character, ObjectManager.Instance.GetCharacter(CharCoidA));
            Assert.AreSame(vehicle, ObjectManager.Instance.GetVehicle(VehicleCoidA));
        }
        finally
        {
            ForceRemove(CharCoidA, VehicleCoidA);
        }
    }

    [TestMethod]
    public void Remove_ByCoid_RemovesOnlyThatEntry()
    {
        var character = CreateCharacterWithVehicle(CharCoidB, VehicleCoidB);

        try
        {
            Assert.IsTrue(ObjectManager.Instance.Add(character));
            Assert.IsTrue(ObjectManager.Instance.Add(character.CurrentVehicle));

            Assert.IsTrue(ObjectManager.Instance.Remove(CharCoidB));
            Assert.IsNull(ObjectManager.Instance.GetCharacter(CharCoidB));
            Assert.IsNotNull(ObjectManager.Instance.GetVehicle(VehicleCoidB),
                "Removing character coid must not remove vehicle until vehicle is removed.");

            Assert.IsTrue(ObjectManager.Instance.Remove(VehicleCoidB));
            Assert.IsNull(ObjectManager.Instance.GetVehicle(VehicleCoidB));
            Assert.IsFalse(ObjectManager.Instance.Remove(CharCoidB), "Second remove of same coid is a no-op.");
        }
        finally
        {
            ForceRemove(CharCoidB, VehicleCoidB);
        }
    }

    [TestMethod]
    public void OnConnectionTerminated_UnregistersCharacterAndVehicleFromObjectManager()
    {
        // End-to-end registry teardown path used on disconnect (SS-03 verification).
        const long charCoid = 9_030_000_301L;
        const long vehicleCoid = 9_030_000_302L;

        var character = CreateCharacterWithVehicle(charCoid, vehicleCoid);
        var connection = character.OwningConnection;
        // OnConnectionTerminated logs GetNetAddressString(); TNL requires a bound address.
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 27000));

        try
        {
            Assert.IsTrue(ObjectManager.Instance.Add(character));
            Assert.IsTrue(ObjectManager.Instance.Add(character.CurrentVehicle));
            Assert.IsNotNull(ObjectManager.Instance.GetCharacter(charCoid));
            Assert.IsNotNull(ObjectManager.Instance.GetVehicle(vehicleCoid));

            connection.OnConnectionTerminated(TerminationReason.ReasonSelfDisconnect, "SS-03 unit test");

            Assert.IsNull(connection.CurrentCharacter,
                "Connection should clear CurrentCharacter after terminate.");
            Assert.IsNull(ObjectManager.Instance.GetCharacter(charCoid),
                "Disconnect must remove character from ObjectManager so reconnect cannot get a corpse/stale instance.");
            Assert.IsNull(ObjectManager.Instance.GetVehicle(vehicleCoid),
                "Disconnect must remove vehicle from ObjectManager.");
        }
        finally
        {
            ForceRemove(charCoid, vehicleCoid);
        }
    }

    [TestMethod]
    public void UnregisterCharacterSession_NullCharacter_IsSafe()
    {
        ObjectManager.Instance.UnregisterCharacterSession(null!);
    }

    [TestMethod]
    public void UnregisterCharacterSession_NullVehicle_StillRemovesCharacter()
    {
        const long charCoid = 9_030_000_401L;
        var character = new Character();
        character.SetCoid(charCoid, true);
        // No vehicle attached.

        try
        {
            Assert.IsTrue(ObjectManager.Instance.Add(character));
            ObjectManager.Instance.UnregisterCharacterSession(character);
            Assert.IsNull(ObjectManager.Instance.GetCharacter(charCoid));
        }
        finally
        {
            ForceRemove(charCoid);
        }
    }
}
