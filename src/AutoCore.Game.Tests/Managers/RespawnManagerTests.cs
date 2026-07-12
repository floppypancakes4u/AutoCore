using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

[TestClass]
public class RespawnManagerTests
{
    [TestCleanup]
    public void TearDown() => TNLConnection.TestPacketSink = null;

    private static SectorMap CreateMap(int continentId, Vector4 entry)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"test_map_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true
        };
        return SectorMap.CreateForTests(continent, entry);
    }

    private static Character CreateCharacterWithVehicle(long charCoid = 100, long vehicleCoid = 101)
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

        return character;
    }

    private static Reaction CreateMarkReaction(int coid, int genericVar1, params long[] linkedObjects)
    {
        var template = new ReactionTemplate
        {
            COID = coid,
            ReactionType = ReactionType.MarkRepairStation,
            GenericVar1 = genericVar1
        };
        foreach (var linked in linkedObjects)
            template.Objects.Add(linked);

        var reaction = new Reaction(template);
        reaction.SetCoid(coid, false);
        return reaction;
    }

    private static Trigger CreateTrigger(int coid, long reactionCoid, Vector3 position, float scale = 30f)
    {
        var template = new TriggerTemplate
        {
            COID = coid,
            Reactions = { reactionCoid },
            Scale = scale
        };
        var trigger = new Trigger(template);
        trigger.SetCoid(coid, false);
        trigger.Position = position;
        trigger.Scale = scale;
        return trigger;
    }

    #region Packets

    [TestMethod]
    public void RespawnInSectorPacket_Read_ParsesBodyAfterOpcode()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1.5f);
            writer.Write(2.5f);
            writer.Write(3.5f);
            writer.Write(0.1f);
            writer.Write(0.2f);
            writer.Write(0.3f);
            writer.Write(0.9f);
            writer.Write(999L);
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var packet = new RespawnInSectorPacket();
        packet.Read(reader);

        Assert.AreEqual(1.5f, packet.Position.X);
        Assert.AreEqual(2.5f, packet.Position.Y);
        Assert.AreEqual(3.5f, packet.Position.Z);
        Assert.AreEqual(0.1f, packet.Rotation.X);
        Assert.AreEqual(0.9f, packet.Rotation.W);
        Assert.AreEqual(999L, packet.VehicleCoid);
        Assert.AreEqual(AutoCore.Game.Constants.GameOpcode.RespawnInSector, packet.Opcode);
    }

    [TestMethod]
    public void SpecialEventPacket_Write_HasExpectedLayout()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((uint)AutoCore.Game.Constants.GameOpcode.SpecialEvent);
        var packet = new SpecialEventPacket
        {
            Type = SpecialEventType.Respawn,
            Position = new Vector3(10f, 20f, 30f),
            Rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f),
            Target = new TFID(55, true),
            Flag = 1
        };
        packet.Write(writer);
        ms.SetLength(ms.Position);

        Assert.AreEqual(0x44, ms.Length);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        Assert.AreEqual(0x20A9u, reader.ReadUInt32());
        Assert.AreEqual((byte)SpecialEventType.Respawn, reader.ReadByte());
        reader.BaseStream.Position += 3;
        Assert.AreEqual(10f, reader.ReadSingle());
        Assert.AreEqual(20f, reader.ReadSingle());
        Assert.AreEqual(30f, reader.ReadSingle());
        Assert.AreEqual(0.1f, reader.ReadSingle());
        Assert.AreEqual(0.2f, reader.ReadSingle());
        Assert.AreEqual(0.3f, reader.ReadSingle());
        Assert.AreEqual(0.9f, reader.ReadSingle());
        reader.BaseStream.Position += 4;
        Assert.AreEqual(55L, reader.ReadInt64());
        Assert.IsTrue(reader.ReadBoolean());
    }

    [TestMethod]
    public void SpecialEventPacket_Write_NullTarget_WritesDefaultTfid()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)AutoCore.Game.Constants.GameOpcode.SpecialEvent);
        new SpecialEventPacket
        {
            Type = SpecialEventType.TeleportOut,
            Target = null,
            Flag = 0
        }.Write(writer);
        ms.SetLength(ms.Position);
        Assert.AreEqual(0x44, ms.Length);
        Assert.AreEqual(AutoCore.Game.Constants.GameOpcode.SpecialEvent, new SpecialEventPacket().Opcode);
    }

    #endregion

    #region MarkRepairStation

    [TestMethod]
    public void MarkRepairStation_PrefersLinkedPadOverTrigger()
    {
        var map = CreateMap(50, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(9f, 50f, 7f);

        var reaction = CreateMarkReaction(9001, 3, 4242);
        reaction.SetMap(map);

        CreateTrigger(8001, 9001, new Vector3(100f, -20f, 120f), 50f).SetMap(map);

        var pad = new SimpleObject(GraphicsObjectType.Graphics);
        pad.SetCoid(4242, false);
        pad.Position = new Vector3(105f, 51f, 125f);
        pad.Rotation = new Quaternion(0f, 0.5f, 0f, 0.5f);
        pad.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.AreEqual(3, character.GetLastStationId());
        Assert.AreEqual(50, character.GetLastStationMapId());
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out var rot));
        Assert.AreEqual(105f, pose.X);
        Assert.AreEqual(51f, pose.Y);
        Assert.AreEqual(125f, pose.Z);
        Assert.AreEqual(0.5f, rot.Y);
    }

    [TestMethod]
    public void MarkRepairStation_LinkedPadFromMapTemplate_WhenNoLiveEntity()
    {
        var map = CreateMap(53, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0f, 10f, 0f);

        var reaction = CreateMarkReaction(9100, 7, 7777);
        reaction.SetMap(map);

        var graphics = new GraphicsObjectTemplate(GraphicsObjectType.Graphics)
        {
            Location = new Vector4(88f, 12f, 99f, 0f),
            Rotation = new Quaternion(0f, 0f, 0f, 1f)
        };
        map.MapData.Templates[7777] = graphics;

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(88f, pose.X);
        Assert.AreEqual(12f, pose.Y);
        Assert.AreEqual(99f, pose.Z);
    }

    [TestMethod]
    public void MarkRepairStation_TriggerYClampedToActivatorGround()
    {
        var map = CreateMap(51, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(10f, 40f, 10f);

        var reaction = CreateMarkReaction(9002, 1);
        reaction.SetMap(map);
        CreateTrigger(8002, 9002, new Vector3(12f, -5f, 12f), 30f).SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(12f, pose.X);
        Assert.AreEqual(40f, pose.Y);
        Assert.AreEqual(12f, pose.Z);
    }

    [TestMethod]
    public void MarkRepairStation_NearbyGraphicsWhenNoLinkedObjects()
    {
        var map = CreateMap(52, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0f, 10f, 0f);

        var reaction = CreateMarkReaction(9003, 2);
        reaction.SetMap(map);
        CreateTrigger(8003, 9003, new Vector3(0f, 10f, 0f), 20f).SetMap(map);

        var pad = new SimpleObject(GraphicsObjectType.Graphics);
        pad.SetCoid(5555, false);
        pad.Position = new Vector3(3f, 10.5f, 4f);
        pad.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(3f, pose.X);
        Assert.AreEqual(10.5f, pose.Y);
        Assert.AreEqual(4f, pose.Z);
    }

    [TestMethod]
    public void MarkRepairStation_NearbyTemplateWhenNoLiveProps()
    {
        var map = CreateMap(54, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0f, 5f, 0f);

        var reaction = CreateMarkReaction(9004, 4);
        reaction.SetMap(map);
        CreateTrigger(8004, 9004, new Vector3(0f, 5f, 0f), 15f).SetMap(map);

        map.MapData.Templates[6666] = new GraphicsObjectTemplate(GraphicsObjectType.Graphics)
        {
            Location = new Vector4(2f, 6f, 2f, 0f),
            Rotation = Quaternion.Default
        };

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(2f, pose.X);
        Assert.AreEqual(6f, pose.Y);
        Assert.AreEqual(2f, pose.Z);
    }

    [TestMethod]
    public void MarkRepairStation_UsesActivatorWhenNoTriggerOrPad()
    {
        var map = CreateMap(55, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(15f, 16f, 17f);
        character.CurrentVehicle.Rotation = new Quaternion(0f, 0f, 0f, 1f);

        var reaction = CreateMarkReaction(9005, 0); // station id falls back to COID
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.AreEqual(9005, character.GetLastStationId());
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(15f, pose.X);
        Assert.AreEqual(16f, pose.Y);
        Assert.AreEqual(17f, pose.Z);
    }

    [TestMethod]
    public void MarkRepairStation_UsesReactionPositionWhenSet()
    {
        var map = CreateMap(56, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(1f, 1f, 1f);

        var reaction = CreateMarkReaction(9006, 9);
        reaction.Position = new Vector3(70f, 71f, 72f);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(70f, pose.X);
        Assert.AreEqual(71f, pose.Y);
        Assert.AreEqual(72f, pose.Z);
    }

    [TestMethod]
    public void MarkRepairStation_NoCharacter_StillReturnsTrue()
    {
        var map = CreateMap(57, new Vector4(0, 0, 0, 0));
        var creature = new Creature();
        creature.SetCoid(1, false);
        creature.SetMap(map);

        var reaction = CreateMarkReaction(9007, 1);
        reaction.SetMap(map);

        // Creature is not a character and has no super-character owner.
        Assert.IsTrue(reaction.TriggerIfPossible(creature));
    }

    [TestMethod]
    public void MarkRepairStation_SkipsLinkedTriggerObjects()
    {
        var map = CreateMap(58, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0f, 8f, 0f);

        // Linked COID points at a Trigger — must not be treated as a pad.
        var reaction = CreateMarkReaction(9008, 1, 8008);
        reaction.SetMap(map);
        CreateTrigger(8008, 9008, new Vector3(5f, 8f, 5f), 20f).SetMap(map);

        var pad = new SimpleObject(GraphicsObjectType.Graphics);
        pad.SetCoid(8888, false);
        pad.Position = new Vector3(6f, 8.5f, 6f);
        pad.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        // Nearby pad wins (linked was a trigger and skipped).
        Assert.AreEqual(6f, pose.X);
        Assert.AreEqual(8.5f, pose.Y);
    }

    #endregion

    #region Character station storage

    [TestMethod]
    public void Character_LastStation_RuntimeOverridesDbDefaults()
    {
        var character = new Character();
        Assert.AreEqual(-1, character.GetLastStationId());
        Assert.AreEqual(-1, character.GetLastStationMapId());
        Assert.IsFalse(character.TryGetLastStationPose(out _, out _));

        character.SetLastRepairStation(12, 34);
        Assert.AreEqual(12, character.GetLastStationId());
        Assert.AreEqual(34, character.GetLastStationMapId());
        Assert.IsFalse(character.TryGetLastStationPose(out _, out _));

        character.SetLastRepairStation(12, 34, new Vector3(1f, 2f, 3f), Quaternion.Default);
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(1f, pose.X);
    }

    #endregion

    #region Revive

    [TestMethod]
    public void Revive_ClearsCorpseAndRestoresHP()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(1, true);
        vehicle.SetHPForTests(0);
        vehicle.OnDeath(AutoCore.Game.Constants.DeathType.Violent);

        Assert.IsTrue(vehicle.IsCorpse);
        Assert.AreEqual(0, vehicle.GetCurrentHP());

        vehicle.Revive();

        Assert.IsFalse(vehicle.IsCorpse);
        Assert.IsTrue(vehicle.GetCurrentHP() > 0);
        Assert.AreEqual(vehicle.GetMaximumHP(), vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void Character_Revive_ClearsCorpse()
    {
        var character = new Character();
        character.SetCoid(2, true);
        character.OnDeath(AutoCore.Game.Constants.DeathType.Silent);
        Assert.IsTrue(character.IsCorpse);
        character.Revive();
        Assert.IsFalse(character.IsCorpse);
    }

    #endregion

    #region Destination resolve

    [TestMethod]
    public void TryResolveDestination_UsesPadObjectWhenPresent()
    {
        var map = CreateMap(70, new Vector4(1f, 2f, 3f, 0f));
        var pad = new SimpleObject(GraphicsObjectType.Graphics);
        pad.SetCoid(4242, false);
        pad.Position = new Vector3(100f, 200f, 300f);
        pad.Rotation = new Quaternion(0f, 0f, 0f, 1f);
        pad.SetMap(map);

        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.SetLastRepairStation(4242, 70);

        var previous = RespawnManager.Instance.ResolveMapForTests;
        try
        {
            RespawnManager.Instance.ResolveMapForTests = id => map;

            Assert.IsTrue(RespawnManager.Instance.TryResolveDestination(
                character, out var destMap, out var pos, out var rot, out var reason));

            Assert.IsNull(reason);
            Assert.AreSame(map, destMap);
            Assert.AreEqual(100f, pos.X);
            Assert.AreEqual(200f, pos.Y);
            Assert.AreEqual(300f, pos.Z);
            Assert.AreEqual(1f, rot.W);
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previous;
        }
    }

    [TestMethod]
    public void TryResolveDestination_FallsBackToEntryPoint()
    {
        var map = CreateMap(80, new Vector4(11f, 22f, 33f, 0f));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);

        Assert.IsTrue(RespawnManager.Instance.TryResolveDestination(
            character, out var destMap, out var pos, out _, out var reason));

        Assert.IsNull(reason);
        Assert.AreSame(map, destMap);
        Assert.AreEqual(11f, pos.X);
        Assert.AreEqual(22f, pos.Y);
        Assert.AreEqual(33f, pos.Z);
    }

    [TestMethod]
    public void TryResolveDestination_PrefersStoredPoseOverMissingObject()
    {
        var map = CreateMap(91, new Vector4(1f, 2f, 3f, 0f));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.SetLastRepairStation(3, 91, new Vector3(400f, 500f, 600f), Quaternion.Default);

        Assert.IsTrue(RespawnManager.Instance.TryResolveDestination(
            character, out _, out var pos, out _, out _));
        Assert.AreEqual(400f, pos.X);
        Assert.AreEqual(500f, pos.Y);
        Assert.AreEqual(600f, pos.Z);
    }

    [TestMethod]
    public void TryResolveDestination_MissingStationObject_UsesEntry()
    {
        var map = CreateMap(92, new Vector4(4f, 5f, 6f, 0f));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.SetLastRepairStation(99999, 92); // id only, no pose

        Assert.IsTrue(RespawnManager.Instance.TryResolveDestination(
            character, out _, out var pos, out _, out _));
        Assert.AreEqual(4f, pos.X);
        Assert.AreEqual(5f, pos.Y);
        Assert.AreEqual(6f, pos.Z);
    }

    [TestMethod]
    public void TryResolveDestination_NoMap_Fails()
    {
        var character = CreateCharacterWithVehicle();
        // No map, LastTownId default -1
        Assert.IsFalse(RespawnManager.Instance.TryResolveDestination(
            character, out _, out _, out _, out var reason));
        Assert.IsTrue(reason.Contains("no map"));
    }

    [TestMethod]
    public void TryResolveDestination_UsesResolveMapForTests_ForOtherContinent()
    {
        var current = CreateMap(100, new Vector4(0, 0, 0, 0));
        var dest = CreateMap(101, new Vector4(9f, 8f, 7f, 0f));
        var character = CreateCharacterWithVehicle();
        character.SetMap(current);
        character.SetLastRepairStation(1, 101, new Vector3(1f, 2f, 3f), Quaternion.Default);

        var previous = RespawnManager.Instance.ResolveMapForTests;
        try
        {
            RespawnManager.Instance.ResolveMapForTests = id =>
            {
                Assert.AreEqual(101, id);
                return dest;
            };

            Assert.IsTrue(RespawnManager.Instance.TryResolveDestination(
                character, out var destMap, out var pos, out _, out _));
            Assert.AreSame(dest, destMap);
            Assert.AreEqual(1f, pos.X);
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previous;
        }
    }

    #endregion

    #region RespawnInSector

    [TestMethod]
    public void TryRespawnInSector_SameMap_MovesAndRevives()
    {
        var map = CreateMap(90, new Vector4(5f, 6f, 7f, 0f));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.Position = new Vector3(0f, 0f, 0f);
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        character.CurrentVehicle.SetHPForTests(0);
        character.CurrentVehicle.OnDeath(AutoCore.Game.Constants.DeathType.Silent);
        character.SetLastRepairStation(3, 90, new Vector3(50f, 60f, 70f), Quaternion.Default);

        var previous = RespawnManager.Instance.ResolveMapForTests;
        try
        {
            RespawnManager.Instance.ResolveMapForTests = id => map;

            Assert.IsTrue(RespawnManager.Instance.TryRespawnInSector(
                character, character.ObjectId.Coid, out var reason));
            Assert.IsNull(reason);

            Assert.IsFalse(character.CurrentVehicle.IsCorpse);
            Assert.IsTrue(character.CurrentVehicle.GetCurrentHP() > 0);
            Assert.AreEqual(50f, character.CurrentVehicle.Position.X);
            Assert.AreEqual(60f, character.CurrentVehicle.Position.Y);
            Assert.AreEqual(70f, character.CurrentVehicle.Position.Z);
            Assert.AreEqual(50f, character.Position.X);
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previous;
        }
    }

    [TestMethod]
    public void TryRespawnInSector_VehicleCoid_StillSucceeds()
    {
        var map = CreateMap(93, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle(200, 201);
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.SetLastRepairStation(1, 93, new Vector3(8f, 9f, 10f), Quaternion.Default);

        Assert.IsTrue(RespawnManager.Instance.TryRespawnInSector(
            character, character.CurrentVehicle.ObjectId.Coid, out _));
        Assert.AreEqual(8f, character.CurrentVehicle.Position.X);
    }

    [TestMethod]
    public void TryRespawnInSector_ValidVehicleRequest_TargetsCharacterSpecialEvent()
    {
        var map = CreateMap(94, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle(220, 221);
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        BasePacket sent = null;
        TNLConnection.TestPacketSink = (_, packet) => sent = packet;
        character.CurrentVehicle.SetHPForTests(0);
        character.CurrentVehicle.OnDeath(AutoCore.Game.Constants.DeathType.Silent);
        character.SetLastRepairStation(1, 94, new Vector3(8f, 9f, 10f), Quaternion.Default);

        Assert.IsNull(sent, "vehicle death initializes the client UI through ghost state, not SpecialEvent");

        Assert.IsTrue(RespawnManager.Instance.TryRespawnInSector(
            character, character.CurrentVehicle.ObjectId.Coid, out var reason));

        Assert.IsNull(reason);
        var specialEvent = sent as SpecialEventPacket;
        Assert.IsNotNull(specialEvent, "repair is the only step that sends a SpecialEvent");
        Assert.AreEqual(SpecialEventType.Respawn, specialEvent.Type);
        Assert.AreEqual(character.ObjectId.Coid, specialEvent.Target.Coid);
        Assert.AreEqual(character.ObjectId.Global, specialEvent.Target.Global);
        Assert.AreEqual(1, specialEvent.Flag);
    }

    [TestMethod]
    public void PlayerVehicleDeath_ScopesCorpseHealthGhostAfterStateTransition()
    {
        var character = CreateCharacterWithVehicle(230, 231);
        character.CreateGhost();
        character.OwningConnection.ActivateGhosting();
        var vehicle = character.CurrentVehicle;
        vehicle.SetHPForTests(0);

        vehicle.OnDeath(AutoCore.Game.Constants.DeathType.Silent);

        Assert.IsTrue(vehicle.IsCorpse);
        Assert.IsNotNull(vehicle.Ghost, "death must create the owner vehicle ghost if needed");
        Assert.IsNotNull(vehicle.Ghost.GetFirstObjectRef(),
            "death must scope the health/corpse ghost block to the owner connection");
        Assert.AreEqual(1.0f,
            vehicle.Ghost.GetUpdatePriority(character.Ghost, GhostObject.HealthMask, updateSkips: 0),
            "the owner corpse update must outrank foreign pose traffic");
    }

    [TestMethod]
    public void TryRespawnInSector_UnexpectedCoid_FailsWithoutMovingOrReviving()
    {
        var map = CreateMap(94, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle(210, 211);
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.SetLastRepairStation(1, 94, new Vector3(1f, 1f, 1f), Quaternion.Default);

        character.CurrentVehicle.SetHPForTests(0);
        character.CurrentVehicle.OnDeath(AutoCore.Game.Constants.DeathType.Silent);
        var originalPosition = character.CurrentVehicle.Position;

        Assert.IsFalse(RespawnManager.Instance.TryRespawnInSector(character, 999999L, out var reason));
        StringAssert.Contains(reason, "entity");
        Assert.IsTrue(character.CurrentVehicle.IsCorpse);
        Assert.AreEqual(0, character.CurrentVehicle.GetCurrentHP());
        Assert.AreEqual(originalPosition.X, character.CurrentVehicle.Position.X);
    }

    [TestMethod]
    public void TryRespawnInSector_CrossMap_Transfers()
    {
        var current = CreateMap(200, new Vector4(0, 0, 0, 0));
        var dest = CreateMap(201, new Vector4(10f, 20f, 30f, 0f));
        var character = CreateCharacterWithVehicle(300, 301);
        character.SetMap(current);
        character.CurrentVehicle.SetMap(current);
        character.SetLastRepairStation(1, 201, new Vector3(40f, 50f, 60f), Quaternion.Default);

        var previousResolver = RespawnManager.Instance.ResolveMapForTests;
        var previousMapResolver = MapManager.Instance.ResolveMapForTests;
        var previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        try
        {
            RespawnManager.Instance.ResolveMapForTests = id => id == 201 ? dest : current;
            MapManager.Instance.ResolveMapForTests = id => id == 201 ? dest : current;
            MapManager.Instance.SuppressCreatePacketsForTests = true;

            Assert.IsTrue(RespawnManager.Instance.TryRespawnInSector(
                character, character.ObjectId.Coid, out var reason));
            Assert.IsNull(reason);
            Assert.AreSame(dest, character.Map);
            Assert.AreEqual(40f, character.Position.X);
            Assert.AreEqual(50f, character.Position.Y);
            Assert.AreEqual(60f, character.Position.Z);
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previousResolver;
            MapManager.Instance.ResolveMapForTests = previousMapResolver;
            MapManager.Instance.SuppressCreatePacketsForTests = previousSuppress;
        }
    }

    [TestMethod]
    public void TryRespawnInSector_NullCharacter_Fails()
    {
        Assert.IsFalse(RespawnManager.Instance.TryRespawnInSector(null, 0, out var reason));
        Assert.IsTrue(reason.Contains("null"));
    }

    [TestMethod]
    public void TryRespawnInSector_NoConnection_Fails()
    {
        var character = new Character();
        character.SetCoid(1, true);
        character.SetCurrentVehicleForTests(new Vehicle());
        Assert.IsFalse(RespawnManager.Instance.TryRespawnInSector(character, 0, out var reason));
        Assert.IsTrue(reason.Contains("connection"));
    }

    [TestMethod]
    public void TryRespawnInSector_NoVehicle_Fails()
    {
        var character = new Character();
        character.SetCoid(1, true);
        character.SetOwningConnection(new TNLConnection());

        Assert.IsFalse(RespawnManager.Instance.TryRespawnInSector(character, 0, out var reason));
        Assert.IsTrue(reason.Contains("vehicle"));
    }

    [TestMethod]
    public void HandleRespawnInSectorPacket_ReadsAndRespawns()
    {
        var map = CreateMap(95, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle(400, 401);
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.SetLastRepairStation(1, 95, new Vector3(2f, 3f, 4f), Quaternion.Default);

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(1f);
            writer.Write(character.ObjectId.Coid);
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        RespawnManager.Instance.HandleRespawnInSectorPacket(character, reader);
        Assert.AreEqual(2f, character.Position.X);
        Assert.AreEqual(3f, character.Position.Y);
        Assert.AreEqual(4f, character.Position.Z);
    }

    [TestMethod]
    public void HandleRespawnInSectorPacket_Failure_DoesNotThrow()
    {
        var character = new Character();
        character.SetCoid(1, true);
        // no vehicle / connection → TryRespawn fails; handler only logs
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            for (var i = 0; i < 7; i++)
                writer.Write(0f);
            writer.Write(1L);
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        RespawnManager.Instance.HandleRespawnInSectorPacket(character, reader);
    }

    [TestMethod]
    public void TryRespawnInSector_ResolveFails_ReturnsFalse()
    {
        var character = CreateCharacterWithVehicle();
        // no map, no station → resolve fails
        Assert.IsFalse(RespawnManager.Instance.TryRespawnInSector(
            character, character.ObjectId.Coid, out var reason));
        Assert.IsTrue(reason.Contains("no map"));
    }

    [TestMethod]
    public void TryRespawnInSector_CrossMapTransferFails_ReturnsFalse()
    {
        var current = CreateMap(210, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle(500, 501);
        character.SetMap(current);
        character.CurrentVehicle.SetMap(current);
        character.SetLastRepairStation(1, 211, new Vector3(1f, 1f, 1f), Quaternion.Default);

        var previousRespawn = RespawnManager.Instance.ResolveMapForTests;
        var previousMap = MapManager.Instance.ResolveMapForTests;
        try
        {
            // Destination map resolves for pose lookup, but MapManager transfer gets null map.
            RespawnManager.Instance.ResolveMapForTests = id =>
                id == 211 ? CreateMap(211, new Vector4(1, 1, 1, 0)) : current;
            MapManager.Instance.ResolveMapForTests = _ => null;

            Assert.IsFalse(RespawnManager.Instance.TryRespawnInSector(
                character, character.ObjectId.Coid, out var reason));
            Assert.IsTrue(reason.Contains("map transfer"));
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previousRespawn;
            MapManager.Instance.ResolveMapForTests = previousMap;
        }
    }

    [TestMethod]
    public void TryResolveDestination_ResolverThrows_FallsBackToCurrentMap()
    {
        var current = CreateMap(220, new Vector4(5f, 5f, 5f, 0f));
        var character = CreateCharacterWithVehicle();
        character.SetMap(current);
        character.SetLastRepairStation(1, 221); // different map, no pose

        var previous = RespawnManager.Instance.ResolveMapForTests;
        try
        {
            RespawnManager.Instance.ResolveMapForTests = _ => throw new InvalidOperationException("boom");

            Assert.IsTrue(RespawnManager.Instance.TryResolveDestination(
                character, out var destMap, out var pos, out _, out _));
            Assert.AreSame(current, destMap);
            Assert.AreEqual(5f, pos.X);
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previous;
        }
    }

    [TestMethod]
    public void TryResolveDestination_ResolverThrowsWithoutCurrentMap_Fails()
    {
        var character = CreateCharacterWithVehicle();
        character.SetLastRepairStation(1, 222);

        var previous = RespawnManager.Instance.ResolveMapForTests;
        try
        {
            RespawnManager.Instance.ResolveMapForTests = _ => throw new InvalidOperationException("boom");

            Assert.IsFalse(RespawnManager.Instance.TryResolveDestination(
                character, out _, out _, out _, out var reason));
            Assert.IsTrue(reason.Contains("unable to load map"));
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previous;
        }
    }

    [TestMethod]
    public void TryResolveDestination_ResolverReturnsNull_Fails()
    {
        var character = CreateCharacterWithVehicle();
        character.SetLastRepairStation(1, 223, new Vector3(1, 1, 1), Quaternion.Default);

        var previous = RespawnManager.Instance.ResolveMapForTests;
        try
        {
            RespawnManager.Instance.ResolveMapForTests = _ => null;

            Assert.IsFalse(RespawnManager.Instance.TryResolveDestination(
                character, out _, out _, out _, out var reason));
            Assert.IsTrue(reason.Contains("null"));
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previous;
        }
    }

    [TestMethod]
    public void TryResolveDestination_GetMapThrows_FallsBackWhenOnMap()
    {
        var current = CreateMap(230, new Vector4(7f, 7f, 7f, 0f));
        var character = CreateCharacterWithVehicle();
        character.SetMap(current);
        character.SetLastRepairStation(1, 999001); // force GetMap path (no ResolveMapForTests)

        var previous = RespawnManager.Instance.ResolveMapForTests;
        try
        {
            RespawnManager.Instance.ResolveMapForTests = null;
            Assert.IsTrue(RespawnManager.Instance.TryResolveDestination(
                character, out var destMap, out var pos, out _, out _));
            Assert.AreSame(current, destMap);
            Assert.AreEqual(7f, pos.X);
        }
        finally
        {
            RespawnManager.Instance.ResolveMapForTests = previous;
        }
    }

    [TestMethod]
    public void MarkRepairStation_TriggerNotInRange_StillUsesTrigger()
    {
        var map = CreateMap(59, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        // Far outside trigger scale — FindTrigger uses bestAny
        character.CurrentVehicle.Position = new Vector3(1000f, 10f, 1000f);

        var reaction = CreateMarkReaction(9010, 1);
        reaction.SetMap(map);
        CreateTrigger(8010, 9010, new Vector3(0f, 10f, 0f), 5f).SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(0f, pose.X);
        Assert.AreEqual(10f, pose.Y);
    }

    [TestMethod]
    public void MarkRepairStation_ZeroScaleTrigger_UsesUnitRange()
    {
        var map = CreateMap(60, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0.5f, 10f, 0.5f);

        var reaction = CreateMarkReaction(9011, 1);
        reaction.SetMap(map);
        CreateTrigger(8011, 9011, new Vector3(0f, 10f, 0f), 0f).SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(0f, pose.X);
    }

    [TestMethod]
    public void MarkRepairStation_PrefersNonBuriedNearbyObject()
    {
        var map = CreateMap(61, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0f, 20f, 0f);

        var reaction = CreateMarkReaction(9012, 1);
        reaction.SetMap(map);
        CreateTrigger(8012, 9012, new Vector3(0f, 20f, 0f), 30f).SetMap(map);

        var buried = new SimpleObject(GraphicsObjectType.Graphics);
        buried.SetCoid(1, false);
        buried.Position = new Vector3(1f, 0f, 1f); // much lower than trigger
        buried.SetMap(map);

        var surface = new SimpleObject(GraphicsObjectType.Graphics);
        surface.SetCoid(2, false);
        surface.Position = new Vector3(2f, 20.5f, 2f);
        surface.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(2f, pose.X);
        Assert.AreEqual(20.5f, pose.Y);
    }

    [TestMethod]
    public void MarkRepairStation_IgnoresUnrelatedTriggersAndFarProps()
    {
        var map = CreateMap(62, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0f, 10f, 0f);

        var reaction = CreateMarkReaction(9013, 1);
        reaction.SetMap(map);

        // Unrelated trigger (different reaction list)
        CreateTrigger(8013, 9999, new Vector3(0f, 10f, 0f), 50f).SetMap(map);
        // Our trigger
        CreateTrigger(8014, 9013, new Vector3(0f, 10f, 0f), 10f).SetMap(map);

        // Far outside search radius (max(20, 25)=25)
        var far = new SimpleObject(GraphicsObjectType.Graphics);
        far.SetCoid(3, false);
        far.Position = new Vector3(100f, 10f, 100f);
        far.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        // No nearby pad → trigger center
        Assert.AreEqual(0f, pose.X);
        Assert.AreEqual(10f, pose.Y);
    }

    [TestMethod]
    public void MarkRepairStation_TemplateScan_SkipsTriggersAndSpawnPoints()
    {
        var map = CreateMap(63, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0f, 5f, 0f);

        var reaction = CreateMarkReaction(9014, 1);
        reaction.SetMap(map);
        CreateTrigger(8015, 9014, new Vector3(0f, 5f, 0f), 20f).SetMap(map);

        map.MapData.Templates[1] = new TriggerTemplate { Location = new Vector4(1f, 5f, 1f, 0f) };
        map.MapData.Templates[2] = new SpawnPointTemplate { Location = new Vector4(1.5f, 5f, 1.5f, 0f) };
        map.MapData.Templates[3] = new GraphicsObjectTemplate(GraphicsObjectType.Graphics)
        {
            Location = new Vector4(2f, 1f, 2f, 0f), // buried relative to trigger Y=5
            Rotation = Quaternion.Default
        };
        map.MapData.Templates[4] = new GraphicsObjectTemplate(GraphicsObjectType.Graphics)
        {
            Location = new Vector4(3f, 5.5f, 3f, 0f),
            Rotation = Quaternion.Default
        };

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        Assert.AreEqual(3f, pose.X);
        Assert.AreEqual(5.5f, pose.Y);
    }

    [TestMethod]
    public void MarkRepairStation_LinkedSpawnPointTemplate_SkippedForTemplatePad()
    {
        var map = CreateMap(64, new Vector4(0, 0, 0, 0));
        var character = CreateCharacterWithVehicle();
        character.SetMap(map);
        character.CurrentVehicle.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0f, 5f, 0f);

        var reaction = CreateMarkReaction(9015, 1, 50);
        reaction.SetMap(map);

        map.MapData.Templates[50] = new SpawnPointTemplate
        {
            Location = new Vector4(9f, 9f, 9f, 0f)
        };

        Assert.IsTrue(reaction.TriggerIfPossible(character.CurrentVehicle));
        Assert.IsTrue(character.TryGetLastStationPose(out var pose, out _));
        // Linked spawn template skipped → activator pose
        Assert.AreEqual(0f, pose.X);
        Assert.AreEqual(5f, pose.Y);
    }

    #endregion
}
