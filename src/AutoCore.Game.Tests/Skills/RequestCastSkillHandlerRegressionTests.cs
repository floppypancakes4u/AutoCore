using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
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
        CharacterLevelManager.Instance.ClearAllForTests();
        SkillService.ClearCooldownsForTests();
        Vehicle.ClearCombatThrottleForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestSkills();
        CharacterLevelManager.Instance.ClearAllForTests();
        SkillService.ClearCooldownsForTests();
        Vehicle.ClearCombatThrottleForTests();
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
        vehicle.Ghost.ClearDirtyMaskBitsForTests();

        InvokeHandler(connection, BuildRequestBody(
            new TFID(1342195395, true), skillId: 2103, new Vector3(10, 20, 30)));

        Assert.IsTrue(_sent.OfType<SkillStatusEffectPacket>().Any());
        Assert.IsNotNull(vehicle.Ghost);
        var dirty = GetDirtyMaskBits(vehicle.Ghost);
        Assert.AreEqual(GhostVehicle.PowerMask, dirty & GhostVehicle.PowerMask,
            "rejected casts must dirty PowerMask so optimistic client spend can be restored");
    }

    /// <summary>
    /// A cooldown reject must still answer on the SkillStatusEffect channel. The client pushes every
    /// RequestCastSkill onto m_plistSkillsQueue, and CanCast (0x51a790) returns BUSY while that queue
    /// is non-empty. Only three sites pop it: OnDeath, Skill_GenericCast (success) and
    /// Process_EMSG_Sector_SkillStatusEffect — so a silent reject strands the entry and every later
    /// cast of every skill is refused client-side until the player dies.
    ///
    /// Status must be CancelledActive (0x11): the handler's 0x11 branch pops the queue but skips the
    /// "Aborting cooldown" path that destroys the CVOGHBOKToCastAgain heartbeat, so the hotbar sweep
    /// survives, and it prints no "Server says" chat line.
    /// </summary>
    [TestMethod]
    public void RechargeRejection_RepliesCancelledActive_SoClientPopsQueueAndKeepsCooldown()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100, cooldownMs: 14000, cost: 0);
        var (connection, character, target) = CreateCastScenario(skillId: 2103, gmLevel: 0);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2103, 1, target.ObjectId, target.Position));
        var afterSuccess = _sent.OfType<SkillStatusEffectPacket>().Count();
        Assert.AreEqual(1, afterSuccess, "success path must still emit SkillStatusEffect Status=0");

        InvokeHandler(connection, BuildRequestBody(target.ObjectId, skillId: 2103, target.Position));

        var reject = _sent.OfType<SkillStatusEffectPacket>().Skip(afterSuccess).Single();
        Assert.AreEqual((byte)SkillResponse.CancelledActive, reject.Status,
            "cooldown reject must use the 0x11 branch: pops the client skill queue, keeps the sweep");
        Assert.AreNotEqual((byte)SkillResponse.Recharge, reject.Status,
            "Status=Recharge takes the abort path and destroys CVOGHBOKToCastAgain");
    }

    /// <summary>
    /// Only the cooldown reject is remapped. Everything else keeps its retail eSkillResponses value
    /// so GetErrorResponseString still shows the player the right reason.
    /// </summary>
    [TestMethod]
    public void OutOfRangeRejection_KeepsRetailStatus()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 1, cooldownMs: 14000, cost: 0);
        var (connection, _, target) = CreateCastScenario(skillId: 2103, gmLevel: 0);

        InvokeHandler(connection, BuildRequestBody(target.ObjectId, skillId: 2103, target.Position));

        var reject = _sent.OfType<SkillStatusEffectPacket>().Single();
        Assert.AreEqual((byte)SkillResponse.OutOfRange, reject.Status);
    }

    /// <summary>
    /// The server cooldown is the authored one for every account, GM included. Known client
    /// divergence: CVOGHBOKToCastAgain (0x51e240) clamps its own sweep to 500 ms whenever the
    /// caster's character GM flag (CVOGCharacter+0x6B4, delivered by SetGMFlag 0x2033 and by the
    /// vehicle ghost's GM field) is >= 1, so a GM's hotbar goes ready early and the extra clicks are
    /// answered with the quiet CancelledActive reject above. Deliberate: cooldowns stay
    /// server-authoritative and identical for everyone.
    /// </summary>
    [TestMethod]
    public void GmCharacter_GetsNoCooldownExemption()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100, cooldownMs: 14000, cost: 0);
        var (_, gm, target) = CreateCastScenario(skillId: 2103, gmLevel: 5);

        Assert.IsTrue(SkillService.TryCastPlayer(gm, 2103, 1, target.ObjectId, target.Position));

        Assert.IsFalse(
            SkillService.TryCastPlayer(gm, 2103, 1, target.ObjectId, target.Position, out var response));
        Assert.AreEqual(SkillResponse.Recharge, response);
    }

    private static (TNLConnection Connection, Character Caster, Vehicle Target) CreateCastScenario(
        int skillId, byte gmLevel)
    {
        var map = AutoCore.Game.Map.SectorMap.CreateForTests(new ContinentObject
        {
            Id = 1988,
            MapFileName = "tm_recharge_ui",
            DisplayName = "test",
            IsPersistent = true,
        }, new Vector4());
        var connection = new TNLConnection();
        var character = new Character();
        character.SetCoid(NextCoid(), true);
        character.Faction = 0;
        character.GMLevel = gmLevel;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        character.LearnedSkills[skillId] = 1;
        var vehicle = new Vehicle();
        vehicle.SetCoid(NextCoid(), true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        var target = new Vehicle();
        target.SetCoid(NextCoid(), true);
        target.SetMap(map);
        target.SetMaximumHP(1_000_000, triggerGhostUpdate: false);
        target.SetHPForTests(1_000_000);
        vehicle.Position = new Vector3(0, 0, 0);
        target.Position = new Vector3(5, 0, 0);
        return (connection, character, target);
    }

    private static long _nextCoid = 18_400;

    private static long NextCoid() => Interlocked.Increment(ref _nextCoid);

    private static void RegisterDamageSkill(
        int id, float min, float max, float pen, float range, float cooldownMs, float cost)
    {
        const int energy = 22;
        const int flagDamageMin = 65536;
        const int flagDamageMax = 131072;
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = id,
            Name = "Tesla Strike",
            CategoryId = 3,
            GroupId = -1,
            TargetType = 0x8,
            Elements = new List<SkillElement>
            {
                new() { ElementType = flagDamageMin | energy, ValueBase = min },
                new() { ElementType = flagDamageMax | energy, ValueBase = max },
                new() { ElementType = 68, ValueBase = pen },
                new() { ElementType = 7, ValueBase = range },
                new() { ElementType = 3, ValueBase = cooldownMs },
                new() { ElementType = 1, ValueBase = cost },
            }
        });
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
