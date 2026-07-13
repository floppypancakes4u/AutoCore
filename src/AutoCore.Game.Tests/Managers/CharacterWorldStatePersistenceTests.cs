using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using global::TNL.Entities;
// CharacterQuest lives under Structures (mission active row).

/// <summary>
/// Recording-persistence + session-end ownership tests for world-state logout save.
/// </summary>
[TestClass]
public class CharacterWorldStatePersistenceTests
{
    private const long CharCoid = 9_040_000_101L;
    private const long VehicleCoid = 9_040_000_102L;

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.WorldStatePersistenceForTests = null;
        TNLConnection.MissionFlushForTests = null;
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
        MissionPersistence.Instance.ResetPersistenceForTests();
    }

    [TestMethod]
    public void PersistFromCharacter_CapturesAndSavesViaPersistence()
    {
        var map = CreateMap(700);
        var character = CreateCharacterWithVehicle(map);
        character.CurrentVehicle.Position = new Vector3(11f, 22f, 33f);
        character.CurrentVehicle.Rotation = new Quaternion(0f, 0.5f, 0f, 0.5f);

        var recording = new RecordingWorldStatePersistence();
        CharacterWorldStatePersistence.PersistFromCharacter(character, recording);

        Assert.AreEqual(1, recording.Saves.Count);
        var snap = recording.Saves[0];
        Assert.AreEqual(CharCoid, snap.CharacterCoid);
        Assert.AreEqual(700, snap.ContinentId);
        Assert.AreEqual(11f, snap.PositionX);
        Assert.AreEqual(22f, snap.PositionY);
        Assert.AreEqual(33f, snap.PositionZ);
        Assert.AreEqual(0.5f, snap.RotationY);
        Assert.AreEqual(0.5f, snap.RotationW);
        Assert.AreEqual(VehicleCoid, snap.VehicleCoid);
        Assert.AreEqual(700, character.LastTownId);
    }

    [TestMethod]
    public void PersistFromCharacter_NullCharacter_IsSafe()
    {
        CharacterWorldStatePersistence.PersistFromCharacter(null, new RecordingWorldStatePersistence());
    }

    [TestMethod]
    public void PersistFromCharacter_NoDbData_DoesNotSave()
    {
        var character = new Character();
        character.SetCoid(CharCoid, true);
        // No AttachTestDataForTests → CaptureWorldStateToDb returns null.

        var recording = new RecordingWorldStatePersistence();
        CharacterWorldStatePersistence.PersistFromCharacter(character, recording);

        Assert.AreEqual(0, recording.Saves.Count);
    }

    [TestMethod]
    public void PersistFromCharacter_DefaultPersistence_UsesInstanceWhenNull()
    {
        // Explicitly pass null persistence so the Instance path is taken; Save early-returns
        // for invalid coid without opening a database.
        var character = new Character();
        character.SetCoid(0, true);
        character.AttachTestDataForTests();
        // DBData.Coid is 0 from SetCoid(0) / AttachTestData uses ObjectId.Coid which is 0.
        // Snapshot CharacterCoid <= 0 causes Save to no-op before EF.
        CharacterWorldStatePersistence.PersistFromCharacter(character, persistence: null);
    }

    [TestMethod]
    public void Save_InvalidCharacterCoid_IsNoOp()
    {
        CharacterWorldStatePersistence.Instance.Save(new CharacterWorldStateSnapshot(
            CharacterCoid: 0,
            ContinentId: 1,
            PositionX: 1, PositionY: 2, PositionZ: 3,
            RotationX: 0, RotationY: 0, RotationZ: 0, RotationW: 1,
            VehicleCoid: -1));
    }

    [TestMethod]
    public void Save_WithStoreFactory_DelegatesToSaveWithStore()
    {
        var character = new CharacterData { Coid = CharCoid };
        var vehicle = new VehicleData { Coid = VehicleCoid };
        var store = new FakeWorldStateStore
        {
            Characters = { [CharCoid] = character },
            Vehicles = { [VehicleCoid] = vehicle }
        };

        var persistence = new CharacterWorldStatePersistence
        {
            StoreFactoryForTests = () => store
        };

        try
        {
            persistence.Save(SampleSnapshot(characterCoid: CharCoid, vehicleCoid: VehicleCoid, continentId: 321));

            Assert.AreEqual(1, store.SaveChangesCalls);
            Assert.AreEqual(321, character.LastTownId);
            Assert.AreEqual(10f, vehicle.PositionX);
        }
        finally
        {
            // no static state on Instance for this instance-local factory
        }
    }

    [TestMethod]
    public void Save_StoreFactoryThrows_LogsAndRethrows()
    {
        var persistence = new CharacterWorldStatePersistence
        {
            StoreFactoryForTests = () => throw new InvalidOperationException("store boom")
        };

        Assert.ThrowsException<InvalidOperationException>(() =>
            persistence.Save(SampleSnapshot(characterCoid: CharCoid)));
    }

    [TestMethod]
    public void Save_ProductionSavePath_InvokesProductionSaveHook()
    {
        var called = false;
        CharacterWorldStateSnapshot seen = default;
        var persistence = new CharacterWorldStatePersistence
        {
            // No StoreFactoryForTests → ProductionSave branch.
            ProductionSave = snap =>
            {
                called = true;
                seen = snap;
            }
        };

        var snapshot = SampleSnapshot(characterCoid: CharCoid, continentId: 44);
        persistence.Save(snapshot);

        Assert.IsTrue(called);
        Assert.AreEqual(CharCoid, seen.CharacterCoid);
        Assert.AreEqual(44, seen.ContinentId);
    }

    [TestMethod]
    public void SaveWithStore_NullStore_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            CharacterWorldStatePersistence.SaveWithStore(null, SampleSnapshot()));
    }

    [TestMethod]
    public void SaveWithStore_InvalidCoid_IsNoOp()
    {
        var store = new FakeWorldStateStore();
        CharacterWorldStatePersistence.SaveWithStore(store, SampleSnapshot(characterCoid: 0));
        Assert.AreEqual(0, store.SaveChangesCalls);
    }

    [TestMethod]
    public void SaveWithStore_CharacterMissing_DoesNotSaveChanges()
    {
        var store = new FakeWorldStateStore();
        CharacterWorldStatePersistence.SaveWithStore(store, SampleSnapshot());
        Assert.AreEqual(0, store.SaveChangesCalls);
    }

    [TestMethod]
    public void SaveWithStore_CharacterAndVehicle_AppliesPoseAndSaves()
    {
        var character = new CharacterData { Coid = CharCoid, LastTownId = 1 };
        var vehicle = new VehicleData { Coid = VehicleCoid };
        var store = new FakeWorldStateStore
        {
            Characters = { [CharCoid] = character },
            Vehicles = { [VehicleCoid] = vehicle }
        };

        var snapshot = SampleSnapshot(characterCoid: CharCoid, vehicleCoid: VehicleCoid, continentId: 888);
        CharacterWorldStatePersistence.SaveWithStore(store, snapshot);

        Assert.AreEqual(1, store.SaveChangesCalls);
        Assert.AreEqual(888, character.LastTownId);
        Assert.AreEqual(10f, character.PositionX);
        Assert.AreEqual(20f, character.PositionY);
        Assert.AreEqual(30f, character.PositionZ);
        Assert.AreEqual(0.1f, character.RotationX);
        Assert.AreEqual(0.9f, character.RotationW);
        Assert.AreEqual(10f, vehicle.PositionX);
        Assert.AreEqual(30f, vehicle.PositionZ);
        Assert.AreEqual(0.9f, vehicle.RotationW);
    }

    [TestMethod]
    public void SaveWithStore_VehicleMissing_StillSavesCharacter()
    {
        var character = new CharacterData { Coid = CharCoid };
        var store = new FakeWorldStateStore
        {
            Characters = { [CharCoid] = character }
        };

        CharacterWorldStatePersistence.SaveWithStore(
            store,
            SampleSnapshot(characterCoid: CharCoid, vehicleCoid: VehicleCoid, continentId: 50));

        Assert.AreEqual(1, store.SaveChangesCalls);
        Assert.AreEqual(50, character.LastTownId);
        Assert.AreEqual(10f, character.PositionX);
    }

    [TestMethod]
    public void SaveWithStore_NoVehicleCoid_SkipsVehicleLookup()
    {
        var character = new CharacterData { Coid = CharCoid };
        var store = new FakeWorldStateStore
        {
            Characters = { [CharCoid] = character }
        };

        CharacterWorldStatePersistence.SaveWithStore(
            store,
            SampleSnapshot(characterCoid: CharCoid, vehicleCoid: -1, continentId: 12));

        Assert.AreEqual(1, store.SaveChangesCalls);
        Assert.AreEqual(0, store.VehicleLookups);
        Assert.AreEqual(12, character.LastTownId);
    }

    [TestMethod]
    public void ApplyToCharacter_Null_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            CharacterWorldStatePersistence.ApplyToCharacter(null, SampleSnapshot()));
    }

    [TestMethod]
    public void ApplyToVehicle_Null_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            CharacterWorldStatePersistence.ApplyToVehicle(null, SampleSnapshot()));
    }

    [TestMethod]
    public void ApplyToCharacterAndVehicle_WritesAllFields()
    {
        var character = new CharacterData();
        var vehicle = new VehicleData();
        var snap = new CharacterWorldStateSnapshot(
            1, 99, 1.5f, 2.5f, 3.5f, 0.2f, 0.3f, 0.4f, 0.8f, 2);

        CharacterWorldStatePersistence.ApplyToCharacter(character, snap);
        CharacterWorldStatePersistence.ApplyToVehicle(vehicle, snap);

        Assert.AreEqual(99, character.LastTownId);
        Assert.AreEqual(1.5f, character.PositionX);
        Assert.AreEqual(2.5f, character.PositionY);
        Assert.AreEqual(3.5f, character.PositionZ);
        Assert.AreEqual(0.2f, character.RotationX);
        Assert.AreEqual(0.3f, character.RotationY);
        Assert.AreEqual(0.4f, character.RotationZ);
        Assert.AreEqual(0.8f, character.RotationW);

        Assert.AreEqual(1.5f, vehicle.PositionX);
        Assert.AreEqual(2.5f, vehicle.PositionY);
        Assert.AreEqual(3.5f, vehicle.PositionZ);
        Assert.AreEqual(0.2f, vehicle.RotationX);
        Assert.AreEqual(0.3f, vehicle.RotationY);
        Assert.AreEqual(0.4f, vehicle.RotationZ);
        Assert.AreEqual(0.8f, vehicle.RotationW);
        // Combat fields default to -1 when omitted from the snapshot constructor.
        Assert.AreEqual(-1, vehicle.CurrentHP);
        Assert.AreEqual(-1, vehicle.CurrentShield);
        Assert.AreEqual(-1, vehicle.CurrentPower);
        Assert.AreEqual(-1, vehicle.CurrentHeat);
    }

    [TestMethod]
    public void EndCharacterSession_OwningConnection_PersistsThenUnregisters()
    {
        var map = CreateMap(701);
        var character = CreateCharacterWithVehicle(map);
        character.CurrentVehicle.Position = new Vector3(9f, 8f, 7f);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        var connection = character.OwningConnection;
        connection.CurrentCharacter = character;
        connection.EndCharacterSession();

        Assert.AreEqual(1, recording.Saves.Count);
        Assert.AreEqual(701, recording.Saves[0].ContinentId);
        Assert.AreEqual(9f, recording.Saves[0].PositionX);
        Assert.IsNull(connection.CurrentCharacter);
        Assert.IsNull(character.OwningConnection);
        Assert.IsNull(character.Map);
        Assert.IsNull(ObjectManager.Instance.GetCharacter(CharCoid));
        Assert.IsNull(ObjectManager.Instance.GetVehicle(VehicleCoid));
    }

    [TestMethod]
    public void EndCharacterSession_PersistenceThrows_StillTearsDown()
    {
        var map = CreateMap(705);
        var character = CreateCharacterWithVehicle(map);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        TNLConnection.WorldStatePersistenceForTests = new ThrowingWorldStatePersistence();

        var connection = character.OwningConnection;
        connection.CurrentCharacter = character;
        connection.EndCharacterSession();

        Assert.IsNull(connection.CurrentCharacter);
        Assert.IsNull(character.OwningConnection);
        Assert.IsNull(ObjectManager.Instance.GetCharacter(CharCoid));
    }

    [TestMethod]
    public void EndCharacterSession_OwningConnectionNull_StillOnMap_TreatsAsOwnerAndPersists()
    {
        var map = CreateMap(706);
        var character = CreateCharacterWithVehicle(map);
        character.SetOwningConnection(null);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        var connection = new TNLConnection();
        connection.CurrentCharacter = character;
        connection.EndCharacterSession();

        Assert.AreEqual(1, recording.Saves.Count);
        Assert.IsNull(connection.CurrentCharacter);
        Assert.IsNull(ObjectManager.Instance.GetCharacter(CharCoid));
    }

    /// <summary>
    /// Sector owner disconnect persists + clears Map/owner. Global connection still holds
    /// CurrentCharacter and must not re-persist (would wipe town on-foot pose with vehicle).
    /// </summary>
    [TestMethod]
    public void EndCharacterSession_AfterOwnerTeardown_SecondConnectionDoesNotRepersist()
    {
        var map = CreateMap(709);
        var character = CreateCharacterWithVehicle(map);
        character.Position = new Vector3(100f, 12f, 200f);
        character.CurrentVehicle.Position = new Vector3(1f, -50f, 2f);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        var sector = character.OwningConnection;
        sector.CurrentCharacter = character;

        // Simulate Global connection that also references the same character.
        var global = new TNLConnection();
        global.CurrentCharacter = character;

        sector.EndCharacterSession();
        Assert.AreEqual(1, recording.Saves.Count);
        Assert.IsNull(character.Map);
        Assert.IsNull(character.OwningConnection);

        // Global disconnects second — must not write another snapshot.
        global.EndCharacterSession();
        Assert.AreEqual(1, recording.Saves.Count, "Second disconnect must not re-persist world state.");
        Assert.IsNull(global.CurrentCharacter);
    }

    /// <summary>
    /// Town logout: sector must persist the walked character pose, not the garage vehicle pose.
    /// </summary>
    [TestMethod]
    public void EndCharacterSession_OnTown_PersistsCharacterPoseNotVehicle()
    {
        var map = CreateMap(710, isTown: true);
        var character = CreateCharacterWithVehicle(map);
        character.Position = new Vector3(100.5f, 12f, 200.75f);
        character.Rotation = new Quaternion(0f, 0.707f, 0f, 0.707f);
        character.CurrentVehicle.Position = new Vector3(1f, -50f, 2f);
        character.CurrentVehicle.Rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        var connection = character.OwningConnection;
        connection.CurrentCharacter = character;
        connection.EndCharacterSession();

        Assert.AreEqual(1, recording.Saves.Count);
        var snap = recording.Saves[0];
        Assert.AreEqual(710, snap.ContinentId);
        Assert.AreEqual(100.5f, snap.PositionX);
        Assert.AreEqual(12f, snap.PositionY);
        Assert.AreEqual(200.75f, snap.PositionZ);
        Assert.AreEqual(0.707f, snap.RotationY);
        Assert.AreEqual(0.707f, snap.RotationW);
        Assert.AreNotEqual(1f, snap.PositionX);
        Assert.AreNotEqual(-50f, snap.PositionY);
    }

    [TestMethod]
    public void EndCharacterSession_OnField_PersistsVehiclePose()
    {
        var map = CreateMap(711, isTown: false);
        var character = CreateCharacterWithVehicle(map);
        character.Position = new Vector3(1f, 1f, 1f);
        character.CurrentVehicle.Position = new Vector3(88f, 9f, 77f);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        character.OwningConnection.CurrentCharacter = character;
        character.OwningConnection.EndCharacterSession();

        Assert.AreEqual(1, recording.Saves.Count);
        Assert.AreEqual(88f, recording.Saves[0].PositionX);
        Assert.AreEqual(9f, recording.Saves[0].PositionY);
        Assert.AreEqual(77f, recording.Saves[0].PositionZ);
    }

    [TestMethod]
    public void EndCharacterSession_OrphanNoMap_DoesNotPersist()
    {
        // Already torn down: no owner, no map — Global-style leftover reference only.
        var character = CreateCharacterWithVehicle(CreateMap(712));
        character.SetMap(null);
        character.CurrentVehicle?.SetMap(null);
        character.SetOwningConnection(null);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        var connection = new TNLConnection();
        connection.CurrentCharacter = character;
        connection.EndCharacterSession();

        Assert.AreEqual(0, recording.Saves.Count);
        Assert.IsNull(connection.CurrentCharacter);
    }

    [TestMethod]
    public void EndCharacterSession_MissionFlushThrows_StillTearsDown()
    {
        var map = CreateMap(713);
        var character = CreateCharacterWithVehicle(map);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        TNLConnection.WorldStatePersistenceForTests = new RecordingWorldStatePersistence();
        // Production MissionPersistenceQueue swallows row failures; inject a throwing flush so
        // the EndCharacterSession catch + continue teardown path is covered.
        TNLConnection.MissionFlushForTests = () =>
            throw new InvalidOperationException("simulated mission flush failure");

        var connection = character.OwningConnection;
        connection.CurrentCharacter = character;
        connection.EndCharacterSession();

        Assert.IsNull(connection.CurrentCharacter);
        Assert.IsNull(character.OwningConnection);
        Assert.IsNull(ObjectManager.Instance.GetCharacter(CharCoid));
    }

    [TestMethod]
    public void EndCharacterSession_TeardownStepThrows_StillCompletesSession()
    {
        var map = CreateMap(714);
        var character = CreateCharacterWithVehicle(map);
        // Ghost whose SetParent throws exercises SafeTeardownStep catch during ClearGhost.
        character.SetGhost(new ThrowingOnParentClearGhost());
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        TNLConnection.WorldStatePersistenceForTests = new RecordingWorldStatePersistence();

        var connection = character.OwningConnection;
        connection.CurrentCharacter = character;
        connection.EndCharacterSession();

        Assert.IsNull(connection.CurrentCharacter);
        Assert.IsNull(character.OwningConnection);
        Assert.IsNull(ObjectManager.Instance.GetCharacter(CharCoid));
    }

    [TestMethod]
    public void EndCharacterSession_NonOwningConnection_DoesNotTeardownOrPersist()
    {
        var map = CreateMap(702);
        var owner = new TNLConnection();
        owner.SetGhostFrom(true);
        owner.SetGhostTo(false);

        var character = CreateCharacterWithVehicle(map, owningConnection: owner);
        character.CurrentVehicle.Position = new Vector3(1f, 2f, 3f);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        var other = new TNLConnection();
        other.CurrentCharacter = character;
        other.EndCharacterSession();

        Assert.AreEqual(0, recording.Saves.Count, "Non-owning connection must not persist.");
        Assert.IsNull(other.CurrentCharacter);
        Assert.AreSame(owner, character.OwningConnection);
        Assert.AreSame(map, character.Map);
        Assert.IsNotNull(ObjectManager.Instance.GetCharacter(CharCoid));
        Assert.IsNotNull(ObjectManager.Instance.GetVehicle(VehicleCoid));
    }

    [TestMethod]
    public void EndCharacterSession_NullCurrentCharacter_IsSafe()
    {
        var connection = new TNLConnection();
        connection.EndCharacterSession();
        Assert.IsNull(connection.CurrentCharacter);
    }

    [TestMethod]
    public void OnConnectionTerminated_InvokesEndCharacterSession()
    {
        var map = CreateMap(707);
        var character = CreateCharacterWithVehicle(map);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        var connection = character.OwningConnection;
        connection.CurrentCharacter = character;
        connection.OnConnectionTerminated(TerminationReason.ReasonRemoteDisconnectPacket, "test");

        Assert.AreEqual(1, recording.Saves.Count);
        Assert.IsNull(connection.CurrentCharacter);
        Assert.IsNull(ObjectManager.Instance.GetCharacter(CharCoid));
    }

    [TestMethod]
    public void EndCharacterSession_UsesWorldStatePersistenceTestOverride()
    {
        var map = CreateMap(708);
        var character = CreateCharacterWithVehicle(map);
        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var recording = new RecordingWorldStatePersistence();
        TNLConnection.WorldStatePersistenceForTests = recording;

        var conn = character.OwningConnection;
        conn.CurrentCharacter = character;
        conn.EndCharacterSession();

        Assert.AreEqual(1, recording.Saves.Count);
        Assert.AreEqual(CharCoid, recording.Saves[0].CharacterCoid);
    }

    private static CharacterWorldStateSnapshot SampleSnapshot(
        long characterCoid = CharCoid,
        long vehicleCoid = VehicleCoid,
        int continentId = 100) =>
        new(
            characterCoid,
            continentId,
            10f, 20f, 30f,
            0.1f, 0f, 0f, 0.9f,
            vehicleCoid);

    private static Character CreateCharacterWithVehicle(SectorMap map, TNLConnection owningConnection = null)
    {
        var connection = owningConnection ?? new TNLConnection();
        if (owningConnection == null)
        {
            connection.SetGhostFrom(true);
            connection.SetGhostTo(false);
        }

        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);

        connection.CurrentCharacter = character;
        return character;
    }

    private static SectorMap CreateMap(int continentId, bool isTown = false)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = "test_map",
            DisplayName = "Test",
            IsTown = isTown,
            IsPersistent = true
        };
        return SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
    }

    private sealed class ThrowingOnParentClearGhost : AutoCore.Game.TNL.Ghost.GhostObject
    {
        public override void SetParent(ClonedObjectBase parent)
        {
            if (parent == null)
                throw new InvalidOperationException("simulated ClearGhost parent clear failure");
            base.SetParent(parent);
        }
    }

    private sealed class RecordingWorldStatePersistence : ICharacterWorldStatePersistence
    {
        public List<CharacterWorldStateSnapshot> Saves { get; } = new();

        public void Save(CharacterWorldStateSnapshot snapshot) => Saves.Add(snapshot);
    }

    private sealed class ThrowingWorldStatePersistence : ICharacterWorldStatePersistence
    {
        public void Save(CharacterWorldStateSnapshot snapshot) =>
            throw new InvalidOperationException("simulated persist failure");
    }

    private sealed class FakeWorldStateStore : CharacterWorldStatePersistence.IWorldStateStore
    {
        public Dictionary<long, CharacterData> Characters { get; } = new();
        public Dictionary<long, VehicleData> Vehicles { get; } = new();
        public int SaveChangesCalls { get; private set; }
        public int VehicleLookups { get; private set; }

        public CharacterData FindCharacter(long coid) =>
            Characters.TryGetValue(coid, out var c) ? c : null;

        public VehicleData FindVehicle(long coid)
        {
            VehicleLookups++;
            return Vehicles.TryGetValue(coid, out var v) ? v : null;
        }

        public void SaveChanges() => SaveChangesCalls++;
    }
}
