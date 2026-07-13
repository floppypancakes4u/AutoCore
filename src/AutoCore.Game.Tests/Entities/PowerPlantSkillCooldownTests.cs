using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using System.IO;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;

/// <summary>
/// Client cast-again duration is ceil(skillCooldownMs * powerPlant.SkillCooldown) + charge.
/// SkillCooldown must be the identity multiplier 1.0; 0.0 zeroes hotbar cooldowns for all skills
/// (Ghidra FUN_0052a9b0 / FUN_0051e240; Tesla Strike live: recast at ~1.4s while server still on 14s CD).
/// </summary>
[TestClass]
public class PowerPlantSkillCooldownTests
{
    [TestInitialize]
    public void TestInitialize() => AssetManagerTestHelper.ClearRegisteredCloneBases();

    [TestCleanup]
    public void TestCleanup() => AssetManagerTestHelper.ClearRegisteredCloneBases();

    [TestMethod]
    public void WriteToPacket_SkillCooldown_IsIdentityMultiplier()
    {
        const int cbid = 710_001;
        AssetManagerTestHelper.RegisterPowerPlantCloneBase(cbid, mass: 12.5f);

        var plant = new PowerPlant();
        plant.SetCoid(91001, true);
        plant.LoadCloneBase(cbid);

        var packet = new CreatePowerPlantPacket();
        plant.WriteToPacket(packet);

        Assert.AreEqual(1.0f, packet.SkillCooldown,
            "CreatePowerPlant.SkillCooldown is the equipped cast-again multiplier; 0.0 zeroes client skill cooldowns.");
        Assert.AreEqual(12.5f, packet.Mass);
    }

    [TestMethod]
    public void WriteToPacket_SkillCooldown_IsWrittenAsTrailingFloat()
    {
        const int cbid = 710_002;
        AssetManagerTestHelper.RegisterPowerPlantCloneBase(cbid);

        var plant = new PowerPlant();
        plant.SetCoid(91002, true);
        plant.LoadCloneBase(cbid);

        var packet = new CreatePowerPlantPacket();
        plant.WriteToPacket(packet);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        writer.Flush();
        var body = ms.ToArray();

        Assert.IsTrue(body.Length >= sizeof(float), "power plant body must include SkillCooldown");
        var skillCooldown = BitConverter.ToSingle(body, body.Length - sizeof(float));
        Assert.AreEqual(1.0f, skillCooldown,
            "trailing CreatePowerPlant float must be identity SkillCooldown for client FUN_0052a9b0");
    }
}
