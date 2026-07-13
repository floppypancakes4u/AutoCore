using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// HP / Power / Shield / Heat capture on logout and restore on login.
/// </summary>
[TestClass]
public class VehicleCombatStatePersistenceTests
{
    private const long CharCoid = 9_050_000_101L;
    private const long VehicleCoid = 9_050_000_102L;

    [TestCleanup]
    public void Cleanup()
    {
        CharacterLevelManager.Instance.ClearAllForTests();
        TNLConnection.WorldStatePersistenceForTests = null;
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
    }

    [TestMethod]
    public void CaptureCombatState_ReadsLivePoolsAndWritesDbData()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        // Default MaxHP/HP = 500 from SimpleObject ctor.
        vehicle.SetCurrentHP(120, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetMaximumShield(200, triggerGhostUpdate: false);
        vehicle.SetCurrentShield(45, triggerGhostUpdate: false);
        vehicle.SetMaximumHeat(100, triggerGhostUpdate: false);
        vehicle.SetCurrentHeat(30, triggerGhostUpdate: false);

        CharacterLevelManager.Instance.SetMaxMana(character, 80, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 22, sendPacket: false);

        var snap = vehicle.CaptureCombatState(character);

        Assert.AreEqual(120, snap.CurrentHP);
        Assert.AreEqual(45, snap.CurrentShield);
        Assert.AreEqual(22, snap.CurrentPower);
        Assert.AreEqual(30, snap.CurrentHeat);

        Assert.AreEqual(120, vehicle.GetDbCurrentHPForTests());
        Assert.AreEqual(45, vehicle.GetDbCurrentShieldForTests());
        Assert.AreEqual(22, vehicle.GetDbCurrentPowerForTests());
        Assert.AreEqual(30, vehicle.GetDbCurrentHeatForTests());
    }

    [TestMethod]
    public void CaptureWorldStateToDb_IncludesCombatPools()
    {
        var map = CreateMap(710);
        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests();

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        vehicle.SetCurrentHP(99, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetMaximumShield(100, triggerGhostUpdate: false);
        vehicle.SetCurrentShield(11, triggerGhostUpdate: false);
        vehicle.SetMaximumHeat(50, triggerGhostUpdate: false);
        vehicle.SetCurrentHeat(7, triggerGhostUpdate: false);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);

        CharacterLevelManager.Instance.SetMaxMana(character, 60, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 15, sendPacket: false);

        var snapshot = character.CaptureWorldStateToDb();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(99, snapshot.Value.CurrentHP);
        Assert.AreEqual(11, snapshot.Value.CurrentShield);
        Assert.AreEqual(15, snapshot.Value.CurrentPower);
        Assert.AreEqual(7, snapshot.Value.CurrentHeat);
        Assert.AreEqual(VehicleCoid, snapshot.Value.VehicleCoid);
    }

    [TestMethod]
    public void CaptureWorldStateToDb_WithoutVehicle_UsesCombatSentinels()
    {
        var map = CreateMap(711);
        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests();
        character.SetMap(map);

        var snapshot = character.CaptureWorldStateToDb();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(-1, snapshot.Value.CurrentHP);
        Assert.AreEqual(-1, snapshot.Value.CurrentShield);
        Assert.AreEqual(-1, snapshot.Value.CurrentPower);
        Assert.AreEqual(-1, snapshot.Value.CurrentHeat);
        Assert.AreEqual(-1L, snapshot.Value.VehicleCoid);
    }

    [TestMethod]
    public void ApplyToVehicle_WritesCombatColumns()
    {
        var vehicle = new VehicleData { Coid = VehicleCoid };
        var snap = new CharacterWorldStateSnapshot(
            CharCoid, 1,
            1f, 2f, 3f,
            0f, 0f, 0f, 1f,
            VehicleCoid,
            CurrentHP: 40,
            CurrentShield: 20,
            CurrentPower: 10,
            CurrentHeat: 5);

        CharacterWorldStatePersistence.ApplyToVehicle(vehicle, snap);

        Assert.AreEqual(40, vehicle.CurrentHP);
        Assert.AreEqual(20, vehicle.CurrentShield);
        Assert.AreEqual(10, vehicle.CurrentPower);
        Assert.AreEqual(5, vehicle.CurrentHeat);
        Assert.AreEqual(1f, vehicle.PositionX);
    }

    [TestMethod]
    public void SaveWithStore_PersistsCombatFieldsOnVehicle()
    {
        var character = new CharacterData { Coid = CharCoid };
        var vehicle = new VehicleData { Coid = VehicleCoid, CurrentHP = -1 };
        var store = new FakeWorldStateStore
        {
            Characters = { [CharCoid] = character },
            Vehicles = { [VehicleCoid] = vehicle }
        };

        var snapshot = new CharacterWorldStateSnapshot(
            CharCoid, 50,
            10f, 20f, 30f,
            0f, 0f, 0f, 1f,
            VehicleCoid,
            CurrentHP: 77,
            CurrentShield: 33,
            CurrentPower: 12,
            CurrentHeat: 4);

        CharacterWorldStatePersistence.SaveWithStore(store, snapshot);

        Assert.AreEqual(1, store.SaveChangesCalls);
        Assert.AreEqual(77, vehicle.CurrentHP);
        Assert.AreEqual(33, vehicle.CurrentShield);
        Assert.AreEqual(12, vehicle.CurrentPower);
        Assert.AreEqual(4, vehicle.CurrentHeat);
    }

    [TestMethod]
    public void RestoreCombatStateFromDb_AppliesSavedValuesAfterFullPools()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        // Simulate login path: maxes computed and pools filled full first.
        vehicle.SetMaximumHP(500, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetCurrentHP(500, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetMaximumShield(200, triggerGhostUpdate: false);
        vehicle.SetCurrentShield(200, triggerGhostUpdate: false);
        vehicle.SetMaximumHeat(80, triggerGhostUpdate: false);
        vehicle.SetCurrentHeat(0, triggerGhostUpdate: false);
        CharacterLevelManager.Instance.SetMaxMana(character, 100, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 100, sendPacket: false);

        vehicle.SetDbCombatStateForTests(currentHP: 150, currentShield: 40, currentPower: 25, currentHeat: 12);

        vehicle.RestoreCombatStateFromDb(character);

        Assert.AreEqual(150, vehicle.GetCurrentHP());
        Assert.AreEqual(40, vehicle.CurrentShield);
        Assert.AreEqual(12, vehicle.CurrentHeat);
        Assert.AreEqual(25, CharacterLevelManager.Instance.GetCurrentMana(CharCoid));
        Assert.AreEqual(100, CharacterLevelManager.Instance.GetPower(CharCoid).Maximum);
    }

    [TestMethod]
    public void RestoreCombatStateFromDb_SentinelLeavesFullPools()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        vehicle.SetMaximumHP(500, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetCurrentHP(500, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetMaximumShield(200, triggerGhostUpdate: false);
        vehicle.SetCurrentShield(200, triggerGhostUpdate: false);
        vehicle.SetMaximumHeat(80, triggerGhostUpdate: false);
        vehicle.SetCurrentHeat(0, triggerGhostUpdate: false);
        CharacterLevelManager.Instance.SetMaxMana(character, 90, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 90, sendPacket: false);

        // Defaults from AttachTestData / new columns are -1.
        vehicle.RestoreCombatStateFromDb(character);

        Assert.AreEqual(500, vehicle.GetCurrentHP());
        Assert.AreEqual(200, vehicle.CurrentShield);
        Assert.AreEqual(0, vehicle.CurrentHeat);
        Assert.AreEqual(90, CharacterLevelManager.Instance.GetCurrentMana(CharCoid));
    }

    [TestMethod]
    public void RestoreCombatStateFromDb_ClampsAboveMax()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetCurrentHP(100, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetMaximumShield(50, triggerGhostUpdate: false);
        vehicle.SetCurrentShield(50, triggerGhostUpdate: false);
        CharacterLevelManager.Instance.SetMaxMana(character, 30, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 30, sendPacket: false);

        vehicle.SetDbCombatStateForTests(currentHP: 9999, currentShield: 9999, currentPower: 9999, currentHeat: 0);

        vehicle.RestoreCombatStateFromDb(character);

        Assert.AreEqual(100, vehicle.GetCurrentHP());
        Assert.AreEqual(50, vehicle.CurrentShield);
        Assert.AreEqual(30, CharacterLevelManager.Instance.GetCurrentMana(CharCoid));
    }

    [TestMethod]
    public void EndCharacterSession_SnapshotIncludesCombatState()
    {
        var map = CreateMap(712);
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        vehicle.SetCurrentHP(88, triggerGhostUpdate: false, notifyOwnerHud: false);
        vehicle.SetMaximumShield(100, triggerGhostUpdate: false);
        vehicle.SetCurrentShield(17, triggerGhostUpdate: false);
        vehicle.SetMaximumHeat(40, triggerGhostUpdate: false);
        vehicle.SetCurrentHeat(9, triggerGhostUpdate: false);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);

        CharacterLevelManager.Instance.SetMaxMana(character, 55, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 18, sendPacket: false);

        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(vehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;
        connection.CurrentCharacter = character;
        connection.EndCharacterSession();

        Assert.AreEqual(1, recording.Saves.Count);
        var snap = recording.Saves[0];
        Assert.AreEqual(88, snap.CurrentHP);
        Assert.AreEqual(17, snap.CurrentShield);
        Assert.AreEqual(18, snap.CurrentPower);
        Assert.AreEqual(9, snap.CurrentHeat);
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

    private sealed class RecordingWorldStatePersistence : ICharacterWorldStatePersistence
    {
        public List<CharacterWorldStateSnapshot> Saves { get; } = new();

        public void Save(CharacterWorldStateSnapshot snapshot) => Saves.Add(snapshot);
    }

    private sealed class FakeWorldStateStore : CharacterWorldStatePersistence.IWorldStateStore
    {
        public Dictionary<long, CharacterData> Characters { get; } = new();
        public Dictionary<long, VehicleData> Vehicles { get; } = new();
        public int SaveChangesCalls { get; private set; }

        public CharacterData FindCharacter(long coid) =>
            Characters.TryGetValue(coid, out var c) ? c : null;

        public VehicleData FindVehicle(long coid) =>
            Vehicles.TryGetValue(coid, out var v) ? v : null;

        public void SaveChanges() => SaveChangesCalls++;
    }
}
