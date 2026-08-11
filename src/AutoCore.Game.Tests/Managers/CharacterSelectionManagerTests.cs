using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Login;
using AutoCore.Game.TNL;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

[TestClass]
public class CharacterSelectionManagerTests
{
    private string _dbName = null!;
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void Init()
    {
        _dbName = "char-sel-" + Guid.NewGuid().ToString("N");
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        CharacterSelectionManager.ResetForTests();
        CharacterSelectionManager.CreateContext = CreateContext;
        AssetManagerTestHelper.ClearRegisteredCloneBases();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        CharacterSelectionManager.ResetForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    private CharContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CharContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new CharContext(options);
    }

    private static TNLConnection CreateClient(uint accountId = 1, string name = "sel-user")
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        connection.Account = new Account { Id = accountId, Name = name, Level = 1 };
        return connection;
    }

    [TestMethod]
    public void ResolveChassisCargoPages_MissingClone_DefaultsToOne()
    {
        Assert.AreEqual(1, CharacterSelectionManager.ResolveChassisCargoPages(999_001));
    }

    [TestMethod]
    public void ResolveChassisCargoPages_UsesVehicleInventorySlotsClamped()
    {
        const int cbid = 42_100;
        AssetManagerTestHelper.RegisterVehicleCloneBase(cbid, inventorySlots: 3);
        Assert.AreEqual(3, CharacterSelectionManager.ResolveChassisCargoPages(cbid));

        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterVehicleCloneBase(cbid, inventorySlots: 99);
        // Max wire pages is 4.
        Assert.AreEqual(4, CharacterSelectionManager.ResolveChassisCargoPages(cbid));

        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterVehicleCloneBase(cbid, inventorySlots: 0);
        Assert.AreEqual(1, CharacterSelectionManager.ResolveChassisCargoPages(cbid));
    }

    [TestMethod]
    public void SendCharacterList_EmptyAccount_SendsNoCreatePackets()
    {
        var client = CreateClient(accountId: 10);
        CharacterSelectionManager.SendCharacterList(client);
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void SendCharacterList_MissingVehicleLoad_SkipsBrokenRows()
    {
        // Character row without vehicle/simpleobject graph → LoadCharacterForSelection returns null.
        using (var seed = CreateContext())
        {
            seed.Characters.Add(new CharacterData
            {
                Coid = 7001,
                AccountId = 11,
                Name = "Broken",
                Deleted = false
            });
            seed.SaveChanges();
        }

        var client = CreateClient(accountId: 11);
        CharacterSelectionManager.SendCharacterList(client);
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void SendCharacterList_Ss31CorruptedCharacterIdentity_SkipsWithoutCreatePackets()
    {
        // Live SS-31 incident: item coid collided with character coid and EnsureSimpleObject
        // overwrote simple_object.Type/CBID to Item. Character.LoadFromDB must refuse so
        // SendCharacter never ships a poison CreateCharacter (client AV 0x0080A62A).
        const uint accountId = 13;
        const long coid = 18274;

        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = coid,
                Type = (byte)CloneBaseObjectType.Item,
                CBID = 17774
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = accountId,
                Name = "Corrupted",
                Deleted = false,
                ActiveVehicleCoid = coid + 1
            });
            seed.SaveChanges();
        }

        var client = CreateClient(accountId);
        CharacterSelectionManager.SendCharacterList(client);
        Assert.AreEqual(0, _sent.Count, "corrupt character identity must not reach the client wire");
    }

    [TestMethod]
    public void SendCharacterList_Ss31CorruptedVehicleIdentity_SkipsWithoutCreatePackets()
    {
        // Character simple_object is fine, but the active vehicle row was clobbered to Weapon.
        const uint accountId = 14;
        const long charCoid = 19001;
        const long vehCoid = 19002;
        const int charCbid = 42_201;

        AssetManagerTestHelper.RegisterCharacterCloneBase(charCbid);

        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = charCoid,
                Type = (byte)CloneBaseObjectType.Character,
                CBID = charCbid
            });
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = vehCoid,
                Type = (byte)CloneBaseObjectType.Weapon,
                CBID = 1552
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = charCoid,
                AccountId = accountId,
                Name = "VehBroken",
                Deleted = false,
                ActiveVehicleCoid = vehCoid
            });
            seed.Vehicles.Add(new VehicleData
            {
                Coid = vehCoid,
                CharacterCoid = charCoid
            });
            seed.SaveChanges();
        }

        var client = CreateClient(accountId);
        CharacterSelectionManager.SendCharacterList(client);
        Assert.AreEqual(0, _sent.Count, "corrupt vehicle identity must not reach the client wire");
    }

    [TestMethod]
    public void SendCharacterList_Ss31PlaceholderIdentityRow_SkipsInsteadOfThrowing()
    {
        // Live SS-31 crash: a {Type=0, CBID=0} simple_object placeholder row reaches
        // LoadCloneBase(0), which throws InvalidOperationException out of SendCharacterList
        // (LoadCharacterForSelection has no catch). Character.LoadFromDB must refuse cbid<=0.
        const uint accountId = 15;
        const long coid = 20001;

        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = coid,
                Type = 0,
                CBID = 0
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = accountId,
                Name = "Placeholder",
                Deleted = false,
                ActiveVehicleCoid = coid + 1
            });
            seed.SaveChanges();
        }

        var client = CreateClient(accountId);
        CharacterSelectionManager.SendCharacterList(client);
        Assert.AreEqual(0, _sent.Count, "placeholder identity row must not throw or ship packets");
    }

    [TestMethod]
    public void SendCharacterList_Ss31WrongCloneBaseKind_SkipsWithoutCreatePackets()
    {
        // simple_object.Type says Character, but the CBID it points at resolves to a Vehicle
        // clonebase. LoadCloneBase succeeds (cbid is known), so the Type prefilter above cannot
        // catch this — only comparing the loaded clonebase kind after LoadCloneBase can.
        //
        // The active vehicle is seeded fully healthy (own simple_object + wheelset row) so the
        // skip below is provably caused by the character's kind mismatch, not by an unrelated
        // LoadCurrentVehicle failure (e.g. a missing vehicle/wheelset row).
        const uint accountId = 16;
        const long coid = 20101;
        const long vehCoid = 20102;
        const long wheelsetCoid = 20103;
        const int wrongKindCbid = 42_301;
        const int vehCbid = 42_306;
        const int wheelsetCbid = 42_307;

        AssetManagerTestHelper.RegisterVehicleCloneBase(wrongKindCbid);
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehCbid);
        AssetManagerTestHelper.RegisterWheelSetCloneBase(wheelsetCbid);

        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = coid,
                Type = (byte)CloneBaseObjectType.Character,
                CBID = wrongKindCbid
            });
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = vehCoid,
                Type = (byte)CloneBaseObjectType.Vehicle,
                CBID = vehCbid
            });
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = wheelsetCoid,
                Type = (byte)CloneBaseObjectType.WheelSet,
                CBID = wheelsetCbid
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = accountId,
                Name = "WrongKind",
                Deleted = false,
                ActiveVehicleCoid = vehCoid
            });
            seed.Vehicles.Add(new VehicleData
            {
                Coid = vehCoid,
                CharacterCoid = coid,
                Wheelset = wheelsetCoid
            });
            seed.SaveChanges();
        }

        var client = CreateClient(accountId);
        CharacterSelectionManager.SendCharacterList(client);
        Assert.AreEqual(0, _sent.Count, "clonebase-kind mismatch must not reach the client wire");
    }

    [TestMethod]
    public void SendCharacterList_Ss31VehiclePlaceholderIdentityRow_SkipsInsteadOfThrowing()
    {
        // Character row is healthy; the active vehicle row is the {Type=0, CBID=0} placeholder.
        const uint accountId = 17;
        const long charCoid = 20201;
        const long vehCoid = 20202;
        const int charCbid = 42_302;

        AssetManagerTestHelper.RegisterCharacterCloneBase(charCbid);

        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = charCoid,
                Type = (byte)CloneBaseObjectType.Character,
                CBID = charCbid
            });
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = vehCoid,
                Type = 0,
                CBID = 0
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = charCoid,
                AccountId = accountId,
                Name = "VehPlaceholder",
                Deleted = false,
                ActiveVehicleCoid = vehCoid
            });
            seed.Vehicles.Add(new VehicleData
            {
                Coid = vehCoid,
                CharacterCoid = charCoid
            });
            seed.SaveChanges();
        }

        var client = CreateClient(accountId);
        CharacterSelectionManager.SendCharacterList(client);
        Assert.AreEqual(0, _sent.Count, "placeholder vehicle identity must not throw or ship packets");
    }

    [TestMethod]
    public void SendCharacterList_Ss31VehicleWrongCloneBaseKind_SkipsWithoutCreatePackets()
    {
        // Character row is healthy; the active vehicle row's simple_object.Type says Vehicle,
        // but its CBID resolves to a Character clonebase.
        //
        // The vehicle's wheelset is seeded fully healthy (own simple_object row with a
        // registered WheelSet clonebase) so the skip below is provably caused by the vehicle's
        // own kind mismatch, not by the unconditional WheelSet.LoadFromDB call that runs right
        // after LoadCloneBase in Vehicle.LoadFromDB.
        const uint accountId = 18;
        const long charCoid = 20301;
        const long vehCoid = 20302;
        const long wheelsetCoid = 20303;
        const int charCbid = 42_303;
        const int wrongKindCbid = 42_304;
        const int wheelsetCbid = 42_308;

        AssetManagerTestHelper.RegisterCharacterCloneBase(charCbid);
        AssetManagerTestHelper.RegisterCharacterCloneBase(wrongKindCbid);
        AssetManagerTestHelper.RegisterWheelSetCloneBase(wheelsetCbid);

        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = charCoid,
                Type = (byte)CloneBaseObjectType.Character,
                CBID = charCbid
            });
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = vehCoid,
                Type = (byte)CloneBaseObjectType.Vehicle,
                CBID = wrongKindCbid
            });
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = wheelsetCoid,
                Type = (byte)CloneBaseObjectType.WheelSet,
                CBID = wheelsetCbid
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = charCoid,
                AccountId = accountId,
                Name = "VehWrongKind",
                Deleted = false,
                ActiveVehicleCoid = vehCoid
            });
            seed.Vehicles.Add(new VehicleData
            {
                Coid = vehCoid,
                CharacterCoid = charCoid,
                Wheelset = wheelsetCoid
            });
            seed.SaveChanges();
        }

        var client = CreateClient(accountId);
        CharacterSelectionManager.SendCharacterList(client);
        Assert.AreEqual(0, _sent.Count, "vehicle clonebase-kind mismatch must not reach the client wire");
    }

    [TestMethod]
    public void LoadFromDB_LegacyTypeZeroWithValidCbid_StillLoads()
    {
        // Pins the legacy-tolerance decision: Type==0 (never migrated) with a valid, positive
        // CBID must still load — only CBID<=0 and clonebase-kind mismatches are refused.
        const long coid = 20401;
        const int cbid = 42_305;

        AssetManagerTestHelper.RegisterCharacterCloneBase(cbid);

        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = coid,
                Type = 0,
                CBID = cbid
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = 19,
                Name = "LegacyTypeZero",
                Deleted = false
            });
            seed.SaveChanges();
        }

        using var context = CreateContext();
        var character = new AutoCore.Game.Entities.Character();
        var loaded = character.LoadFromDB(context, coid, isInCharacterSelection: true);

        Assert.IsTrue(loaded, "legacy Type=0 rows with a valid CBID must still load");
    }

    [TestMethod]
    public void DeleteCharacter_MarksOwnedCharacterDeleted()
    {
        const uint accountId = 12;
        const long coid = 8001;

        using (var seed = CreateContext())
        {
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = accountId,
                Name = "Deleteme",
                Deleted = false
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = 8002,
                AccountId = accountId + 1,
                Name = "OtherAccount",
                Deleted = false
            });
            seed.SaveChanges();
        }

        var client = CreateClient(accountId);
        CharacterSelectionManager.DeleteCharacter(client, coid);

        using var verify = CreateContext();
        Assert.IsTrue(verify.Characters.Single(c => c.Coid == coid).Deleted);
        Assert.IsFalse(verify.Characters.Single(c => c.Coid == 8002).Deleted);
    }

    [TestMethod]
    public void DeleteCharacter_WrongAccount_DoesNotDelete()
    {
        const long coid = 8100;
        using (var seed = CreateContext())
        {
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = 99,
                Name = "Protected",
                Deleted = false
            });
            seed.SaveChanges();
        }

        CharacterSelectionManager.DeleteCharacter(CreateClient(accountId: 1), coid);

        using var verify = CreateContext();
        Assert.IsFalse(verify.Characters.Single(c => c.Coid == coid).Deleted);
    }

    [TestMethod]
    public void CreateNewCharacter_DuplicateName_ReturnsFalse()
    {
        using (var seed = CreateContext())
        {
            seed.Characters.Add(new CharacterData
            {
                Coid = 9001,
                AccountId = 1,
                Name = "TakenName",
                Deleted = false
            });
            seed.SaveChanges();
        }

        var packet = new LoginNewCharacterPacket
        {
            CharacterName = "takenname",
            VehicleName = "fresh-veh",
            CBID = 1
        };

        var (ok, coid) = CharacterSelectionManager.CreateNewCharacter(CreateClient(), packet);
        Assert.IsFalse(ok);
        Assert.AreEqual(-1, coid);
    }

    [TestMethod]
    public void CreateNewCharacter_InvalidCbidWithoutBypass_ReturnsFalse()
    {
        // No clone base registered and AllowMissingCBID is false by default in test process.
        var packet = new LoginNewCharacterPacket
        {
            CharacterName = "NewChar",
            VehicleName = "NewVeh",
            CBID = 123_456_789
        };

        var (ok, coid) = CharacterSelectionManager.CreateNewCharacter(CreateClient(), packet);
        Assert.IsFalse(ok);
        Assert.AreEqual(-1, coid);
    }

    [TestMethod]
    public void ExtendCharacterList_MissingCoid_SendsNothing()
    {
        CharacterSelectionManager.ExtendCharacterList(CreateClient(), coid: 42_000);
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void CreateNewCharacter_DuplicateVehicleName_ReturnsFalse()
    {
        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = 9101,
                Type = (byte)CloneBaseObjectType.Character,
                CBID = 1
            });
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = 9102,
                Type = (byte)CloneBaseObjectType.Vehicle,
                CBID = 2
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = 9101,
                AccountId = 1,
                Name = "OwnerOfVeh",
                ActiveVehicleCoid = 9102,
                Deleted = false
            });
            seed.Vehicles.Add(new VehicleData
            {
                Coid = 9102,
                CharacterCoid = 9101,
                Name = "TakenVeh"
            });
            seed.SaveChanges();
        }

        var packet = new LoginNewCharacterPacket
        {
            CharacterName = "BrandNew",
            VehicleName = "takenveh",
            CBID = 1
        };

        var (ok, coid) = CharacterSelectionManager.CreateNewCharacter(CreateClient(), packet);
        Assert.IsFalse(ok);
        Assert.AreEqual(-1, coid);
    }

    [TestMethod]
    public void CreateNewCharacter_EmptyNameCollisionWithWhitespace_StillChecked()
    {
        using (var seed = CreateContext())
        {
            seed.Characters.Add(new CharacterData
            {
                Coid = 9201,
                AccountId = 2,
                Name = "  Spaced  ",
                Deleted = false
            });
            seed.SaveChanges();
        }

        var packet = new LoginNewCharacterPacket
        {
            CharacterName = "spaced",
            VehicleName = "veh-unique-xyz",
            CBID = 1
        };

        var (ok, coid) = CharacterSelectionManager.CreateNewCharacter(CreateClient(accountId: 3), packet);
        Assert.IsFalse(ok);
        Assert.AreEqual(-1, coid);
    }

    [TestMethod]
    public void DeleteCharacter_AlreadyDeleted_IsNoOp()
    {
        const long coid = 9301;
        using (var seed = CreateContext())
        {
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = 1,
                Name = "Gone",
                Deleted = true
            });
            seed.SaveChanges();
        }

        CharacterSelectionManager.DeleteCharacter(CreateClient(), coid);

        using var verify = CreateContext();
        Assert.IsTrue(verify.Characters.Single(c => c.Coid == coid).Deleted);
    }

    [TestMethod]
    public void SendCharacterList_Ss31Skip_IncrementsCorruptIdentitySkipCounter()
    {
        // Reuses the SS-31 corrupted-character-identity seed above; asserts the skip site
        // increments the operator-visible counter by exactly 1 (other tests share the process
        // counter, so assert delta rather than an absolute value).
        const uint accountId = 21;
        const long coid = 20501;

        using (var seed = CreateContext())
        {
            seed.SimpleObjects.Add(new SimpleObjectData
            {
                Coid = coid,
                Type = (byte)CloneBaseObjectType.Item,
                CBID = 17774
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = accountId,
                Name = "CounterCorrupted",
                Deleted = false,
                ActiveVehicleCoid = coid + 1
            });
            seed.SaveChanges();
        }

        var before = CharacterSelectionManager.CorruptIdentitySkipCount;

        var client = CreateClient(accountId);
        CharacterSelectionManager.SendCharacterList(client);

        var after = CharacterSelectionManager.CorruptIdentitySkipCount;
        Assert.AreEqual(1, after - before, "corrupt identity skip must increment the counter by exactly one");
    }

    [TestMethod]
    public void SendCharacterList_DeletedCharacters_AreSkipped()
    {
        using (var seed = CreateContext())
        {
            seed.Characters.Add(new CharacterData
            {
                Coid = 9401,
                AccountId = 20,
                Name = "LiveButBroken",
                Deleted = false
            });
            seed.Characters.Add(new CharacterData
            {
                Coid = 9402,
                AccountId = 20,
                Name = "Deleted",
                Deleted = true
            });
            seed.SaveChanges();
        }

        CharacterSelectionManager.SendCharacterList(CreateClient(accountId: 20));
        // Broken rows (no vehicle graph) produce no create packets; deleted skipped.
        Assert.AreEqual(0, _sent.Count);
    }
}

