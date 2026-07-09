using AutoCore.Game.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

[TestClass]
public class GetMoneyCbidFromCreditsTests
{
    [TestMethod]
    public void GetMoneyCBIDFromCredits_Thresholds()
    {
        Assert.AreEqual(-1, ClonedObjectBase.GetMoneyCBIDFromCredits(0));
        Assert.AreEqual(-1, ClonedObjectBase.GetMoneyCBIDFromCredits(-1));

        Assert.AreEqual(2826, ClonedObjectBase.GetMoneyCBIDFromCredits(1)); // Clink
        Assert.AreEqual(2826, ClonedObjectBase.GetMoneyCBIDFromCredits(999));

        Assert.AreEqual(2828, ClonedObjectBase.GetMoneyCBIDFromCredits(1000)); // Script
        Assert.AreEqual(2828, ClonedObjectBase.GetMoneyCBIDFromCredits(999_999));

        Assert.AreEqual(2825, ClonedObjectBase.GetMoneyCBIDFromCredits(1_000_000)); // Bars
        Assert.AreEqual(2825, ClonedObjectBase.GetMoneyCBIDFromCredits(999_999_999));

        Assert.AreEqual(2827, ClonedObjectBase.GetMoneyCBIDFromCredits(1_000_000_000)); // Orb
        Assert.AreEqual(2827, ClonedObjectBase.GetMoneyCBIDFromCredits(long.MaxValue));
    }
}
