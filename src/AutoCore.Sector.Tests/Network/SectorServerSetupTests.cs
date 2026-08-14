using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Sector.Tests.Network;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.TNL;
using AutoCore.Sector.Config;
using AutoCore.Sector.Network;

[TestClass]
public class SectorServerSetupTests
{
    [TestCleanup]
    public void Cleanup()
    {
        SectorLoopControl.GetLoopMilliseconds = null;
        SectorLoopControl.TrySetLoopMilliseconds = null;
    }

    [TestMethod]
    public void Constructor_RegistersMainLoopPeriodConstant()
    {
        Assert.AreEqual(50, SectorServer.MainLoopTime);

        using var host = new SectorServerHost();
        Assert.IsFalse(host.Server.IsRunning);
        Assert.IsNotNull(host.Server.Loop);
        Assert.AreEqual(SectorServer.MainLoopTime, host.Server.Loop.LoopTime);
    }

    [TestMethod]
    public void Setup_AppliesConfigAndCreatesInterfaceWithoutStart()
    {
        using var host = new SectorServerHost();
        var port = GetFreeUdpPort();
        var config = new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = port,
                PublicAddress = "127.0.0.1",
                AllowVersionMismatch = true,
                ExpectedVersion = TNLInterface.Version,
                EnableDevControl = false
            }
        };

        host.Server.Setup(config);

        Assert.AreSame(config, host.Server.Config);
        Assert.AreEqual(IPAddress.Parse("127.0.0.1"), host.Server.PublicAddress);
        Assert.IsNotNull(host.Server.Interface);
        Assert.IsTrue(host.Server.Interface.AllowVersionMismatch);
        Assert.AreEqual(TNLInterface.Version, host.Server.Interface.ExpectedVersion);
        Assert.IsFalse(host.Server.IsRunning, "Setup must not start the main loop.");
    }

    [TestMethod]
    public void Setup_ExpectedVersionZero_UsesTnlDefaultVersion()
    {
        using var host = new SectorServerHost();
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = GetFreeUdpPort(),
                PublicAddress = "127.0.0.1",
                ExpectedVersion = 0
            }
        });

        Assert.AreEqual(TNLInterface.Version, host.Server.Interface.ExpectedVersion);
        Assert.IsFalse(host.Server.Interface.AllowVersionMismatch);
    }

    [TestMethod]
    public void Setup_StockClientVersion_Expects175WithoutAllowingMismatch()
    {
        using var host = new SectorServerHost();
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = GetFreeUdpPort(),
                PublicAddress = "127.0.0.1",
                AllowVersionMismatch = false,
                ExpectedVersion = 175
            }
        });

        Assert.AreEqual(175, TNLInterface.Version);
        Assert.AreEqual(175, host.Server.Interface.ExpectedVersion);
        Assert.AreEqual(TNLInterface.Version, host.Server.Interface.ExpectedVersion);
        Assert.IsFalse(host.Server.Interface.AllowVersionMismatch);
    }

    [TestMethod]
    public void Setup_RegistersSectorLoopControl()
    {
        using var host = new SectorServerHost();
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = GetFreeUdpPort(),
                PublicAddress = "10.1.2.3"
            }
        });

        Assert.IsNotNull(SectorLoopControl.GetLoopMilliseconds);
        Assert.IsNotNull(SectorLoopControl.TrySetLoopMilliseconds);
        Assert.AreEqual(SectorServer.MainLoopTime, SectorLoopControl.CurrentMilliseconds);

        Assert.IsTrue(SectorLoopControl.TrySet(100, out var message));
        Assert.AreEqual(100, host.Server.Loop.LoopTime);
        StringAssert.Contains(message, "100ms");
    }

    [TestMethod]
    public void Start_WhenPortIsZero_ReturnsFalseWithoutStartingLoop()
    {
        using var host = new SectorServerHost();
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = 0,
                PublicAddress = "127.0.0.1"
            }
        });

        Assert.IsFalse(host.Server.Start());
        Assert.IsFalse(host.Server.IsRunning);
    }

    [TestMethod]
    public void Shutdown_AfterSetupOnly_DoesNotThrow()
    {
        using var host = new SectorServerHost(disposeViaShutdown: false);
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = GetFreeUdpPort(),
                PublicAddress = "127.0.0.1"
            }
        });

        host.Server.Shutdown();

        Assert.IsNull(host.Server.Interface);
        Assert.IsFalse(host.Server.IsRunning);
    }

    [TestMethod]
    public void MainLoop_WhenInterfaceNull_ReturnsWithoutThrowing()
    {
        using var host = new SectorServerHost();
        // No Setup → Interface remains null
        host.Server.MainLoop(50);
    }

    [TestMethod]
    public void MainLoop_AfterSetup_RunsEmptyConnectionTick()
    {
        using var host = new SectorServerHost();
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = GetFreeUdpPort(),
                PublicAddress = "127.0.0.1",
                EnableDevControl = false
            }
        });

        // Empty MapConnections: exercises Pulse, combat/pose wrappers, pool loop without clients.
        host.Server.MainLoop(50);
        host.Server.MainLoop(100);
        host.Server.MainLoop(1);
    }

    [TestMethod]
    public void MainLoop_WithMapConnections_ExercisesPerConnectionLoops()
    {
        using var host = new SectorServerHost();
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = GetFreeUdpPort(),
                PublicAddress = "127.0.0.1",
                EnableDevControl = false
            }
        });

        var character = new Character();
        character.SetCoid(4242, true);
        character.AttachTestDataForTests("TickPilot");

        var vehicle = new Vehicle();
        vehicle.SetCoid(4243, true);
        character.SetCurrentVehicleForTests(vehicle);

        var conn = new TNLConnection();
        conn.SetPlayerCOID(4242);
        conn.CurrentCharacter = character;

        host.Server.Interface.MapConnections[4242] = conn;
        host.Server.Interface.MapConnections[99] = null; // null connection entry

        var prevPathPose = LogFilters.PathPoseForce;
        try
        {
            LogFilters.PathPoseForce = true;
            host.Server.MainLoop(50);
            // Second tick may cross the 2s path-pose diag bucket depending on TickCount64.
            host.Server.MainLoop(50);
        }
        finally
        {
            LogFilters.PathPoseForce = prevPathPose;
        }
    }

    [TestMethod]
    public void Start_WithEphemeralPorts_StartsLoopThenShutdown()
    {
        using var host = new SectorServerHost(disposeViaShutdown: false);
        var udpPort = GetFreeUdpPort();
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = udpPort,
                PublicAddress = "127.0.0.1",
                EnableDevControl = true,
                DevControlPort = 0 // ephemeral TCP
            }
        });

        Assert.IsTrue(host.Server.Start());
        Assert.IsTrue(host.Server.IsRunning);

        // Let the main loop tick at least once.
        Thread.Sleep(120);

        host.Server.Shutdown();
        Assert.IsFalse(host.Server.IsRunning);
        Assert.IsNull(host.Server.Interface);
    }

    [TestMethod]
    public void Start_WhenDevControlDisabled_DoesNotRequireDevPort()
    {
        using var host = new SectorServerHost(disposeViaShutdown: false);
        host.Server.Setup(new SectorConfig
        {
            GameConfig = new GameConfig
            {
                Port = GetFreeUdpPort(),
                PublicAddress = "127.0.0.1",
                EnableDevControl = false
            }
        });

        Assert.IsTrue(host.Server.Start());
        host.Server.Shutdown();
    }

    private static int GetFreeUdpPort()
    {
        using var udp = new UdpClient(0);
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    /// <summary>Owns a SectorServer and always tears down UDP/TNL without leaving Loop running.</summary>
    private sealed class SectorServerHost : IDisposable
    {
        private readonly bool _disposeViaShutdown;
        public SectorServer Server { get; } = new();

        public SectorServerHost(bool disposeViaShutdown = true)
        {
            _disposeViaShutdown = disposeViaShutdown;
        }

        public void Dispose()
        {
            if (_disposeViaShutdown)
            {
                try
                {
                    Server.Shutdown();
                }
                catch
                {
                    // Best-effort; residual UDP may linger until GC if TNL socket not closed.
                }
            }
            else if (Server.Interface != null)
            {
                Server.Interface.Socket?.Stop();
                Server.Interface.Close();
            }

            SectorLoopControl.GetLoopMilliseconds = null;
            SectorLoopControl.TrySetLoopMilliseconds = null;
        }
    }
}
