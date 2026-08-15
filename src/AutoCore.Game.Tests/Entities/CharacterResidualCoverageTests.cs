using System.Runtime.CompilerServices;
using AutoCore.Database.Char.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

/// <summary>
/// Pure residual Character surface: station mark, progress clamps, cargo capacity,
/// ghost create, identity helpers. EF LoadFromDB / LoadCurrentVehicle / LoadSkills excluded.
/// </summary>
[TestClass]
public class CharacterResidualCoverageTests
{
    [TestMethod]
    public void PropertyDefaults_WithoutDbData()
    {
        var character = new Character();
        Assert.AreEqual(-1, character.LastTownId);
        Assert.AreEqual(-1, character.LastStationMapId);
        Assert.AreEqual(-1, character.LastStationId);
        Assert.AreEqual((byte)1, character.Level);
        Assert.AreEqual((byte)1, character.GetLevel());
        Assert.AreEqual(0, character.Experience);
        Assert.AreEqual(0, character.SkillPoints);
        Assert.AreEqual(0, character.AttributePoints);
        Assert.AreEqual(0, character.ResearchPoints);
        Assert.AreEqual(-1, character.ClanId);
        Assert.AreEqual(-1, character.ClanRank);
        Assert.IsNull(character.ClanName);
        Assert.AreSame(character, character.GetAsCharacter());
        Assert.AreSame(character, character.GetSuperCharacter(false));
        Assert.AreSame(character, character.GetSuperCharacter(true));
        Assert.IsNull(character.EnsureLogicVariables());
        Assert.AreEqual(100, character.QuickBarItemCoids.Length);
        Assert.AreEqual(-1L, character.QuickBarItemCoids[0]);
    }

    [TestMethod]
    public void SetLastRepairStation_RuntimeAndDbAndPose()
    {
        var character = new Character();
        character.SetCoid(50, true);
        character.AttachTestDataForTests("Pilot");

        Assert.AreEqual(-1, character.GetLastStationId());
        Assert.AreEqual(-1, character.GetLastStationMapId());
        Assert.IsFalse(character.TryGetLastStationPose(out _, out _));

        character.SetLastRepairStation(7, 12);
        Assert.AreEqual(7, character.GetLastStationId());
        Assert.AreEqual(12, character.GetLastStationMapId());
        Assert.IsFalse(character.TryGetLastStationPose(out _, out _));

        var pos = new Vector3(1, 2, 3);
        var rot = new Quaternion(0, 0.5f, 0, 0.5f);
        character.SetLastRepairStation(8, 13, pos, rot);
        Assert.AreEqual(8, character.GetLastStationId());
        Assert.AreEqual(13, character.GetLastStationMapId());
        Assert.IsTrue(character.TryGetLastStationPose(out var gotPos, out var gotRot));
        Assert.AreEqual(1f, gotPos.X);
        Assert.AreEqual(0.5f, gotRot.Y);

        // DB row receives ids
        Assert.AreEqual(8, character.LastStationId);
        Assert.AreEqual(13, character.LastStationMapId);
    }

    [TestMethod]
    public void SetLastRepairStation_WithoutDbData_StillTracksRuntime()
    {
        var character = new Character();
        character.SetLastRepairStation(1, 2, new Vector3(9, 0, 0));
        Assert.AreEqual(1, character.GetLastStationId());
        Assert.AreEqual(2, character.GetLastStationMapId());
        Assert.IsTrue(character.TryGetLastStationPose(out var p, out _));
        Assert.AreEqual(9f, p.X);
    }

    [TestMethod]
    public void ProgressClamps_AndToProgressSnapshot()
    {
        var character = new Character();
        character.SetCoid(1, true);
        character.AttachTestDataForTests();

        character.SetLevel(0);
        Assert.AreEqual((byte)1, character.Level);
        character.SetLevel(12);
        Assert.AreEqual((byte)12, character.Level);

        character.SetSkillPoints(-3);
        Assert.AreEqual((short)0, character.SkillPoints);
        character.SetAttributePoints(-1);
        Assert.AreEqual((short)0, character.AttributePoints);
        character.SetResearchPoints(-9);
        Assert.AreEqual((short)0, character.ResearchPoints);

        character.SetAttributeTech(4);
        character.SetAttributeCombat(5);
        character.SetAttributeTheory(6);
        character.SetAttributePerception(7);
        character.SetExperience(100);
        character.SetSkillPoints(2);
        character.SetAttributePoints(3);
        character.SetResearchPoints(1);

        var snap = character.ToProgressSnapshot();
        Assert.AreEqual((byte)12, snap.Level);
        Assert.AreEqual(100, snap.Experience);
        Assert.AreEqual((short)2, snap.SkillPoints);
        Assert.AreEqual((short)3, snap.AttributePoints);
        Assert.AreEqual((short)1, snap.ResearchPoints);
        Assert.AreEqual((short)4, snap.AttributeTech);
        Assert.AreEqual((short)5, snap.AttributeCombat);
        Assert.AreEqual((short)6, snap.AttributeTheory);
        Assert.AreEqual((short)7, snap.AttributePerception);
    }

    [TestMethod]
    public void SetAttributes_WithoutDbData_UsesFallbackFields()
    {
        var character = new Character();
        character.SetAttributeTech(3);
        character.SetAttributeCombat(4);
        character.SetAttributeTheory(5);
        character.SetAttributePerception(6);
        Assert.AreEqual((short)3, character.AttributeTech);
        Assert.AreEqual((short)4, character.AttributeCombat);
        Assert.AreEqual((short)5, character.AttributeTheory);
        Assert.AreEqual((short)6, character.AttributePerception);

        var snap = character.ToProgressSnapshot();
        Assert.AreEqual((short)3, snap.AttributeTech);
    }

    [TestMethod]
    public void ApplyCargoCapacityFromCurrentVehicle_DefaultWithoutVehicle()
    {
        var character = new Character();
        character.SetCoid(2, true);
        character.AttachTestDataForTests();
        // persist:false — avoid InventoryPersistence CharContext / MySQL in unit tests
        character.ApplyCargoCapacityFromCurrentVehicle(persist: false);
        // Default chassis slots = 1 → 6x13 cargo
        Assert.IsTrue(character.Inventory.Width >= 1);
        Assert.IsTrue(character.Inventory.PageCount >= 1);
    }

    [TestMethod]
    public void CreateGhost_Idempotent()
    {
        var character = new Character();
        Assert.IsNull(character.Ghost);
        character.CreateGhost();
        Assert.IsInstanceOfType(character.Ghost, typeof(GhostCharacter));
        var first = character.Ghost;
        character.CreateGhost();
        Assert.AreSame(first, character.Ghost);
    }

    [TestMethod]
    public void SetOwningConnection_AndVehicleTestHooks()
    {
        var character = new Character();
        var conn = new TNLConnection();
        character.SetOwningConnection(conn);
        Assert.AreSame(conn, character.OwningConnection);

        var vehicle = new Vehicle();
        character.SetCurrentVehicleForTests(vehicle);
        Assert.AreSame(vehicle, character.CurrentVehicle);

        var inv = new InventoryManager(new RecordingInventoryPersistence());
        character.AttachInventoryForTests(inv);
        Assert.AreSame(inv, character.Inventory);

        character.AttachTestDataForTests("Named");
        character.SetLastTownIdForTests(44);
        Assert.AreEqual("Named", character.Name);
        Assert.AreEqual(44, character.LastTownId);
        Assert.IsTrue(float.IsNaN(character.GetDbPositionXForTests()) || character.GetDbPositionXForTests() == 0f);
    }

    [TestMethod]
    public void CaptureWorldStateToDb_NullWithoutDbData()
    {
        var character = new Character();
        Assert.IsNull(character.CaptureWorldStateToDb());
    }

    [TestMethod]
    public void CaptureWorldStateToDb_UsesCharacterPoseWhenNoMap()
    {
        var character = new Character();
        character.SetCoid(3, true);
        character.AttachTestDataForTests();
        character.Position = new Vector3(10, 20, 30);
        character.Rotation = new Quaternion(0, 0, 0, 1);

        var snap = character.CaptureWorldStateToDb();
        Assert.IsTrue(snap.HasValue);
        Assert.AreEqual(10f, snap.Value.PositionX);
        Assert.AreEqual(20f, snap.Value.PositionY);
        Assert.AreEqual(30f, snap.Value.PositionZ);
        Assert.AreEqual(3L, snap.Value.CharacterCoid);
    }

    [TestMethod]
    public void WriteToPacket_CreateCharacter_FillsAppearance()
    {
        var character = MakeCharacterWithCloneBase(4, "WireName");
        character.SetLevel(8);
        character.GMLevel = 2;

        var db = typeof(Character)
            .GetProperty("DBData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(character) as CharacterData;
        Assert.IsNotNull(db);
        db!.HeadId = 1;
        db.BodyId = 2;
        db.HairId = 3;
        db.EyesId = 4;
        db.MouthId = 5;
        db.HelmetId = 6;
        db.PrimaryColor = 0x111111;
        db.SecondaryColor = 0x222222;
        db.ScaleOffset = 0.1f;
        db.ActiveVehicleCoid = 999;

        var packet = new CreateCharacterPacket();
        character.WriteToPacket(packet);

        Assert.AreEqual("WireName", packet.Name);
        Assert.AreEqual((byte)8, packet.Level);
        Assert.AreEqual(2, packet.GMLevel);
        Assert.AreEqual(1, packet.HeadId);
        Assert.AreEqual(2, packet.BodyId);
        Assert.AreEqual(999L, packet.CurrentVehicleCoid);
        Assert.AreEqual(0.1f, packet.CharacterScaleOffset);
    }

    [TestMethod]
    public void WriteToPacket_Extended_FillsQuestsSkillsLockerCreditsCleared()
    {
        var character = MakeCharacterWithCloneBase(5, "Ext");
        character.SetCredits(5000);
        character.SetExperience(2500);
        character.SetLevel(2);
        character.SetSkillPoints(3);
        character.SetAttributePoints(4);
        character.LearnedSkills[10] = 2;
        character.LearnedSkills[20] = 1;
        character.CompletedMissionIds.Add(50);
        character.QuickBarItemCoids[0] = 123;
        character.QuickBarSkills[0] = 7;

        var packet = new CreateCharacterExtendedPacket();
        character.WriteToPacket(packet);

        Assert.AreEqual(0L, packet.Credits, "login path clears credits on create packet");
        Assert.AreEqual(2500, packet.XP,
            "Create decode assigns m_lXP absolutely then AddXP(0). Leaving 0 makes the HUD interpolate 0→total.");
        Assert.AreEqual((byte)2, packet.Level);
        Assert.AreEqual((short)3, packet.SkillPoints);
        Assert.AreEqual((short)4, packet.AttributePoints);
        Assert.AreEqual(2, packet.NumSkills);
        Assert.AreEqual(1, packet.NumCompletedQuests);
        Assert.AreEqual(123L, packet.QuickBarItemCoids[0]);
        Assert.AreEqual(7, packet.QuickBarSkills[0]);
    }

    private static Character MakeCharacterWithCloneBase(long coid, string name)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.AttachTestDataForTests(name);

        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific
        {
            CloneBaseId = 1,
            Type = (int)CloneBaseObjectType.Character,
            BaseValue = 0,
        };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            MaxHitPoint = 100,
            MaxUses = 0,
        };
        character.AssignCloneBaseForTests(clone);
        return character;
    }
}
