using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Capture live map/pose into character/vehicle DBData (no EF I/O).
/// </summary>
[TestClass]
public class CharacterWorldStateCaptureTests
{
    [TestMethod]
    public void CaptureWorldStateToDb_WithVehicle_UsesVehiclePoseAndMapContinent()
    {
        var map = CreateMap(continentId: 693);
        var character = new Character();
        character.SetCoid(1001, true);
        character.AttachTestDataForTests();
        character.Position = new Vector3(1f, 2f, 3f);
        character.Rotation = new Quaternion(0f, 0f, 0f, 1f);

        var vehicle = new Vehicle();
        vehicle.SetCoid(2001, true);
        vehicle.AttachTestDataForTests();
        vehicle.Position = new Vector3(10.5f, 20.25f, 30.75f);
        vehicle.Rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);

        var snapshot = character.CaptureWorldStateToDb();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(1001L, snapshot.Value.CharacterCoid);
        Assert.AreEqual(693, snapshot.Value.ContinentId);
        Assert.AreEqual(10.5f, snapshot.Value.PositionX);
        Assert.AreEqual(20.25f, snapshot.Value.PositionY);
        Assert.AreEqual(30.75f, snapshot.Value.PositionZ);
        Assert.AreEqual(0.1f, snapshot.Value.RotationX);
        Assert.AreEqual(0.2f, snapshot.Value.RotationY);
        Assert.AreEqual(0.3f, snapshot.Value.RotationZ);
        Assert.AreEqual(0.9f, snapshot.Value.RotationW);
        Assert.AreEqual(2001L, snapshot.Value.VehicleCoid);

        Assert.AreEqual(693, character.LastTownId);
        Assert.AreEqual(10.5f, character.GetDbPositionXForTests());
        Assert.AreEqual(20.25f, character.GetDbPositionYForTests());
        Assert.AreEqual(30.75f, character.GetDbPositionZForTests());
        Assert.AreEqual(0.1f, character.GetDbRotationXForTests());
        Assert.AreEqual(0.9f, character.GetDbRotationWForTests());

        Assert.AreEqual(10.5f, vehicle.GetDbPositionXForTests());
        Assert.AreEqual(30.75f, vehicle.GetDbPositionZForTests());
        Assert.AreEqual(0.2f, vehicle.GetDbRotationYForTests());
        Assert.AreEqual(0.9f, vehicle.GetDbRotationWForTests());
    }

    [TestMethod]
    public void CaptureWorldStateToDb_WithoutVehicle_UsesCharacterPose()
    {
        var map = CreateMap(continentId: 42);
        var character = new Character();
        character.SetCoid(1002, true);
        character.AttachTestDataForTests();
        character.Position = new Vector3(5f, 6f, 7f);
        character.Rotation = new Quaternion(0f, 1f, 0f, 0f);
        character.SetMap(map);

        var snapshot = character.CaptureWorldStateToDb();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(42, snapshot.Value.ContinentId);
        Assert.AreEqual(5f, snapshot.Value.PositionX);
        Assert.AreEqual(6f, snapshot.Value.PositionY);
        Assert.AreEqual(7f, snapshot.Value.PositionZ);
        Assert.AreEqual(0f, snapshot.Value.RotationX);
        Assert.AreEqual(1f, snapshot.Value.RotationY);
        Assert.AreEqual(0f, snapshot.Value.RotationZ);
        Assert.AreEqual(0f, snapshot.Value.RotationW);
        Assert.AreEqual(-1L, snapshot.Value.VehicleCoid);
        Assert.AreEqual(42, character.LastTownId);
    }

    [TestMethod]
    public void CaptureWorldStateToDb_NoMap_KeepsExistingLastTownId()
    {
        var character = new Character();
        character.SetCoid(1003, true);
        character.AttachTestDataForTests();
        character.SetLastTownIdForTests(55);
        character.Position = new Vector3(1f, 1f, 1f);
        character.Rotation = Quaternion.Default;

        var snapshot = character.CaptureWorldStateToDb();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(55, snapshot.Value.ContinentId);
        Assert.AreEqual(55, character.LastTownId);
    }

    [TestMethod]
    public void CaptureWorldStateToDb_NoDbData_ReturnsNull()
    {
        var character = new Character();
        character.SetCoid(1004, true);
        character.Position = new Vector3(1f, 2f, 3f);

        Assert.IsNull(character.CaptureWorldStateToDb());
    }

    [TestMethod]
    public void Vehicle_CaptureWorldStateToDb_NoDbData_IsSafeNoOp()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(2004, true);

        // No AttachTestDataForTests — DBData is null.
        vehicle.CaptureWorldStateToDb(new Vector3(1f, 2f, 3f), Quaternion.Default);

        Assert.IsTrue(float.IsNaN(vehicle.GetDbPositionXForTests()));
    }

    [TestMethod]
    public void GetDbHelpers_WithoutDbData_ReturnNaN()
    {
        var character = new Character();
        character.SetCoid(1005, true);
        Assert.IsTrue(float.IsNaN(character.GetDbPositionXForTests()));
        Assert.IsTrue(float.IsNaN(character.GetDbPositionYForTests()));
        Assert.IsTrue(float.IsNaN(character.GetDbPositionZForTests()));
        Assert.IsTrue(float.IsNaN(character.GetDbRotationXForTests()));
        Assert.IsTrue(float.IsNaN(character.GetDbRotationWForTests()));

        var vehicle = new Vehicle();
        vehicle.SetCoid(2005, true);
        Assert.IsTrue(float.IsNaN(vehicle.GetDbPositionXForTests()));
        Assert.IsTrue(float.IsNaN(vehicle.GetDbPositionZForTests()));
        Assert.IsTrue(float.IsNaN(vehicle.GetDbRotationYForTests()));
        Assert.IsTrue(float.IsNaN(vehicle.GetDbRotationWForTests()));
    }

    [TestMethod]
    public void SetLastTownIdForTests_WithoutDbData_IsSafe()
    {
        var character = new Character();
        character.SetCoid(1006, true);
        character.SetLastTownIdForTests(99);
        Assert.AreEqual(-1, character.LastTownId);
    }

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = "test_map",
            DisplayName = "Test",
            IsTown = false,
            IsPersistent = true
        };
        return SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
    }
}
