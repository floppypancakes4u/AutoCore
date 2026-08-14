using AutoCore.Auth.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Auth.Tests.Data;

[TestClass]
public class EnumTests
{
    [TestMethod]
    public void ClientOpcode_HasExpectedValues()
    {
        Assert.AreEqual(0x00, (byte)ClientOpcode.Login);
        Assert.AreEqual(0x02, (byte)ClientOpcode.AboutToPlay);
        Assert.AreEqual(0x03, (byte)ClientOpcode.Logout);
        Assert.AreEqual(0x05, (byte)ClientOpcode.ServerListExt);
        Assert.AreEqual(0x06, (byte)ClientOpcode.SCCheck);
    }

    [TestMethod]
    public void ServerOpcode_HasExpectedValues()
    {
        Assert.AreEqual(0x00, (byte)ServerOpcode.ProtocolVersion);
        Assert.AreEqual(0x01, (byte)ServerOpcode.LoginFail);
        Assert.AreEqual(0x02, (byte)ServerOpcode.BlockedAccount);
        Assert.AreEqual(0x03, (byte)ServerOpcode.LoginOk);
        Assert.AreEqual(0x04, (byte)ServerOpcode.SendServerListExt);
        Assert.AreEqual(0x05, (byte)ServerOpcode.SendServerListFail);
        Assert.AreEqual(0x06, (byte)ServerOpcode.PlayFail);
        Assert.AreEqual(0x07, (byte)ServerOpcode.PlayOk);
        Assert.AreEqual(0x08, (byte)ServerOpcode.AccountKicked);
        Assert.AreEqual(0x09, (byte)ServerOpcode.BlockedAccountWithMessage);
        Assert.AreEqual(0x0A, (byte)ServerOpcode.SCCheckReq);
        Assert.AreEqual(0x0B, (byte)ServerOpcode.Unknown1);
        Assert.AreEqual(0x0C, (byte)ServerOpcode.HandOffToQueue);
        Assert.AreEqual(0x0E, (byte)ServerOpcode.HandoffToGame);
    }

    [TestMethod]
    public void FailReason_HasExpectedValues()
    {
        Assert.AreEqual(0, (byte)FailReason.UnexpectedError);
        Assert.AreEqual(2, (byte)FailReason.UserNameOrPassword);
        Assert.AreEqual(5, (byte)FailReason.SSNInformationUnavailable);
        Assert.AreEqual(6, (byte)FailReason.NoAvailableServers);
        Assert.AreEqual(7, (byte)FailReason.AlreadyLoggedIn);
        Assert.AreEqual(8, (byte)FailReason.ServerIsDown);
        Assert.AreEqual(11, (byte)FailReason.Kicked);
        Assert.AreEqual(12, (byte)FailReason.AgeRestricted);
        Assert.AreEqual(15, (byte)FailReason.ServerIsFull);
        Assert.AreEqual(17, (byte)FailReason.MustChangePassword);
        Assert.AreEqual(18, (byte)FailReason.OutOfTime);
    }

    [TestMethod]
    public void ClientState_HasExpectedValues()
    {
        Assert.AreEqual(0, (byte)ClientState.None);
        Assert.AreEqual(1, (byte)ClientState.Connected);
        Assert.AreEqual(2, (byte)ClientState.LoggedIn);
        Assert.AreEqual(3, (byte)ClientState.ServerList);
        Assert.AreEqual(4, (byte)ClientState.Queued);
        Assert.AreEqual(5, (byte)ClientState.Redirecting);
        Assert.AreEqual(6, (byte)ClientState.Disconnected);
    }
}
