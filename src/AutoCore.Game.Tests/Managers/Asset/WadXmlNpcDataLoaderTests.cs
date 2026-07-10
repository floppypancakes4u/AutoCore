using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Structures;

[TestClass]
public class WadXmlNpcDataLoaderTests
{
    private const string RealWadXmlPath = @"C:\Program Files (x86)\NetDevil\Auto Assault\wad.xml";

    private string _tempXmlPath;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempXmlPath = Path.Combine(Path.GetTempPath(), $"npc-data-{Guid.NewGuid():N}.xml");
        AssetManager.Instance.ClearTestNpcData();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (_tempXmlPath != null && File.Exists(_tempXmlPath))
            File.Delete(_tempXmlPath);
        AssetManager.Instance.ClearTestNpcData();
    }

    [TestMethod]
    public void LoadVehicleTemplates_ParsesRowFields()
    {
        File.WriteAllText(_tempXmlPath, """
            <wad>
              <tVehicleTemplate>
                <row IDVehicleTemplate="1"><IDVehicleTemplate>1</IDVehicleTemplate><CBIDVehicle>2069</CBIDVehicle><CBIDDriver>2071</CBIDDriver><CBIDWeaponTurret>2195</CBIDWeaponTurret><CBIDWeaponFront>-1</CBIDWeaponFront><CBIDArmor>-1</CBIDArmor><sinBaseLevel>2</sinBaseLevel><tinLootChance>10</tinLootChance><tinLootRolls>1</tinLootRolls><IDSkill1>-1</IDSkill1><tinSkillRank1>0</tinSkillRank1><strDescription>USE ME AND DIE</strDescription><strShortDesc>USE ME AND DIE</strShortDesc><intLootTableID>1</intLootTableID><intBaseHP>1</intBaseHP><CBIDWeaponDrop>-1</CBIDWeaponDrop><CBIDWeaponMelee>-1</CBIDWeaponMelee></row>
                <row IDVehicleTemplate="7"><IDVehicleTemplate>7</IDVehicleTemplate><CBIDVehicle>3100</CBIDVehicle><CBIDDriver>3101</CBIDDriver><sinBaseLevel>5</sinBaseLevel><tinLootChance>25</tinLootChance><tinLootRolls>2</tinLootRolls><IDSkill1>12</IDSkill1><tinSkillRank1>3</tinSkillRank1><strDescription>Bandit raider</strDescription><strShortDesc>Raider</strShortDesc><intLootTableID>4</intLootTableID><intBaseHP>900</intBaseHP></row>
              </tVehicleTemplate>
            </wad>
            """);

        var templates = WadXmlWorldDataLoader.LoadVehicleTemplates(_tempXmlPath);

        Assert.AreEqual(2, templates.Count);

        var first = templates[1];
        Assert.AreEqual(1, first.Id);
        Assert.AreEqual(2069, first.VehicleCbid);
        Assert.AreEqual(2071, first.DriverCbid);
        Assert.AreEqual(2195, first.WeaponTurretCbid);
        Assert.AreEqual(-1, first.WeaponFrontCbid);
        Assert.AreEqual(-1, first.ArmorCbid);
        Assert.AreEqual(-1, first.WeaponDropCbid);
        Assert.AreEqual(-1, first.WeaponMeleeCbid);
        Assert.AreEqual((short)2, first.BaseLevel);
        Assert.AreEqual(1, first.BaseHp);
        Assert.AreEqual((byte)10, first.LootChance);
        Assert.AreEqual((byte)1, first.LootRolls);
        Assert.AreEqual(1, first.LootTableId);
        Assert.AreEqual(-1, first.Skill1);
        Assert.AreEqual((byte)0, first.SkillRank1);
        Assert.AreEqual("USE ME AND DIE", first.Description);
        Assert.AreEqual("USE ME AND DIE", first.ShortDesc);

        // Missing CBID columns default to -1; missing skill defaults handled by row values.
        var second = templates[7];
        Assert.AreEqual(3100, second.VehicleCbid);
        Assert.AreEqual(3101, second.DriverCbid);
        Assert.AreEqual(-1, second.WeaponTurretCbid);
        Assert.AreEqual(-1, second.WeaponFrontCbid);
        Assert.AreEqual(-1, second.ArmorCbid);
        Assert.AreEqual(-1, second.WeaponDropCbid);
        Assert.AreEqual(-1, second.WeaponMeleeCbid);
        Assert.AreEqual((short)5, second.BaseLevel);
        Assert.AreEqual(900, second.BaseHp);
        Assert.AreEqual(12, second.Skill1);
        Assert.AreEqual((byte)3, second.SkillRank1);
    }

    [TestMethod]
    public void LoadCreatureAiProfiles_ParsesValsAndCode()
    {
        File.WriteAllText(_tempXmlPath, """
            <wad>
              <tCreatureAI>
                <row AIID="1"><AIID>1</AIID><AICode>2</AICode><strDescInternal>Normal creature AI</strDescInternal><val1>8000</val1><val2>0.30000001</val2><val3>0.2</val3><val4>0.89999998</val4><val5>1</val5><val6>0.5</val6><val7>50</val7><val8></val8><val9></val9><val10></val10><val11></val11><val12></val12><val13></val13><val14></val14><val15></val15><val16></val16><val17></val17><val18></val18><val19></val19><val20></val20></row>
                <row AIID="4"><AIID>4</AIID><AICode>5</AICode><strDescInternal>DR: AI</strDescInternal><val1>8000</val1><val2>0.60000002</val2><val3>0.2</val3><val4>1</val4><val5>1</val5><val6>0.5</val6><val7>50</val7><val8></val8><val9></val9><val10></val10><val11></val11><val12></val12><val13></val13><val14></val14><val15></val15><val16></val16><val17></val17><val18></val18><val19></val19><val20></val20></row>
              </tCreatureAI>
            </wad>
            """);

        var profiles = WadXmlWorldDataLoader.LoadCreatureAiProfiles(_tempXmlPath);

        Assert.AreEqual(2, profiles.Count);

        var normal = profiles[1];
        Assert.AreEqual(1, normal.AiId);
        Assert.AreEqual(HBAICode.Creature, normal.AiCode);
        Assert.AreEqual("Normal creature AI", normal.DescInternal);
        Assert.AreEqual(20, normal.Vals.Length);
        Assert.AreEqual(8000f, normal.ValFleeOrEngageTimerMs);
        Assert.AreEqual(0.30000001f, normal.ValFleeHpSecondary);
        Assert.AreEqual(0.2f, normal.ValFleeHpOrChance);
        Assert.AreEqual(0.89999998f, normal.ValReengageThreshold);
        Assert.AreEqual(1f, normal.ValHelpEnabled);
        Assert.AreEqual(0.5f, normal.ValHelpChance);
        Assert.AreEqual(50f, normal.ValHelpRange);

        // Empty <valN> elements parse as 0.
        for (var i = 7; i < 20; i++)
            Assert.AreEqual(0f, normal.Vals[i], $"val{i + 1} should default to 0");

        var driver = profiles[4];
        Assert.AreEqual(HBAICode.Driver, driver.AiCode);
        Assert.AreEqual(0.60000002f, driver.ValFleeHpSecondary);
        Assert.AreEqual(1f, driver.ValReengageThreshold);
    }

    [TestMethod]
    public void LoadVehicleTemplates_RealWad_CountAndSpotCheck()
    {
        if (!File.Exists(RealWadXmlPath))
        {
            Assert.Inconclusive($"Game data not installed at {RealWadXmlPath}");
            return;
        }

        var templates = WadXmlWorldDataLoader.LoadVehicleTemplates(RealWadXmlPath);

        Assert.IsTrue(templates.Count >= 400, $"Expected at least 400 vehicle templates, got {templates.Count}");

        var first = templates[1];
        Assert.AreEqual(2069, first.VehicleCbid);
        Assert.AreEqual(2071, first.DriverCbid);
        Assert.AreEqual(2195, first.WeaponTurretCbid);
        Assert.AreEqual(-1, first.WeaponFrontCbid);
        Assert.AreEqual((short)2, first.BaseLevel);
        Assert.AreEqual(1, first.BaseHp);
        Assert.AreEqual(1, first.LootTableId);
        Assert.AreEqual("USE ME AND DIE", first.Description);
    }

    [TestMethod]
    public void LoadCreatureAiProfiles_RealWad_Aiid1IsNormalCreature()
    {
        if (!File.Exists(RealWadXmlPath))
        {
            Assert.Inconclusive($"Game data not installed at {RealWadXmlPath}");
            return;
        }

        var profiles = WadXmlWorldDataLoader.LoadCreatureAiProfiles(RealWadXmlPath);

        Assert.IsTrue(profiles.Count >= 20, $"Expected at least 20 AI profiles, got {profiles.Count}");

        var normal = profiles[1];
        Assert.AreEqual(HBAICode.Creature, normal.AiCode);
        Assert.AreEqual("Normal creature AI", normal.DescInternal);
        Assert.AreEqual(8000f, normal.ValFleeOrEngageTimerMs);
        Assert.AreEqual(50f, normal.ValHelpRange);

        var driver = profiles[4];
        Assert.AreEqual(HBAICode.Driver, driver.AiCode);
    }

    [TestMethod]
    public void AssetManager_GetCreatureAiProfile_UsesTestSeam()
    {
        Assert.IsNull(AssetManager.Instance.GetCreatureAiProfile(999));
        Assert.IsNull(AssetManager.Instance.GetVehicleTemplate(999));

        var profile = new CreatureAiProfile
        {
            AiId = 999,
            AiCode = HBAICode.Driver,
            DescInternal = "test profile",
        };
        profile.Vals[0] = 1234f;

        var template = new VehicleTemplate { Id = 999, VehicleCbid = 42 };

        AssetManager.Instance.SetTestCreatureAiProfiles(new[] { profile });
        AssetManager.Instance.SetTestVehicleTemplates(new[] { template });

        var fetchedProfile = AssetManager.Instance.GetCreatureAiProfile(999);
        Assert.IsNotNull(fetchedProfile);
        Assert.AreEqual(HBAICode.Driver, fetchedProfile.AiCode);
        Assert.AreEqual(1234f, fetchedProfile.ValFleeOrEngageTimerMs);

        var fetchedTemplate = AssetManager.Instance.GetVehicleTemplate(999);
        Assert.IsNotNull(fetchedTemplate);
        Assert.AreEqual(42, fetchedTemplate.VehicleCbid);

        AssetManager.Instance.ClearTestNpcData();
        Assert.IsNull(AssetManager.Instance.GetCreatureAiProfile(999));
        Assert.IsNull(AssetManager.Instance.GetVehicleTemplate(999));
    }
}
