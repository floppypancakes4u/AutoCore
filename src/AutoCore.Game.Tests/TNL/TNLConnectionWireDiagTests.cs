using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;

[TestClass]
public class TNLConnectionWireDiagTests
{
    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        WireDiag.ResetForTests();
    }

    [TestMethod]
    public void SendGamePacket_WithTestSinkAndWireDiag_RecordsGamePacket()
    {
        WireDiag.Enabled = true;
        BasePacket seen = null;
        TNLConnection.TestPacketSink = (_, p) => seen = p;

        var connection = new TNLConnection();
        var packet = new GroupReactionCallPacket();
        connection.SendGamePacket(packet, skipOpcode: true);

        Assert.IsNotNull(seen);
        Assert.AreSame(packet, seen);
        var entry = WireDiag.Snapshot().Single();
        Assert.AreEqual(WireDiagKind.GamePacket, entry.Kind);
        StringAssert.Contains(entry.Name, "GroupReactionCall");
    }

    [TestMethod]
    public void SendGamePacket_WireDiagDisabled_DoesNotRecord()
    {
        WireDiag.Enabled = false;
        TNLConnection.TestPacketSink = (_, _) => { };

        var connection = new TNLConnection();
        connection.SendGamePacket(new GroupReactionCallPacket(), skipOpcode: true);

        Assert.AreEqual(0, WireDiag.Snapshot().Count);
    }
}
