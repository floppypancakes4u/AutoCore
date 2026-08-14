using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// PDB Pass 7. Decode order and widths from client <c>FUN_0060A230</c> (GhostCharacter pack)
/// and <c>FUN_0060A820</c> (unpack). Initial body is <c>DAT_00d1798c</c>, not mask-gated.
/// Incremental flags: GM 0x80000000, Clan 0x20000000, Pet 0x40000000,
/// Position 0x02, Target 0x04, Token 0x100.
/// </summary>
[TestClass]
public class GhostCharacterClientBitTests
{
    [TestCleanup]
    public void TearDown() => NetObject.PIsInitialUpdate = false;

    [TestMethod]
    public void ClientMaskConstants_MatchPackUpdateBitTests()
    {
        Assert.AreEqual(0x002ul, GhostObject.PositionMask);
        Assert.AreEqual(0x004ul, GhostObject.TargetMask);
        Assert.AreEqual(0x100ul, GhostObject.TokenMask);
        Assert.AreEqual(0x20000000ul, GhostCharacter.ClanMask);
        Assert.AreEqual(0x40000000ul, GhostCharacter.PetCBIDMask);
        Assert.AreEqual(0x80000000ul, GhostCharacter.GMMask);
    }

    [TestMethod]
    public void InitialUpdate_WritesNameLevelVehicleCoidAndAppearance()
    {
        var character = MakeCharacter(8101, "PilotA");
        character.SetLevel(9);

        var stream = Pack(character, GhostObject.InitialMask, initial: true);

        stream.Read(out long coid);
        Assert.AreEqual(8101L, coid);
        stream.ReadFlag();
        stream.ReadInt(20);
        stream.ReadInt(18);
        stream.ReadInt(16);
        stream.ReadInt(16);

        stream.ReadString(out string name);
        Assert.AreEqual("PilotA", name);
        stream.ReadString(out string clan);
        Assert.AreEqual(string.Empty, clan);
        stream.Read(out byte level);
        Assert.AreEqual((byte)9, level);
        stream.Read(out long vehicleCoid);
        Assert.AreEqual(9101L, vehicleCoid,
            "FUN_0060A820 writes the 64-bit vehicle COID into synth CreateCharacter +0xD8.");
        Assert.AreEqual((uint)character.HeadId & 0xFFFFu, stream.ReadInt(16), "HeadId 16-bit.");
        Assert.AreEqual((uint)character.BodyId & 0xFFFFu, stream.ReadInt(16), "BodyId 16-bit.");
    }

    [TestMethod]
    public void Incremental_DoesNotCarryHealthOrCurrentVehicle()
    {
        var character = MakeCharacter(8102, "PilotB");
        var stream = Pack(character, GhostObject.HealthMask | GhostObject.HealthMaxMask, initial: false);

        Assert.IsFalse(stream.ReadFlag(), "GM");
        Assert.IsFalse(stream.ReadFlag(), "Clan");
        Assert.IsFalse(stream.ReadFlag(), "Pet");
        Assert.IsFalse(stream.ReadFlag(), "Position");
        Assert.IsFalse(stream.ReadFlag(), "Target");
        Assert.IsFalse(stream.ReadFlag(), "Token");
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
