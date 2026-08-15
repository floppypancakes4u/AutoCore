namespace AutoCore.Game.Tests.Skills;

using AutoCore.Game.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The hotbar cooldown sweep is client-local, so a playtest log that only records the server window
/// cannot explain what the player saw. These pin the prediction we log alongside it.
///
/// CVOGHBOKToCastAgain (0x51E240) lives for ceil(lCoolDown * modifier) + iCastTime ms and clamps
/// that to 500 ms whenever the caster's CVOGCharacter::m_lGMLevel (+0x6B4) is >= 1. That is why a
/// GM playtest shows a sub-second sweep against a 12 s server cooldown; verified against account
/// 'floppy' (Level 255) casting Psioptic Burst 915 (authored cooldown 12000 ms, no cast-time
/// element) and getting a server reject 2.66 s later.
/// </summary>
[TestClass]
public class SkillCooldownDiagnosticsTests
{
    [TestMethod]
    public void NonGm_SeesTheFullServerWindow()
    {
        Assert.AreEqual(12000L, SkillCooldownDiagnostics.PredictClientSweepMs(12000, gmLevel: 0));
    }

    [TestMethod]
    public void Gm_IsClampedToTheRetailFiveHundredMillisecondCap()
    {
        Assert.AreEqual(500L, SkillCooldownDiagnostics.PredictClientSweepMs(12000, gmLevel: 1),
            "GM level 1 already trips the clamp: the client compares m_lGMLevel >= 1");
        Assert.AreEqual(500L, SkillCooldownDiagnostics.PredictClientSweepMs(14000, gmLevel: 255));
    }

    /// <summary>
    /// The clamp is a cap, not an assignment: the client only overwrites the duration when it is
    /// above 500 ms (CMP EAX,0x1F4 / JC keeps the smaller value).
    /// </summary>
    [TestMethod]
    public void Gm_KeepsACooldownAlreadyShorterThanTheCap()
    {
        Assert.AreEqual(300L, SkillCooldownDiagnostics.PredictClientSweepMs(300, gmLevel: 255));
    }

    [TestMethod]
    public void NoCooldown_PredictsNoSweep()
    {
        Assert.AreEqual(0L, SkillCooldownDiagnostics.PredictClientSweepMs(0, gmLevel: 0));
        Assert.AreEqual(0L, SkillCooldownDiagnostics.PredictClientSweepMs(-5, gmLevel: 255));
    }
}
