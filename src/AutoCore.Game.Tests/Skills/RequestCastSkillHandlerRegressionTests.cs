using System.Reflection;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.Skills;

[TestClass]
public class RequestCastSkillHandlerRegressionTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
    }

    [TestMethod]
    public void UnlearnedSkillRejection_UsesCharacterSourceOwner_NotCurrentVehicle()
    {
        var connection = new TNLConnection();
        var character = new Character();
        character.SetCoid(18325, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle();
        vehicle.SetCoid(18326, true);
        character.SetCurrentVehicleForTests(vehicle);

        InvokeHandler(connection, BuildRequestBody(
            new TFID(1342195395, true), skillId: 2103, new Vector3(10, 20, 30)));

        var response = _sent.OfType<SkillStatusEffectPacket>().Single();
        Assert.AreEqual(character.ObjectId, response.Caster);
        Assert.AreNotEqual(vehicle.ObjectId, response.Caster);
        Assert.AreEqual((byte)SkillResponse.ServerChecksFailed, response.Status);
        Assert.AreEqual((byte)0, response.Flag);
        Assert.AreEqual(0, response.ApplyPower);
    }

    /// <summary>
    /// Client spends power optimistically before RequestCastSkill. On reject the server
    /// often never spent; dirty PowerMask so the HUD can snap back to server truth.
    /// Success path stays silent (no PowerMask / CharacterLevel on approve).
    /// </summary>
    [TestMethod]
    public void UnlearnedSkillRejection_DirtiesVehiclePowerMaskForClientResync()
    {
        var connection = new TNLConnection();
        var character = new Character();
        character.SetCoid(18335, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle();
        vehicle.SetCoid(18336, true);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.CreateGhost();
        vehicle.Ghost.ClearMaskBits(ulong.MaxValue);

        InvokeHandler(connection, BuildRequestBody(
            new TFID(1342195395, true), skillId: 2103, new Vector3(10, 20, 30)));

        Assert.IsTrue(_sent.OfType<SkillStatusEffectPacket>().Any());
        Assert.IsNotNull(vehicle.Ghost);
        var dirty = GetDirtyMaskBits(vehicle.Ghost);
        Assert.AreEqual(GhostVehicle.PowerMask, dirty & GhostVehicle.PowerMask,
            "rejected casts must dirty PowerMask so optimistic client spend can be restored");
    }

    private static ulong GetDirtyMaskBits(NetObject ghost)
    {
        var field = typeof(NetObject).GetField(
            "_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        return (ulong)field.GetValue(ghost)!;
    }

    private static byte[] BuildRequestBody(TFID target, int skillId, Vector3 position)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0);
        writer.Write(target.Coid);
        writer.Write(target.Global);
        writer.Write(new byte[7]);
        writer.Write(skillId);
        writer.Write(position.X);
        writer.Write(position.Y);
        writer.Write(position.Z);
        writer.Flush();
        return stream.ToArray();
    }

    private static void InvokeHandler(TNLConnection connection, byte[] body)
    {
        var method = typeof(TNLConnection).GetMethod(
            "HandleRequestCastSkillPacket",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }
}
