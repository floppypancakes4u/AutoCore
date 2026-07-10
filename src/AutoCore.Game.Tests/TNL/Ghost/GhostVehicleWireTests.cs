using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Stage 4: GhostVehicle.PackUpdate wires real path/template/spawn-owner/driver-AI-state fields
/// onto the wire in the client's fixed field order (VehicleNet_UnpackGhostVehicle contract).
/// </summary>
[TestClass]
public class GhostVehicleWireTests
{
    [TestCleanup]
    public void TearDown()
    {
        NetObject.PIsInitialUpdate = false;
        GhostVehicle.EnableAiStateWire = true;
        GhostVehicle.EnablePathWire = true;
        GhostVehicle.EnableOwnerWire = true;
        GhostVehicle.EnableTemplateSpawnWire = true;
        WireDiag.ResetForTests();
    }

    [TestMethod]
    public void PackInitial_WithPath_WritesPathBlockInOrder()
    {
        var vehicle = CreateVehicleWithMap(9101);
        vehicle.CoidCurrentPath = 555;
        vehicle.ExtraPathId = 7;
        vehicle.PathReversing = true;
        vehicle.PathIsRoad = true;
        vehicle.PatrolDistance = 12.5f;

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);

        Assert.IsTrue(stream.ReadFlag(), "path flag must be set when CoidCurrentPath > 0");
        Assert.AreEqual(555u, stream.ReadInt(18));
        stream.Read(out int extraPathId);
        Assert.AreEqual(7, extraPathId);
        Assert.IsTrue(stream.ReadFlag(), "PathReversing");
        Assert.IsTrue(stream.ReadFlag(), "PathIsRoad");
        stream.Read(out float patrolDistance);
        Assert.AreEqual(12.5f, patrolDistance);
    }

    [TestMethod]
    public void PackInitial_NoPath_WritesFlagFalse()
    {
        var vehicle = CreateVehicleWithMap(9102);

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);

        Assert.IsFalse(stream.ReadFlag(), "path flag must be false when CoidCurrentPath <= 0");
    }

    [TestMethod]
    public void PackInitial_GlobalMapNpcIdentity_PreservesHeaderAndPose()
    {
        const long coid = MapNpcIdentity.CoidBase + 18_228;
        var vehicle = CreateVehicleWithMap(coid);
        vehicle.Position = new Vector3(337.3073f, 2.349889f, 1845.08f);
        vehicle.Rotation = new Quaternion(0f, 0.00005f, 0f, 1f);

        var stream = PackInitial(vehicle, GhostObject.InitialMask | GhostObject.PositionMask);

        stream.Read(out long packedCoid);
        Assert.AreEqual(coid, packedCoid);
        Assert.IsTrue(stream.ReadFlag(), "map NPC vehicle identity must remain global on the wire");

        stream.ReadInt(20); // CBID
        stream.ReadInt(18); // MaxHP
        stream.ReadInt(16); // faction
        stream.ReadInt(16); // bare faction
        stream.Read(out uint _); // primary color
        stream.Read(out uint _); // secondary color
        stream.ReadFlag(); // IsActive
        stream.Read(out byte _); // Trim
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag()); // optional multipliers
        Assert.IsFalse(stream.ReadFlag()); // path
        Assert.IsFalse(stream.ReadFlag()); // template
        Assert.IsFalse(stream.ReadFlag()); // spawn owner
        Assert.AreEqual(0u, stream.ReadInt(8)); // trick count
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsFalse(stream.ReadFlag()); // owner

        for (var i = 0; i < 14; ++i)
            Assert.IsFalse(stream.ReadFlag(), $"unexpected update block before position at index {i}");

        Assert.IsTrue(stream.ReadFlag(), "PositionMask");
        stream.Read(out float x);
        stream.Read(out float y);
        stream.Read(out float z);
        stream.Read(out float qx);
        stream.Read(out float qy);
        stream.Read(out float qz);
        stream.Read(out float qw);

        Assert.AreEqual(vehicle.Position.X, x);
        Assert.AreEqual(vehicle.Position.Y, y);
        Assert.AreEqual(vehicle.Position.Z, z);
        Assert.AreEqual(vehicle.Rotation.X, qx);
        Assert.AreEqual(vehicle.Rotation.Y, qy);
        Assert.AreEqual(vehicle.Rotation.Z, qz);
        Assert.AreEqual(vehicle.Rotation.W, qw);
    }

    [TestMethod]
    public void PackInitial_TemplateAndSpawnOwner_Gated()
    {
        var withValues = CreateVehicleWithMap(9103);
        withValues.TemplateId = 42;
        withValues.SpawnOwnerCoid = 4321;

        var stream = PackInitial(withValues);
        SkipToPathBlock(stream);

        Assert.IsFalse(stream.ReadFlag()); // no path
        Assert.IsTrue(stream.ReadFlag(), "TemplateId flag");
        Assert.AreEqual(42u, stream.ReadInt(20));
        Assert.IsTrue(stream.ReadFlag(), "SpawnOwner flag");
        Assert.AreEqual(4321u, stream.ReadInt(20));

        var withoutValues = CreateVehicleWithMap(9104);
        var stream2 = PackInitial(withoutValues);
        SkipToPathBlock(stream2);

        Assert.IsFalse(stream2.ReadFlag()); // no path
        Assert.IsFalse(stream2.ReadFlag(), "TemplateId flag false when TemplateId == -1");
        Assert.IsFalse(stream2.ReadFlag(), "SpawnOwner flag false when SpawnOwnerCoid == -1");
    }

    [TestMethod]
    public void PackInitial_CreatureOwner_WritesDriverLevel()
    {
        var vehicle = CreateVehicleWithMap(9105);
        var driver = new Creature();
        driver.SetCoid(9106, false);
        driver.Level = 7;
        vehicle.SetOwner(driver);

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);

        Assert.IsFalse(stream.ReadFlag()); // no path
        Assert.IsFalse(stream.ReadFlag()); // no template
        Assert.IsFalse(stream.ReadFlag()); // no spawn owner

        stream.ReadInt(8); // trick count

        Assert.IsFalse(stream.ReadFlag()); // IsTrailer

        Assert.IsTrue(stream.ReadFlag(), "CurrentOwner present");
        stream.Read(out long _); // owner coid
        stream.ReadFlag(); // owner global
        stream.ReadInt(20); // owner CBID

        Assert.IsFalse(stream.ReadFlag(), "characterOwner == null branch (driver is a Creature)");

        Assert.IsFalse(stream.ReadFlag()); // EnhancementID
        Assert.IsFalse(stream.ReadFlag()); // CoidOnUseTrigger
        Assert.IsFalse(stream.ReadFlag()); // CoidOnUseReaction
        Assert.IsFalse(stream.ReadFlag()); // CreatureSummoner
        Assert.IsFalse(stream.ReadFlag()); // DoesntCountAsSummon
        Assert.AreEqual(7u, stream.ReadInt(8), "Level must be the driver creature's level");
        Assert.IsFalse(stream.ReadFlag()); // IsElite
    }

    [TestMethod]
    public void PackUpdate_StateMask_WritesDriverCombatState()
    {
        var vehicle = CreateVehicleWithMap(9107);
        var driver = new Creature();
        driver.SetCoid(9108, false);
        driver.AiCombatState = 3;
        vehicle.SetOwner(driver);

        // Owner block was sent at this client's initial, so delta AI state is safe to write.
        PackInitial(vehicle);

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        SkipNonInitialFlagsBeforeStateMask(stream);

        Assert.IsTrue(stream.ReadFlag(), "StateMask flag must be set when a driver creature is present");
        stream.Read(out byte state);
        Assert.AreEqual((byte)3, state);
    }

    /// <summary>
    /// Regression for the null-owner AV (0x005F8FED): if a delta's owner gating recomputed from the
    /// current lever/owner state instead of what THIS client received at its initial, a live
    /// <c>wire set EnableOwnerWire false</c> during ghost create followed by a flip back to true would
    /// write GM/AI/attribute bytes to a client whose vehicle has no owner object. The latch must keep
    /// those blocks suppressed.
    /// </summary>
    [TestMethod]
    public void PackDelta_AfterOwnerWireOffInitial_SuppressesGmEvenAfterLeverFlipsBackOn()
    {
        var vehicle = CreateVehicleWithMap(9140);
        var character = CreateTestCharacter(9141);
        character.GMLevel = 3;
        vehicle.SetOwner(character);

        // Initial packed with the owner block suppressed: the client never receives an owner object.
        GhostVehicle.EnableOwnerWire = false;
        PackInitial(vehicle, GhostObject.InitialMask | GhostVehicle.GMMask);

        // Lever flipped back on; owner exists server-side, but this client still lacks the object.
        GhostVehicle.EnableOwnerWire = true;
        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.GMMask);

        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag()); // equipment
        Assert.IsFalse(stream.ReadFlag(),
            "GM must stay suppressed on delta: the owner block was never sent to this client at initial");
    }

    [TestMethod]
    public void PackDelta_AfterOwnerSentAtInitial_PacksGm()
    {
        var vehicle = CreateVehicleWithMap(9142);
        var character = CreateTestCharacter(9143);
        character.GMLevel = 5;
        vehicle.SetOwner(character);

        // Owner block sent at initial (default lever on) → delta GM is legitimate.
        PackInitial(vehicle, GhostObject.InitialMask | GhostVehicle.GMMask);

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.GMMask);
        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag()); // equipment
        Assert.IsTrue(stream.ReadFlag(), "GM packs on delta when the owner block was sent at initial");
        Assert.AreEqual(5u, stream.ReadInt(4));
    }

    [TestMethod]
    public void PackUpdate_StateMask_NoDriver_FlagFalse()
    {
        var vehicle = CreateVehicleWithMap(9109);

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        SkipNonInitialFlagsBeforeStateMask(stream);

        Assert.IsFalse(stream.ReadFlag(), "StateMask flag must stay false without a driver creature");
    }

    [TestMethod]
    public void PackUpdate_StateMask_CharacterOwner_FlagFalse()
    {
        var vehicle = CreateVehicleWithMap(9112);
        var driver = new Character();
        driver.SetCoid(9113, false);

        vehicle.SetOwner(driver);

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        SkipNonInitialFlagsBeforeStateMask(stream);

        Assert.IsFalse(stream.ReadFlag(), "StateMask flag must stay false for a Character (player) owner, even though Character inherits GetAsCreature()");
    }

    [TestMethod]
    public void PackUpdate_StateMask_LeverDisabled_FlagFalse()
    {
        var vehicle = CreateVehicleWithMap(9110);
        var driver = new Creature();
        driver.SetCoid(9111, false);
        driver.AiCombatState = 9;
        vehicle.SetOwner(driver);

        GhostVehicle.EnableAiStateWire = false;

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        SkipNonInitialFlagsBeforeStateMask(stream);

        Assert.IsFalse(stream.ReadFlag(), "EnableAiStateWire = false must suppress the AI-state block");
    }

    /// <summary>
    /// Client AV at 0x005F8FED writes owner+0x12A (GM nibble). Never pack GM/AI without a
    /// client-visible owner object — applies game-wide, not map-specific.
    /// </summary>
    [TestMethod]
    public void PackInitial_OwnerWireOff_SuppressesGmAndAiEvenWhenOwnerExistsServerSide()
    {
        var vehicle = CreateVehicleWithMap(9120);
        var character = CreateTestCharacter(9121);
        character.GMLevel = 3;
        vehicle.SetOwner(character);

        var creatureVehicle = CreateVehicleWithMap(9122);
        var creature = new Creature();
        creature.SetCoid(9123, false);
        creature.AiCombatState = 2;
        creatureVehicle.SetOwner(creature);

        GhostVehicle.EnableOwnerWire = false;

        // Character-owned: GM would otherwise pack from superCharacter.
        var gmStream = PackInitial(vehicle, GhostObject.InitialMask | GhostVehicle.GMMask);
        SkipToMaskSectionAfterInitialBody(gmStream, ownerPacked: false);
        SkipEquipmentMaskFlags(gmStream);
        Assert.IsFalse(gmStream.ReadFlag(), "GM must not pack without client owner (owner wire off)");

        // Creature-owned: AI would otherwise pack from driverCreature.
        var aiStream = PackInitial(creatureVehicle, GhostObject.InitialMask | GhostVehicle.StateMask);
        SkipToMaskSectionAfterInitialBody(aiStream, ownerPacked: false);
        SkipEquipmentMaskFlags(aiStream);
        Assert.IsFalse(aiStream.ReadFlag()); // GM
        Assert.IsFalse(aiStream.ReadFlag()); // Clan
        Assert.IsFalse(aiStream.ReadFlag()); // Pet
        Assert.IsFalse(aiStream.ReadFlag()); // Murderer
        Assert.IsFalse(aiStream.ReadFlag()); // Health
        Assert.IsFalse(aiStream.ReadFlag()); // HealthMax
        Assert.IsFalse(aiStream.ReadFlag(), "AI StateMask must not pack without client owner");
    }

    [TestMethod]
    public void PackInitial_CharacterOwnerPacked_AllowsGmBlock()
    {
        var vehicle = CreateVehicleWithMap(9124);
        var character = CreateTestCharacter(9125);
        character.GMLevel = 2;
        vehicle.SetOwner(character);

        var stream = PackInitial(vehicle, GhostObject.InitialMask | GhostVehicle.GMMask);
        SkipToMaskSectionAfterInitialBody(stream, ownerPacked: true);
        SkipEquipmentMaskFlags(stream);

        Assert.IsTrue(stream.ReadFlag(), "GM packs when character owner was on the wire");
        Assert.AreEqual(2u, stream.ReadInt(4));
    }

    [TestMethod]
    public void PackInitial_CreatureOwnerPacked_AllowsAiStateBlock()
    {
        var vehicle = CreateVehicleWithMap(9126);
        var creature = new Creature();
        creature.SetCoid(9127, false);
        creature.AiCombatState = 1;
        vehicle.SetOwner(creature);

        var stream = PackInitial(vehicle, GhostObject.InitialMask | GhostVehicle.StateMask);
        SkipToMaskSectionAfterInitialBody(stream, ownerPacked: true);
        SkipEquipmentMaskFlags(stream);
        Assert.IsFalse(stream.ReadFlag()); // GM
        Assert.IsFalse(stream.ReadFlag()); // Clan
        Assert.IsFalse(stream.ReadFlag()); // Pet
        Assert.IsFalse(stream.ReadFlag()); // Murderer
        Assert.IsFalse(stream.ReadFlag()); // Health
        Assert.IsFalse(stream.ReadFlag()); // HealthMax
        Assert.IsTrue(stream.ReadFlag(), "AI packs when creature owner was on the wire");
        stream.Read(out byte state);
        Assert.AreEqual((byte)1, state);
    }

    [TestMethod]
    public void PackInitial_WithEquipmentMask_DoesNotEmitHardpointPayload()
    {
        // CreateVehicle already embeds hardpoints; initial ghost must not re-send them.
        var vehicle = CreateVehicleWithMap(9130);
        var armor = new Armor();
        armor.SetCoid(9131, false);
        vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _);

        var stream = PackInitial(vehicle, GhostObject.InitialMask | GhostVehicle.ChangeArmor | GhostVehicle.FrontWeaponMask);
        SkipToMaskSectionAfterInitialBody(stream, ownerPacked: false);
        // All seven equipment lead flags false even though ChangeArmor / FrontWeapon were in the mask.
        SkipEquipmentMaskFlags(stream);
    }

    [TestMethod]
    public void PackUpdate_NonInitial_WithWeapon_EmitsHardpointPayload()
    {
        var vehicle = CreateVehicleWithMap(9132);
        var weapon = new Weapon();
        weapon.SetCoid(9133, false);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponFront, weapon, out _));

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.FrontWeaponMask);
        // non-initial: skills flag first
        Assert.IsFalse(stream.ReadFlag()); // SkillsMask not set
        Assert.IsFalse(stream.ReadFlag()); // WheelSet
        Assert.IsTrue(stream.ReadFlag(), "FrontWeapon mask on non-initial");
        Assert.IsTrue(stream.ReadFlag(), "weapon present");
        stream.ReadInt(20); // CBID
        stream.Read(out long coid);
        Assert.AreEqual(9133L, coid);
        stream.ReadFlag(); // global
    }

    [TestMethod]
    public void PackInitial_EnablePathWireFalse_OmitsPathBlock()
    {
        var vehicle = CreateVehicleWithMap(9114);
        vehicle.CoidCurrentPath = 555;
        vehicle.ExtraPathId = 7;
        GhostVehicle.EnablePathWire = false;

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);

        Assert.IsFalse(stream.ReadFlag(), "EnablePathWire=false must force path flag false");
    }

    [TestMethod]
    public void PackInitial_EnableOwnerWireFalse_OmitsOwnerBlock()
    {
        var vehicle = CreateVehicleWithMap(9115);
        var driver = new Creature();
        driver.SetCoid(9116, false);
        vehicle.SetOwner(driver);
        GhostVehicle.EnableOwnerWire = false;

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);
        Assert.IsFalse(stream.ReadFlag()); // path
        Assert.IsFalse(stream.ReadFlag()); // template
        Assert.IsFalse(stream.ReadFlag()); // spawn
        stream.ReadInt(8); // tricks
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsFalse(stream.ReadFlag(), "EnableOwnerWire=false must force owner flag false");
    }

    [TestMethod]
    public void PackInitial_EnableTemplateSpawnWireFalse_OmitsTemplateAndSpawn()
    {
        var vehicle = CreateVehicleWithMap(9117);
        vehicle.TemplateId = 42;
        vehicle.SpawnOwnerCoid = 99;
        GhostVehicle.EnableTemplateSpawnWire = false;

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);
        Assert.IsFalse(stream.ReadFlag()); // path
        Assert.IsFalse(stream.ReadFlag(), "template suppressed");
        Assert.IsFalse(stream.ReadFlag(), "spawn owner suppressed");
    }

    [TestMethod]
    public void PackInitial_WireDiagEnabled_RecordsGhostPackWithWouldPackDetail()
    {
        WireDiag.Enabled = true;
        var vehicle = CreateVehicleWithMap(9118);
        vehicle.CoidCurrentPath = 12;
        GhostVehicle.EnablePathWire = false;

        PackInitial(vehicle);

        var entry = WireDiag.Snapshot().Single();
        Assert.AreEqual(WireDiagKind.GhostPack, entry.Kind);
        Assert.AreEqual("GhostVehicle", entry.Name);
        Assert.IsTrue(entry.Initial);
        StringAssert.Contains(entry.Detail, "path=0/1");
    }

    private static Vehicle CreateVehicleWithMap(long coid)
    {
        var map = CreateTestMap((int)coid);
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.SetMap(map);
        vehicle.CreateGhost();
        return vehicle;
    }

    private static Character CreateTestCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        // Name/body ids come from private DBData — attach a minimal row for ghost pack strings.
        var dbData = new CharacterData
        {
            Coid = coid,
            Name = "Tester",
            Level = 1,
        };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
    }

    private static SectorMap CreateTestMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_ghost_vehicle_wire_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };

        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static BitStream PackInitial(Vehicle vehicle, ulong updateMask = GhostObject.InitialMask)
    {
        var stream = new BitStream(new byte[4096], 4096);
        NetObject.PIsInitialUpdate = true;
        vehicle.Ghost.PackUpdate(null, updateMask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    private static BitStream PackUpdateNonInitial(Vehicle vehicle, ulong updateMask)
    {
        var stream = new BitStream(new byte[4096], 4096);
        NetObject.PIsInitialUpdate = false;
        vehicle.Ghost.PackUpdate(null, updateMask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    /// <summary>Consumes PackCommon + the fixed multiplier flags, leaving the stream positioned
    /// right before the path-block flag (GhostVehicle.cs initial block).</summary>
    private static void SkipToPathBlock(BitStream stream)
    {
        stream.Read(out long _); // PackCommon: coid
        stream.ReadFlag();       // PackCommon: global
        stream.ReadInt(20);      // PackCommon: CBID
        stream.ReadInt(18);      // PackCommon: MaxHP
        stream.ReadInt(16);      // PackCommon: faction
        stream.ReadInt(16);      // PackCommon: bareTeamFaction

        stream.Read(out uint _); // PrimaryColor
        stream.Read(out uint _); // SecondaryColor
        stream.ReadFlag();       // IsActive
        stream.Read(out byte _); // Trim

        for (var i = 0; i < 7; ++i)
            stream.ReadFlag(); // SpeedAdd .. AVDNormalSpinDampeningMultiplier (always false)
    }

    /// <summary>Consumes the non-initial update flags that precede the StateMask block.</summary>
    private static void SkipNonInitialFlagsBeforeStateMask(BitStream stream)
    {
        for (var i = 0; i < 14; ++i)
            stream.ReadFlag();
    }

    /// <summary>
    /// After initial body (common through trailer/owner), mask section starts at equipment flags.
    /// </summary>
    private static void SkipToMaskSectionAfterInitialBody(BitStream stream, bool ownerPacked)
    {
        SkipToPathBlock(stream);
        Assert.IsFalse(stream.ReadFlag()); // path (tests that use this leave path empty)
        Assert.IsFalse(stream.ReadFlag()); // template
        Assert.IsFalse(stream.ReadFlag()); // spawn
        Assert.AreEqual(0u, stream.ReadInt(8)); // tricks
        Assert.IsFalse(stream.ReadFlag()); // trailer
        if (ownerPacked)
        {
            Assert.IsTrue(stream.ReadFlag()); // owner present
            stream.Read(out long _); // coid
            stream.ReadFlag(); // global
            stream.ReadInt(20); // CBID
            var isCharacter = stream.ReadFlag();
            if (isCharacter)
            {
                stream.ReadString(out string _); // name
                stream.ReadString(out string _); // clan
                stream.Read(out byte _); // level
                stream.ReadFlag(); // possess
                stream.ReadString(out string _); // vehicle name
                for (var i = 0; i < 8; ++i)
                    stream.ReadInt(16);
            }
            else
            {
                Assert.IsFalse(stream.ReadFlag()); // enhancement
                Assert.IsFalse(stream.ReadFlag()); // on-use trigger
                Assert.IsFalse(stream.ReadFlag()); // on-use reaction
                Assert.IsFalse(stream.ReadFlag()); // summoner
                Assert.IsFalse(stream.ReadFlag()); // doesnt count summon
                stream.ReadInt(8); // level
                Assert.IsFalse(stream.ReadFlag()); // elite
            }
        }
        else
        {
            Assert.IsFalse(stream.ReadFlag()); // owner
        }
    }

    /// <summary>Seven equipment mask lead flags (all false when those masks are not set).</summary>
    private static void SkipEquipmentMaskFlags(BitStream stream)
    {
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag());
    }
}
