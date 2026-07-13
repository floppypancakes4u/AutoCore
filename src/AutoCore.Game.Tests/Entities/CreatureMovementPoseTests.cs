using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// On-foot (CreatureMoved) pose must update server-side even when the ghost is not ready,
/// so town logout resume and trigger volumes see the walked position.
/// Also covers ghost rebroadcast + target mask branches for ≥95% HandleMovement coverage.
/// </summary>
[TestClass]
public class CreatureMovementPoseTests
{
    private const long CharCoid = 42_100_001L;
    private const long TargetCoidA = 42_100_010L;
    private const long TargetCoidB = 42_100_011L;

    [TestCleanup]
    public void Cleanup()
    {
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(TargetCoidA);
        ObjectManager.Instance.Remove(TargetCoidB);
    }

    [TestMethod]
    public void HandleMovement_WithoutGhost_StillUpdatesPosition()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.Position = new Vector3(1f, 2f, 3f);
        character.Rotation = Quaternion.Default;
        Assert.IsNull(character.Ghost);

        var packet = MakeMove(character.ObjectId, new Vector3(100.5f, 12f, 200.75f),
            new Quaternion(0f, 0.707f, 0f, 0.707f), targetCoid: -1);

        character.HandleMovement(packet);

        Assert.AreEqual(100.5f, character.Position.X, 1e-4f);
        Assert.AreEqual(12f, character.Position.Y, 1e-4f);
        Assert.AreEqual(200.75f, character.Position.Z, 1e-4f);
        Assert.AreEqual(0.707f, character.Rotation.Y, 1e-4f);
        Assert.AreEqual(0.707f, character.Rotation.W, 1e-4f);
        Assert.AreEqual(1f, character.Velocity.X, 1e-4f);
    }

    [TestMethod]
    public void HandleMovement_WithGhost_UpdatesPoseAndDirtiesPositionMask()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.CreateGhost();
        Assert.IsNotNull(character.Ghost);

        character.HandleMovement(MakeMove(
            character.ObjectId,
            new Vector3(50f, 5f, 60f),
            Quaternion.Default,
            targetCoid: -1));

        Assert.AreEqual(50f, character.Position.X, 1e-4f);
        Assert.AreEqual(5f, character.Position.Y, 1e-4f);
        Assert.AreEqual(60f, character.Position.Z, 1e-4f);
        // Position mask must be dirty for remote rebroadcast.
        Assert.AreNotEqual(0UL, GetDirtyMaskBits(character.Ghost) & GhostObject.PositionMask);
    }

    [TestMethod]
    public void HandleMovement_WithGhost_ClearsTargetWhenPacketTargetIsNone()
    {
        var character = CreateCharacterWithGhost();
        var target = CreateTarget(TargetCoidA);
        character.SetTargetObject(target);
        Assert.IsNotNull(character.Target);
        character.Ghost.ClearMaskBits(ulong.MaxValue);

        character.HandleMovement(MakeMove(character.ObjectId, character.Position, Quaternion.Default, targetCoid: -1));

        Assert.IsNull(character.Target);
        Assert.AreNotEqual(0UL, GetDirtyMaskBits(character.Ghost) & GhostObject.TargetMask);
    }

    [TestMethod]
    public void HandleMovement_WithGhost_SwitchesTargetWhenPacketTargetChanges()
    {
        var character = CreateCharacterWithGhost();
        var targetA = CreateTarget(TargetCoidA);
        var targetB = CreateTarget(TargetCoidB);
        character.SetTargetObject(targetA);
        character.Ghost.ClearMaskBits(ulong.MaxValue);

        character.HandleMovement(MakeMove(
            character.ObjectId,
            character.Position,
            Quaternion.Default,
            targetCoid: TargetCoidB,
            targetGlobal: true));

        Assert.AreSame(targetB, character.Target);
        Assert.AreNotEqual(0UL, GetDirtyMaskBits(character.Ghost) & GhostObject.TargetMask);
    }

    [TestMethod]
    public void HandleMovement_WithGhost_AcquiresTargetWhenNonePreviously()
    {
        var character = CreateCharacterWithGhost();
        var target = CreateTarget(TargetCoidA);
        Assert.IsNull(character.Target);
        character.Ghost.ClearMaskBits(ulong.MaxValue);

        character.HandleMovement(MakeMove(
            character.ObjectId,
            character.Position,
            Quaternion.Default,
            targetCoid: TargetCoidA,
            targetGlobal: true));

        Assert.AreSame(target, character.Target);
        Assert.AreNotEqual(0UL, GetDirtyMaskBits(character.Ghost) & GhostObject.TargetMask);
    }

    [TestMethod]
    public void HandleMovement_WithGhost_SameTarget_DoesNotDirtyTargetMask()
    {
        var character = CreateCharacterWithGhost();
        var target = CreateTarget(TargetCoidA);
        character.SetTargetObject(target);
        // Clear dirty bits after SetTargetObject so we only observe HandleMovement.
        character.Ghost.ClearMaskBits(ulong.MaxValue);

        character.HandleMovement(MakeMove(
            character.ObjectId,
            new Vector3(9f, 9f, 9f),
            Quaternion.Default,
            targetCoid: TargetCoidA,
            targetGlobal: true));

        Assert.AreSame(target, character.Target);
        Assert.AreEqual(0UL, GetDirtyMaskBits(character.Ghost) & GhostObject.TargetMask);
        Assert.AreNotEqual(0UL, GetDirtyMaskBits(character.Ghost) & GhostObject.PositionMask);
    }

    [TestMethod]
    public void HandleMovement_WrongObjectId_Throws()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);

        var packet = MakeMove(new TFID(99_999, true), new Vector3(1f, 2f, 3f), Quaternion.Default, targetCoid: -1);

        Assert.ThrowsException<Exception>(() => character.HandleMovement(packet));
    }

    /// <summary>
    /// End-to-end on-foot walk then town capture: vehicle at garage must not win.
    /// </summary>
    [TestMethod]
    public void HandleMovement_ThenTownCapture_PersistsWalkedPoseNotVehicle()
    {
        var continent = new AutoCore.Database.World.Models.ContinentObject
        {
            Id = 558,
            MapFileName = "upside",
            DisplayName = "Upside",
            IsTown = true,
            IsPersistent = true
        };
        var map = AutoCore.Game.Map.SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));

        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests();
        character.Position = new Vector3(0f, 0f, 0f);
        character.SetMap(map);

        var vehicle = new Vehicle();
        vehicle.SetCoid(42_100_002L, true);
        vehicle.AttachTestDataForTests();
        vehicle.Position = new Vector3(1f, -40f, 2f);
        character.SetCurrentVehicleForTests(vehicle);

        // Walk on foot (ghost not required for pose authority).
        character.HandleMovement(MakeMove(
            character.ObjectId,
            new Vector3(123.25f, 14.5f, 456.75f),
            new Quaternion(0f, 1f, 0f, 0f),
            targetCoid: -1));

        var snapshot = character.CaptureWorldStateToDb();
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(123.25f, snapshot.Value.PositionX);
        Assert.AreEqual(14.5f, snapshot.Value.PositionY);
        Assert.AreEqual(456.75f, snapshot.Value.PositionZ);
        Assert.AreNotEqual(1f, snapshot.Value.PositionX);
        Assert.AreNotEqual(-40f, snapshot.Value.PositionY);
    }

    private static Character CreateCharacterWithGhost()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.Position = new Vector3(0f, 0f, 0f);
        character.CreateGhost();
        character.Ghost.ClearMaskBits(ulong.MaxValue);
        return character;
    }

    private static Creature CreateTarget(long coid)
    {
        var target = new Creature();
        target.SetCoid(coid, true);
        ObjectManager.Instance.Add(target);
        return target;
    }

    private static CreatureMovedPacket MakeMove(
        TFID objectId,
        Vector3 location,
        Quaternion rotation,
        long targetCoid,
        bool targetGlobal = false)
    {
        return new CreatureMovedPacket
        {
            ObjectId = objectId,
            Location = location,
            Rotation = rotation,
            Velocity = new Vector3(1f, 0f, 0f),
            AngularVelocity = default,
            TargetPosition = location,
            Absolute = true,
            Target = new TFID(targetCoid, targetGlobal),
        };
    }

    private static readonly FieldInfo DirtyMaskBitsField =
        typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("NetObject._dirtyMaskBits field missing.");

    private static ulong GetDirtyMaskBits(NetObject obj) => (ulong)DirtyMaskBitsField.GetValue(obj);
}
