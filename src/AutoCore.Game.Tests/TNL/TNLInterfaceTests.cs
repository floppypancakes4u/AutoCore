using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

[TestClass]
public class TNLInterfaceTests
{
    [TestMethod]
    public void Version_IsRetail175()
    {
        Assert.AreEqual(175, TNLInterface.Version);
    }

    [TestMethod]
    public void ConstructWithoutBind_DefaultsVersionFieldsAndGhosting()
    {
        // skipNetworkBind=true swaps the ephemeral base(0) socket for an unbound TNLSocket (stock TNL).
        var iface = new TNLInterface(doGhosting: true, skipNetworkBind: true);

        Assert.IsTrue(iface.DoGhosting);
        Assert.AreEqual(TNLInterface.Version, iface.ExpectedVersion);
        Assert.IsFalse(iface.AllowVersionMismatch);
        Assert.AreEqual(0L, iface.ConnectionId);
        Assert.AreEqual(0, iface.MapConnections.Count);
        Assert.IsNotNull(iface.Socket);
    }

    [TestMethod]
    public void ConstructWithoutBind_DoGhostingFalse_AndAllowVersionMismatchMutable()
    {
        var iface = new TNLInterface(doGhosting: false, skipNetworkBind: true);

        Assert.IsFalse(iface.DoGhosting);

        iface.AllowVersionMismatch = true;
        iface.ExpectedVersion = 999;

        Assert.IsTrue(iface.AllowVersionMismatch);
        Assert.AreEqual(999, iface.ExpectedVersion);
    }

    [TestMethod]
    public void FindConnection_Missing_ReturnsNull()
    {
        var iface = new TNLInterface(doGhosting: false, skipNetworkBind: true);
        Assert.IsNull(iface.FindConnection(12345));
    }

    [TestMethod]
    public void AddConnection_AssignsPlayerCoidAndMapsConnection()
    {
        var iface = new TNLInterface(doGhosting: true, skipNetworkBind: true);
        var conn = new TNLConnection();
        conn.SetNetAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));

        iface.AddConnection(conn);

        Assert.AreEqual(1, iface.MapConnections.Count);
        Assert.AreEqual(1L, iface.ConnectionId);
        Assert.AreSame(conn, iface.FindConnection(conn.GetPlayerCOID()));
    }
}
