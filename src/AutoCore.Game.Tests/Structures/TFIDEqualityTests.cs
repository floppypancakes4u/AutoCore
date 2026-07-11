using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Structures;

using AutoCore.Game.Structures;

[TestClass]
public class TFIDEqualityTests
{
    [TestMethod]
    public void NullEqualsNull_IsTrue()
    {
        TFID a = null;
        TFID b = null;
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
    }

    [TestMethod]
    public void NullDoesNotEqualInstance()
    {
        TFID a = null;
        var b = new TFID(1, true);
        Assert.IsFalse(a == b);
        Assert.IsTrue(a != b);
        Assert.IsFalse(b == a);
        Assert.IsTrue(b != a);
    }

    [TestMethod]
    public void SameCoidAndGlobal_AreEqual()
    {
        var a = new TFID(99, true);
        var b = new TFID(99, true);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
    }

    [TestMethod]
    public void DifferentCoid_AreNotEqual()
    {
        var a = new TFID(1, false);
        var b = new TFID(2, false);
        Assert.IsFalse(a == b);
        Assert.IsTrue(a != b);
    }
}
