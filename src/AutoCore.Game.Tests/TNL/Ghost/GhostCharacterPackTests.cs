using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

/// <summary>
/// PackUpdate / PerformScopeQuery coverage for <see cref="GhostCharacter"/>.
/// </summary>
[TestClass]
public class GhostCharacterPackTests
{
    [TestCleanup]
    public void TearDown() => NetObject.PIsInitialUpdate = false;

    [TestMethod]
    public void PackUpdate_WithoutParent_Throws()
    {
        var ghost = new GhostCharacter();
        Assert.ThrowsException<Exception>(() =>
            ghost.PackUpdate(null, GhostObject.InitialMask, new BitStream(new byte[32], 32)));
    }

    [TestMethod]
    public void UnpackUpdate_IsNoOp()
    {
        var ghost = new GhostCharacter();
        ghost.UnpackUpdate(null, new BitStream(new byte[16], 16));
    }

    [TestMethod]
    public void PackUpdate_Initial_WritesNameLevelAndVehicleCoid()
    {
        var character = MakeCharacter(1001, "PilotA");
        var stream = Pack(character, GhostObject.InitialMask, initial: true);

        // PackCommon: coid, global, cbid, maxHP, faction, bareTeam
        stream.Read(out long coid);
        Assert.AreEqual(1001L, coid);
        stream.ReadFlag(); // global
        stream.ReadInt(20);
        stream.ReadInt(18);
        stream.ReadInt(16);
        stream.ReadInt(16);

        stream.ReadString(out string name);
        Assert.AreEqual("PilotA", name);
        stream.ReadString(out string clan);
        Assert.AreEqual(string.Empty, clan);
        stream.Read(out byte level);
        Assert.AreEqual((byte)3, level);
        stream.Read(out long vehicleCoid);
        Assert.AreEqual(2001L, vehicleCoid);
    }

    [TestMethod]
    public void PackUpdate_GMAndPositionAndTargetAndTokenMasks()
    {
        var character = MakeCharacter(1002, "PilotB");
        character.GMLevel = 7;
        character.Position = new Vector3(4f, 5f, 6f);
        character.Rotation = Quaternion.Default;
        var target = new Creature();
        target.SetCoid(3001, true);
        character.SetTargetObject(target);

        var mask = GhostCharacter.GMMask
                   | GhostObject.PositionMask
                   | GhostObject.TargetMask
                   | GhostObject.TokenMask;
        var stream = Pack(character, mask, initial: false);

        Assert.IsTrue(stream.ReadFlag()); // GM
        Assert.AreEqual(7u, stream.ReadInt(4));
        Assert.IsFalse(stream.ReadFlag()); // Clan
        Assert.IsFalse(stream.ReadFlag()); // Pet
        Assert.IsTrue(stream.ReadFlag()); // Position
        stream.Read(out float x);
        Assert.AreEqual(4f, x);
        // Remaining: Y/Z + quat(4) + vel(3) + targetPos(3) = 12 floats
        for (var i = 0; i < 12; i++)
            stream.Read(out float _);

        Assert.IsTrue(stream.ReadFlag()); // Target
        stream.Read(out long tCoid);
        Assert.AreEqual(3001L, tCoid);
        Assert.IsTrue(stream.ReadFlag()); // target global
        Assert.IsTrue(stream.ReadFlag()); // Token
        Assert.IsFalse(stream.ReadFlag()); // GivesToken
    }

    [TestMethod]
    public void PackUpdate_TargetMask_NullTarget_WritesRetailEmptyCoid()
    {
        // Client GhostCharacter::packUpdate writes cfidEmpty (coid=-1) when m_pTargetObject is null.
        var character = MakeCharacter(1003, "PilotC");
        var stream = Pack(character, GhostObject.TargetMask, initial: false);

        Assert.IsFalse(stream.ReadFlag()); // GM
        Assert.IsFalse(stream.ReadFlag()); // Clan
        Assert.IsFalse(stream.ReadFlag()); // Pet
        Assert.IsFalse(stream.ReadFlag()); // Position
        Assert.IsTrue(stream.ReadFlag());  // Target
        stream.Read(out long coid);
        Assert.AreEqual(-1L, coid);
        Assert.IsFalse(stream.ReadFlag());
    }

    [TestMethod]
    public void PackUpdate_ClanMask_WithoutClan_WritesDefaults()
    {
        var character = MakeCharacter(1004, "PilotD");
        var stream = Pack(character, GhostCharacter.ClanMask, initial: false);

        Assert.IsFalse(stream.ReadFlag()); // GM
        Assert.IsTrue(stream.ReadFlag());  // Clan
        stream.Read(out int clanId);
        stream.Read(out int rank);
        Assert.AreEqual(-1, clanId);
        Assert.AreEqual(-1, rank);
        stream.ReadString(out string clanName);
        Assert.AreEqual(string.Empty, clanName);
    }

    [TestMethod]
    public void PerformScopeQuery_WithoutMap_ReturnsEarly()
    {
        var character = MakeCharacter(1005, "PilotE");
        // No map set — must not throw.
        character.Ghost!.PerformScopeQuery(new TNLConnection());
    }

    [TestMethod]
    public void SetParent_AcceptsCharacter()
    {
        var character = MakeCharacter(1006, "PilotF");
        var ghost = new GhostCharacter();
        ghost.SetParent(character);
        // Pack with this ghost after re-parenting through CreateGhost already done.
        Assert.IsNotNull(character.Ghost);
    }

    private static Character MakeCharacter(long coid, string name)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.AttachTestDataForTests(name);
        character.SetLevel(3);
        character.InitializeHealthForTests(100);

        var vehicle = new Vehicle();
        vehicle.SetCoid(coid + 1000, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.CreateGhost();
        return character;
    }

    private static BitStream Pack(Character character, ulong mask, bool initial)
    {
        var stream = new BitStream(new byte[2048], 2048);
        NetObject.PIsInitialUpdate = initial;
        character.Ghost!.PackUpdate(null, mask, stream);
        stream.SetBitPosition(0);
        return stream;
    }
}
