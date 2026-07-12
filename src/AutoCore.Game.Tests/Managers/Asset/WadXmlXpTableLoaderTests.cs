using AutoCore.Game.Managers.Asset;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>Regression tests for XP table loaders used by ExperienceService (docs/XP.md).</summary>
[TestClass]
public class WadXmlXpTableLoaderTests
{
    private string _tempXmlPath = null!;

    [TestInitialize]
    public void Init()
    {
        _tempXmlPath = Path.Combine(Path.GetTempPath(), $"xp-tables-{Guid.NewGuid():N}.xml");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempXmlPath))
            File.Delete(_tempXmlPath);
    }

    [TestMethod]
    public void LoadExperienceLevels_ParsesRetailStyleRows()
    {
        File.WriteAllText(_tempXmlPath, """
            <wad>
              <tExperienceLevel>
                <row>
                  <IDLevel>1</IDLevel>
                  <intExperience>1000</intExperience>
                  <iSkillPoints>1</iSkillPoints>
                  <iAttributePoints>2</iAttributePoints>
                  <iResearchPoints>0</iResearchPoints>
                </row>
                <row>
                  <IDLevel>5</IDLevel>
                  <intExperience>12000</intExperience>
                  <iSkillPoints>1</iSkillPoints>
                  <iAttributePoints>2</iAttributePoints>
                  <iResearchPoints>3</iResearchPoints>
                </row>
                <row>
                  <IDLevel>0</IDLevel>
                  <intExperience>0</intExperience>
                </row>
              </tExperienceLevel>
            </wad>
            """);

        var levels = WadXmlWorldDataLoader.LoadExperienceLevels(_tempXmlPath);
        Assert.AreEqual(2, levels.Count);
        Assert.AreEqual(1000u, levels[1].Experience);
        Assert.AreEqual(1, levels[1].SkillPoints);
        Assert.AreEqual(2, levels[1].AttributePoints);
        Assert.AreEqual(0, levels[1].ResearchPoints);
        Assert.AreEqual(12000u, levels[5].Experience);
        Assert.AreEqual(3, levels[5].ResearchPoints);
    }

    [TestMethod]
    public void LoadExperienceLevels_MissingSection_ReturnsEmpty()
    {
        File.WriteAllText(_tempXmlPath, "<wad><other/></wad>");
        var levels = WadXmlWorldDataLoader.LoadExperienceLevels(_tempXmlPath);
        Assert.AreEqual(0, levels.Count);
    }

    [TestMethod]
    public void LoadCreatureExperienceLevels_ParsesRows()
    {
        File.WriteAllText(_tempXmlPath, """
            <wad>
              <tCreatureExperienceLevel>
                <row><IDCreatureLevel>1</IDCreatureLevel><intExperience>39</intExperience></row>
                <row><IDCreatureLevel>20</IDCreatureLevel><intExperience>112</intExperience></row>
                <row><IDCreatureLevel>-1</IDCreatureLevel><intExperience>0</intExperience></row>
              </tCreatureExperienceLevel>
            </wad>
            """);

        var table = WadXmlWorldDataLoader.LoadCreatureExperienceLevels(_tempXmlPath);
        Assert.AreEqual(2, table.Count);
        Assert.AreEqual(39, table[1]);
        Assert.AreEqual(112, table[20]);
    }

    [TestMethod]
    public void LoadCreatureExperienceLevels_MissingSection_ReturnsEmpty()
    {
        File.WriteAllText(_tempXmlPath, "<wad/>");
        Assert.AreEqual(0, WadXmlWorldDataLoader.LoadCreatureExperienceLevels(_tempXmlPath).Count);
    }

    [TestMethod]
    public void LoadQuestXpLookup_ParsesFractions()
    {
        File.WriteAllText(_tempXmlPath, """
            <wad>
              <tQuestXPLookup>
                <row><IDQuestXPIndex>5</IDQuestXPIndex><rlLevelXP>0.10</rlLevelXP></row>
                <row><IDQuestXPIndex>9</IDQuestXPIndex><rlLevelXP>0.30</rlLevelXP></row>
                <row><IDQuestXPIndex>-1</IDQuestXPIndex><rlLevelXP>1.0</rlLevelXP></row>
              </tQuestXPLookup>
            </wad>
            """);

        var table = WadXmlWorldDataLoader.LoadQuestXpLookup(_tempXmlPath);
        Assert.AreEqual(2, table.Count);
        Assert.AreEqual(0.10f, table[5], 0.0001f);
        Assert.AreEqual(0.30f, table[9], 0.0001f);
    }

    [TestMethod]
    public void LoadQuestXpLookup_MissingSection_ReturnsEmpty()
    {
        File.WriteAllText(_tempXmlPath, "<wad/>");
        Assert.AreEqual(0, WadXmlWorldDataLoader.LoadQuestXpLookup(_tempXmlPath).Count);
    }

    [TestMethod]
    public void LoadQuestCreditsLookup_ParsesFractions()
    {
        File.WriteAllText(_tempXmlPath, """
            <wad>
              <tQuestCreditsLookup>
                <row><IDQuestCreditsIndex>4</IDQuestCreditsIndex><rlLevelCredits>0.8</rlLevelCredits></row>
                <row><IDQuestCreditsIndex>5</IDQuestCreditsIndex><rlLevelCredits>1.0</rlLevelCredits></row>
                <row><IDQuestCreditsIndex>-1</IDQuestCreditsIndex><rlLevelCredits>9</rlLevelCredits></row>
              </tQuestCreditsLookup>
            </wad>
            """);

        var table = WadXmlWorldDataLoader.LoadQuestCreditsLookup(_tempXmlPath);
        Assert.AreEqual(2, table.Count);
        Assert.AreEqual(0.8f, table[4], 0.0001f);
        Assert.AreEqual(1.0f, table[5], 0.0001f);
    }

    [TestMethod]
    public void LoadQuestBaseCredits_ParsesLevels()
    {
        File.WriteAllText(_tempXmlPath, """
            <wad>
              <tQuestBaseCredits>
                <row><IDTargetLevel>1</IDTargetLevel><intBaseCredits>3</intBaseCredits></row>
                <row><IDTargetLevel>2</IDTargetLevel><intBaseCredits>10</intBaseCredits></row>
              </tQuestBaseCredits>
            </wad>
            """);

        var table = WadXmlWorldDataLoader.LoadQuestBaseCredits(_tempXmlPath);
        Assert.AreEqual(2, table.Count);
        Assert.AreEqual(3, table[1]);
        Assert.AreEqual(10, table[2]);
    }

    [TestMethod]
    public void LoadQuestCreditsTables_MissingSection_ReturnsEmpty()
    {
        File.WriteAllText(_tempXmlPath, "<wad/>");
        Assert.AreEqual(0, WadXmlWorldDataLoader.LoadQuestCreditsLookup(_tempXmlPath).Count);
        Assert.AreEqual(0, WadXmlWorldDataLoader.LoadQuestBaseCredits(_tempXmlPath).Count);
    }
}
