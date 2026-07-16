using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Npc;

[TestClass]
public class ServerConfigTests
{
    [TestInitialize]
    public void SetUp()
    {
        ServerConfig.ResetToDefaults();
        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = false;
    }

    [TestCleanup]
    public void TearDown()
    {
        ServerConfig.ResetToDefaults();
        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = false;
    }

    [TestMethod]
    public void Defaults_AreRetailSafe()
    {
        Assert.IsFalse(ServerConfig.NpcVehiclePhysicsEnabled);
        Assert.AreEqual(NpcVehicleControllerTier.Hard, ServerConfig.ControllerTier);
        Assert.AreEqual(60, ServerConfig.SubstepHz);
        Assert.AreEqual(-9.81f, ServerConfig.Gravity, 1e-6f);
        Assert.IsNull(ServerConfig.AirDensityOverride);
        Assert.IsFalse(ServerConfig.DebugLogging);
        Assert.IsFalse(ServerConfig.CompositeWheelCollisionEnabled);
    }

    [TestMethod]
    public void ApplyFromYaml_FullSection_SetsAll()
    {
        var yaml = """
            npcVehiclePhysics:
              controllerTier: physics
              enabled: true
              substepHz: 120
              gravity: -12.5
              airDensityOverride: 1.2
              debugLogging: true
              compositeWheelCollisionEnabled: true
            """;

        Assert.IsTrue(ServerConfig.ApplyFromYaml(yaml, out var error), error);
        Assert.IsNull(error);
        Assert.IsTrue(ServerConfig.NpcVehiclePhysicsEnabled);
        Assert.AreEqual(NpcVehicleControllerTier.Physics, ServerConfig.ControllerTier);
        Assert.AreEqual(120, ServerConfig.SubstepHz);
        Assert.AreEqual(-12.5f, ServerConfig.Gravity, 1e-6f);
        Assert.AreEqual(1.2f, ServerConfig.AirDensityOverride!.Value, 1e-6f);
        Assert.IsTrue(ServerConfig.DebugLogging);
        Assert.IsTrue(ServerConfig.CompositeWheelCollisionEnabled);
    }

    [TestMethod]
    public void ApplyFromYaml_MissingSection_KeepsDefaults()
    {
        Assert.IsTrue(ServerConfig.ApplyFromYaml("someOtherSetting: 5", out var error), error);
        Assert.IsFalse(ServerConfig.NpcVehiclePhysicsEnabled);
        Assert.AreEqual(NpcVehicleControllerTier.Hard, ServerConfig.ControllerTier);
    }

    [TestMethod]
    public void ApplyFromYaml_PartialSection_KeepsOtherDefaults()
    {
        var yaml = """
            npcVehiclePhysics:
              enabled: true
            """;

        Assert.IsTrue(ServerConfig.ApplyFromYaml(yaml, out var error), error);
        Assert.IsTrue(ServerConfig.NpcVehiclePhysicsEnabled);
        // Untouched keys keep defaults.
        Assert.AreEqual(NpcVehicleControllerTier.Hard, ServerConfig.ControllerTier);
        Assert.AreEqual(60, ServerConfig.SubstepHz);
    }

    [TestMethod]
    public void ApplyFromYaml_UnknownKeys_AreIgnored()
    {
        var yaml = """
            npcVehiclePhysics:
              enabled: true
              perClassOverrides:
                tank: { substepHz: 30 }
              futureSetting: hello
            """;

        Assert.IsTrue(ServerConfig.ApplyFromYaml(yaml, out var error), error);
        Assert.IsTrue(ServerConfig.NpcVehiclePhysicsEnabled);
    }

    [TestMethod]
    public void ApplyFromYaml_ControllerTier_IsCaseInsensitive()
    {
        Assert.IsTrue(ServerConfig.ApplyFromYaml("npcVehiclePhysics:\n  controllerTier: KINEMATIC", out var error), error);
        Assert.AreEqual(NpcVehicleControllerTier.Kinematic, ServerConfig.ControllerTier);
    }

    [TestMethod]
    public void ApplyFromYaml_InvalidControllerTier_ReturnsError()
    {
        Assert.IsFalse(ServerConfig.ApplyFromYaml("npcVehiclePhysics:\n  controllerTier: rocket", out var error));
        Assert.IsNotNull(error);
        // Invalid input must not corrupt state.
        Assert.AreEqual(NpcVehicleControllerTier.Hard, ServerConfig.ControllerTier);
    }

    [TestMethod]
    public void ApplyFromYaml_Empty_ReturnsError()
    {
        Assert.IsFalse(ServerConfig.ApplyFromYaml("   ", out var error));
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void ApplyFromYaml_MalformedYaml_ReturnsError()
    {
        // Unbalanced flow mapping.
        Assert.IsFalse(ServerConfig.ApplyFromYaml("npcVehiclePhysics: { enabled: true", out var error));
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void SubstepHz_IsClamped()
    {
        ServerConfig.SubstepHz = 0;
        Assert.AreEqual(1, ServerConfig.SubstepHz);
        ServerConfig.SubstepHz = 100000;
        Assert.AreEqual(480, ServerConfig.SubstepHz);
        ServerConfig.SubstepHz = 90;
        Assert.AreEqual(90, ServerConfig.SubstepHz);
    }

    [TestMethod]
    public void ResetToDefaults_RestoresAll()
    {
        ServerConfig.NpcVehiclePhysicsEnabled = true;
        ServerConfig.ControllerTier = NpcVehicleControllerTier.Physics;
        ServerConfig.SubstepHz = 33;
        ServerConfig.AirDensityOverride = 9f;
        ServerConfig.CompositeWheelCollisionEnabled = true;
        ServerConfig.ResetToDefaults();
        Assert.IsFalse(ServerConfig.NpcVehiclePhysicsEnabled);
        Assert.AreEqual(NpcVehicleControllerTier.Hard, ServerConfig.ControllerTier);
        Assert.AreEqual(60, ServerConfig.SubstepHz);
        Assert.IsNull(ServerConfig.AirDensityOverride);
        Assert.IsFalse(ServerConfig.CompositeWheelCollisionEnabled);
    }

    [TestMethod]
    public void ResolveVehicleMoverTier_PhysicsRequiresEnabledAndTier()
    {
        ServerConfig.ControllerTier = NpcVehicleControllerTier.Physics;
        ServerConfig.NpcVehiclePhysicsEnabled = false;
        Assert.AreEqual(NpcVehicleControllerTier.Hard, ServerConfig.ResolveVehicleMoverTier());

        ServerConfig.NpcVehiclePhysicsEnabled = true;
        Assert.AreEqual(NpcVehicleControllerTier.Physics, ServerConfig.ResolveVehicleMoverTier());
    }

    [TestMethod]
    public void ResolveVehicleMoverTier_WireLeverMapsWhenConfigHard()
    {
        Assert.AreEqual(NpcVehicleControllerTier.Hard, ServerConfig.ResolveVehicleMoverTier());

        NpcVehicleDriveController.Enabled = true;
        Assert.AreEqual(NpcVehicleControllerTier.Kinematic, ServerConfig.ResolveVehicleMoverTier());

        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = true;
        Assert.AreEqual(NpcVehicleControllerTier.Soft, ServerConfig.ResolveVehicleMoverTier());
    }

    [TestMethod]
    public void ResolveVehicleMoverTier_ExplicitKinematicIgnoresSoftLever()
    {
        ServerConfig.ControllerTier = NpcVehicleControllerTier.Kinematic;
        SoftNpcPathMotion.Enabled = true;
        Assert.AreEqual(NpcVehicleControllerTier.Kinematic, ServerConfig.ResolveVehicleMoverTier());
    }
}
