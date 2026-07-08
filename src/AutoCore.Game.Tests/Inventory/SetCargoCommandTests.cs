using AutoCore.Game.Chat;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class SetCargoCommandTests
{
    [TestMethod]
    public void SetCargo_WithoutCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/setcargo 5");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual("No character loaded.", result.Message);
    }

    [TestMethod]
    public void SetCargo_InvalidArgs_ReturnsUsage()
    {
        var result = ChatCommandService.Instance.Execute(null, "/setcargo");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Usage:");
    }
}
