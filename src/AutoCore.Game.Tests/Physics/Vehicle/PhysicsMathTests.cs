using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Pure float/xyz helpers used by vehicle physics (no game-world coupling).
/// </summary>
[TestClass]
public class PhysicsMathTests
{
    private const float Epsilon = 1e-5f;

    [TestMethod]
    public void Dot_OrthogonalUnitAxes_IsZero()
    {
        var a = new Vector3(1f, 0f, 0f);
        var b = new Vector3(0f, 1f, 0f);
        Assert.AreEqual(0f, PhysicsMath.Dot(a, b), Epsilon);
    }

    [TestMethod]
    public void Dot_ParallelVectors_IsProductOfLengths()
    {
        var a = new Vector3(2f, 0f, 0f);
        var b = new Vector3(3f, 0f, 0f);
        Assert.AreEqual(6f, PhysicsMath.Dot(a, b), Epsilon);
    }

    [TestMethod]
    public void Dot_General_MatchesManual()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(4f, -5f, 6f);
        Assert.AreEqual(1f * 4f + 2f * -5f + 3f * 6f, PhysicsMath.Dot(a, b), Epsilon);
    }

    [TestMethod]
    public void Cross_ICrossJ_EqualsK()
    {
        var i = new Vector3(1f, 0f, 0f);
        var j = new Vector3(0f, 1f, 0f);
        var k = PhysicsMath.Cross(i, j);
        Assert.AreEqual(0f, k.X, Epsilon);
        Assert.AreEqual(0f, k.Y, Epsilon);
        Assert.AreEqual(1f, k.Z, Epsilon);
    }

    [TestMethod]
    public void Cross_JCrossI_EqualsNegK()
    {
        var i = new Vector3(1f, 0f, 0f);
        var j = new Vector3(0f, 1f, 0f);
        var r = PhysicsMath.Cross(j, i);
        Assert.AreEqual(0f, r.X, Epsilon);
        Assert.AreEqual(0f, r.Y, Epsilon);
        Assert.AreEqual(-1f, r.Z, Epsilon);
    }

    [TestMethod]
    public void Cross_Parallel_IsZero()
    {
        var a = new Vector3(2f, 4f, 6f);
        var b = new Vector3(1f, 2f, 3f);
        var r = PhysicsMath.Cross(a, b);
        Assert.AreEqual(0f, r.X, Epsilon);
        Assert.AreEqual(0f, r.Y, Epsilon);
        Assert.AreEqual(0f, r.Z, Epsilon);
    }

    [TestMethod]
    public void Length_Unit_IsOne()
    {
        Assert.AreEqual(1f, PhysicsMath.Length(new Vector3(0f, 1f, 0f)), Epsilon);
    }

    [TestMethod]
    public void Length_ThreeFourFive_IsFive()
    {
        // 3-4-5 right triangle in XZ plane
        Assert.AreEqual(5f, PhysicsMath.Length(new Vector3(3f, 0f, 4f)), Epsilon);
    }

    [TestMethod]
    public void Length_Zero_IsZero()
    {
        Assert.AreEqual(0f, PhysicsMath.Length(Vector3.Zero), Epsilon);
    }

    [TestMethod]
    public void Normalize_NonZero_HasUnitLength()
    {
        var n = PhysicsMath.Normalize(new Vector3(3f, 0f, 4f));
        Assert.AreEqual(1f, PhysicsMath.Length(n), Epsilon);
        Assert.AreEqual(0.6f, n.X, Epsilon);
        Assert.AreEqual(0f, n.Y, Epsilon);
        Assert.AreEqual(0.8f, n.Z, Epsilon);
    }

    [TestMethod]
    public void Normalize_Zero_ReturnsZero()
    {
        var n = PhysicsMath.Normalize(Vector3.Zero);
        Assert.AreEqual(0f, n.X, Epsilon);
        Assert.AreEqual(0f, n.Y, Epsilon);
        Assert.AreEqual(0f, n.Z, Epsilon);
    }

    [TestMethod]
    public void Clamp_BelowMin_ReturnsMin()
    {
        Assert.AreEqual(-1f, PhysicsMath.Clamp(-2f, -1f, 1f), Epsilon);
    }

    [TestMethod]
    public void Clamp_AboveMax_ReturnsMax()
    {
        Assert.AreEqual(1f, PhysicsMath.Clamp(2f, -1f, 1f), Epsilon);
    }

    [TestMethod]
    public void Clamp_InRange_ReturnsValue()
    {
        Assert.AreEqual(0.25f, PhysicsMath.Clamp(0.25f, -1f, 1f), Epsilon);
    }

    [TestMethod]
    public void Clamp_AtBounds_ReturnsBounds()
    {
        Assert.AreEqual(-1f, PhysicsMath.Clamp(-1f, -1f, 1f), Epsilon);
        Assert.AreEqual(1f, PhysicsMath.Clamp(1f, -1f, 1f), Epsilon);
    }

    [TestMethod]
    public void Dot_ComponentOverload_MatchesVector()
    {
        float d = PhysicsMath.Dot(1f, 2f, 3f, 4f, -5f, 6f);
        Assert.AreEqual(PhysicsMath.Dot(new Vector3(1f, 2f, 3f), new Vector3(4f, -5f, 6f)), d, Epsilon);
    }

    [TestMethod]
    public void Cross_ComponentOverload_MatchesVector()
    {
        PhysicsMath.Cross(1f, 0f, 0f, 0f, 1f, 0f, out float x, out float y, out float z);
        Assert.AreEqual(0f, x, Epsilon);
        Assert.AreEqual(0f, y, Epsilon);
        Assert.AreEqual(1f, z, Epsilon);
    }

    [TestMethod]
    public void Length_ComponentOverload_MatchesVector()
    {
        Assert.AreEqual(5f, PhysicsMath.Length(3f, 0f, 4f), Epsilon);
    }

    [TestMethod]
    public void Normalize_ComponentOverload_MatchesVector()
    {
        PhysicsMath.Normalize(3f, 0f, 4f, out float x, out float y, out float z);
        Assert.AreEqual(0.6f, x, Epsilon);
        Assert.AreEqual(0f, y, Epsilon);
        Assert.AreEqual(0.8f, z, Epsilon);
    }
}
