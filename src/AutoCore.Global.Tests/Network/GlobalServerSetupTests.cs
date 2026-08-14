using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Global.Tests.Network;

using AutoCore.Game.TNL;
using AutoCore.Global.Network;
using static GlobalServerTestHelpers;

[TestClass]
public class GlobalServerSetupTests
{
    private GlobalServer? _server;
    private IPAddress? _savedSectorRedirect;

    [TestInitialize]
    public void SaveSectorRedirect()
    {
        _savedSectorRedirect = TNLConnection.SectorRedirectAddress;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_server != null)
            SafeShutdown(_server);
        _server = null;

        TNLConnection.SectorRedirectAddress = _savedSectorRedirect ?? IPAddress.Loopback;
    }

    [TestMethod]
    public void Constructor_SetsTypeCreatesLoop_AndRegistersExitCommand()
    {
        _server = CreateServer();

        Assert.AreEqual("Global", _server.Type);
        Assert.IsNotNull(_server.Loop);
        Assert.IsFalse(_server.IsRunning);
        Assert.AreEqual(GlobalServer.MainLoopTime, 100);

        var commands = GetRegisteredCommands();
        Assert.IsTrue(commands.ContainsKey("exit"), "RegisterCommands must register scoped 'global.exit' as 'exit'.");
    }

    [TestMethod]
    public void Setup_AppliesConfig_PublicAddress_AndTnlFlags()
    {
        _server = CreateServer();
        var config = CreateSetupConfig(
            gamePort: 0,
            publicAddress: "10.1.2.3",
            allowVersionMismatch: true,
            expectedVersion: TNLInterface.Version);

        _server.Setup(config);

        Assert.AreSame(config, _server.Config);
        AssertPublicAddress(_server, "10.1.2.3");
        Assert.IsNotNull(_server.Interface);
        Assert.IsTrue(_server.Interface.AllowVersionMismatch);
        Assert.AreEqual(TNLInterface.Version, _server.Interface.ExpectedVersion);
    }

    [TestMethod]
    public void Setup_StockClientVersion_Expects175WithoutAllowingMismatch()
    {
        _server = CreateServer();
        var config = CreateSetupConfig(
            gamePort: 0,
            allowVersionMismatch: false,
            expectedVersion: 175);

        _server.Setup(config);

        Assert.AreEqual(175, TNLInterface.Version);
        Assert.AreEqual(175, _server.Interface.ExpectedVersion);
        Assert.AreEqual(TNLInterface.Version, _server.Interface.ExpectedVersion);
        Assert.IsFalse(_server.Interface.AllowVersionMismatch);
    }

    [TestMethod]
    public void Setup_PropagatesPublicAddress_ToSectorRedirect()
    {
        _server = CreateServer();
        var config = CreateSetupConfig(gamePort: 0, publicAddress: "192.168.50.62");

        _server.Setup(config);

        Assert.AreEqual(IPAddress.Parse("192.168.50.62"), TNLConnection.SectorRedirectAddress);
    }

    [TestMethod]
    public void Setup_WhenExpectedVersionIsZero_UsesTnlDefaultVersion()
    {
        _server = CreateServer();
        var config = CreateSetupConfig(expectedVersion: 0);

        _server.Setup(config);

        Assert.AreEqual(TNLInterface.Version, _server.Interface.ExpectedVersion);
    }

    [TestMethod]
    public void Setup_WhenConfigIsNull_KeepsExistingConfig()
    {
        _server = CreateServer();
        var original = _server.Config;
        original.GameConfig.PublicAddress = "192.168.0.9";
        original.GameConfig.Port = 0;

        _server.Setup(null!);

        Assert.AreSame(original, _server.Config);
        AssertPublicAddress(_server, "192.168.0.9");
        Assert.IsNotNull(_server.Interface);
    }

    [TestMethod]
    public void MainLoop_WhenInterfaceIsNull_ReturnsWithoutThrowing()
    {
        _server = CreateServer();

        _server.MainLoop(50);
    }

    [TestMethod]
    public void MainLoop_WhenInterfaceSet_PulsesWithoutThrowing()
    {
        _server = CreateServer();
        _server.Setup(CreateSetupConfig(gamePort: 0));

        _server.MainLoop(25);
        _server.MainLoop(25);
    }

    [TestMethod]
    public void Start_WhenGamePortIsZero_ReturnsFalseWithoutStartingLoop()
    {
        _server = CreateServer();
        _server.Setup(CreateSetupConfig(gamePort: 0, communicatorPort: 2107));

        var started = _server.Start();

        Assert.IsFalse(started);
        Assert.IsFalse(_server.IsRunning);
        Assert.IsFalse(_server.Loop.Running);
    }

    [TestMethod]
    public void Start_WhenCommunicatorPortIsZero_ReturnsFalse()
    {
        _server = CreateServer();
        // Use non-zero game port for this branch, but avoid binding production 26880.
        // Setup always constructs TNLInterface; pick an ephemeral OS port via 0 would
        // short-circuit earlier. Force the communicator branch by setting Port after Setup.
        var config = CreateSetupConfig(gamePort: 0, communicatorPort: 0);
        _server.Setup(config);
        _server.Config.GameConfig.Port = 39999; // validation only; interface already created on 0

        var started = _server.Start();

        Assert.IsFalse(started);
        Assert.IsFalse(_server.Loop.Running);
    }

    [TestMethod]
    public void Start_WhenCommunicatorAddressIsNull_ReturnsFalse()
    {
        _server = CreateServer();
        var config = CreateSetupConfig(gamePort: 0);
        config.CommunicatorAddress = null!;
        _server.Setup(config);
        _server.Config.GameConfig.Port = 39998;

        var started = _server.Start();

        Assert.IsFalse(started);
        Assert.IsFalse(_server.Loop.Running);
    }

    [TestMethod]
    public void Shutdown_AfterSetupWithoutStart_DoesNotThrow()
    {
        _server = CreateServer();
        _server.Setup(CreateSetupConfig());

        _server.Shutdown();

        Assert.IsNull(_server.Interface);
        Assert.IsFalse(_server.IsRunning);
        GC.SuppressFinalize(_server);
        _server = null;
    }

    [TestMethod]
    public void Constants_AreStable()
    {
        Assert.AreEqual(100, GlobalServer.MainLoopTime);
        Assert.AreEqual(512, GlobalServer.SendBufferSize);
    }

    [TestMethod]
    public void Start_WithValidPorts_StartsLoopThenShutdownStops()
    {
        // Setup uses UDP port 0 (ephemeral; not production 26880). Start only checks
        // Config.GameConfig.Port != 0 — it does not re-bind — so we advertise a non-zero
        // port after Setup. Communicator targets a free local TCP port that is not
        // listening so ConnectAsync fails asynchronously (no live Auth / MySQL).
        var tcpPort = GetFreeTcpPort();

        _server = CreateServer();
        _server.Setup(CreateSetupConfig(
            gamePort: 0,
            communicatorAddress: "127.0.0.1",
            communicatorPort: tcpPort));
        _server.Config.GameConfig.Port = 39990;

        var started = _server.Start();

        Assert.IsTrue(started);
        Assert.IsTrue(_server.Loop.Running);
        Assert.IsTrue(_server.IsRunning);

        _server.Shutdown();

        Assert.IsFalse(_server.Loop.Running);
        Assert.IsNull(_server.Interface);
        GC.SuppressFinalize(_server);
        _server = null;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
