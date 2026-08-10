using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Game.Chat;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// /mission &lt;id&gt; — GM diagnostic: print mission display name and accept text.
/// </summary>
[TestClass]
public class MissionInfoCommandTests
{
    private const int MissionId = 99101;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    public void Mission_KnownId_PrintsTitleAndAcceptText()
    {
        var mission = Mission.CreateForTests(MissionId);
        mission.Title = "Scavenger Hunt";
        mission.OnLineAccept = "Bring me five scrap plates.";
        mission.Description = "Fallback description should not replace accept text.";
        AssetManager.Instance.SetTestMission(mission);

        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, $"/mission {MissionId}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, MissionId.ToString());
        StringAssert.Contains(result.Message, "Scavenger Hunt");
        StringAssert.Contains(result.Message, "Bring me five scrap plates.");
        Assert.IsFalse(
            result.Message.Contains("Fallback description", StringComparison.Ordinal),
            "When OnLineAccept is set, Description must not replace it.");
    }

    [TestMethod]
    public void Mission_MissingAcceptText_FallsBackToDescription()
    {
        var mission = Mission.CreateForTests(MissionId);
        mission.Title = "Empty Accept";
        mission.OnLineAccept = null;
        mission.Description = "Talk to the quartermaster.";
        AssetManager.Instance.SetTestMission(mission);

        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, $"/mission {MissionId}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Empty Accept");
        StringAssert.Contains(result.Message, "Talk to the quartermaster.");
    }

    [TestMethod]
    public void Mission_MissingTitle_FallsBackToInternalName()
    {
        var mission = Mission.CreateForTests(MissionId);
        mission.Title = null;
        mission.Name = "mission_scavenger_01";
        mission.OnLineAccept = "Go.";
        AssetManager.Instance.SetTestMission(mission);

        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, $"/mission {MissionId}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "mission_scavenger_01");
        StringAssert.Contains(result.Message, "Go.");
    }

    [TestMethod]
    public void Mission_UnknownId_ReportsUnknown()
    {
        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, "/mission 99999999");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Unknown");
    }

    [TestMethod]
    public void Mission_Usage_WhenMissingId()
    {
        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, "/mission");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Usage");
    }

    [TestMethod]
    public void Mission_NullCharacter_Safe()
    {
        var result = ChatCommandService.Instance.Execute(null, "/mission 1");
        Assert.IsTrue(result.Handled);
    }

    [TestMethod]
    public void Mission_Alias_CaseInsensitive()
    {
        var mission = Mission.CreateForTests(MissionId);
        mission.Title = "Alias Check";
        mission.OnLineAccept = "ok";
        AssetManager.Instance.SetTestMission(mission);

        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, $"/MISSION {MissionId}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Alias Check");
    }
}
