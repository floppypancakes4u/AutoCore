using System.Linq;
using System.Reflection;
using AutoCore.Game.Constants;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

/// <summary>
/// Exhaustive opcode audit: pin retail numeric values for production-critical GameOpcodes.
/// Client evidence: PDB/Ghidra Process_EMSG_* and PackedPackets::unpackPacket @ 0x00637C20.
/// </summary>
[TestClass]
public class OpcodeNumericValueTests
{
    [TestMethod]
    [DataRow(nameof(GameOpcode.MapInfo), 0x2005u)]
    [DataRow(nameof(GameOpcode.Damage), 0x2023u)]
    [DataRow(nameof(GameOpcode.GroupReactionCall), 0x206Cu)]
    [DataRow(nameof(GameOpcode.MapInstanceListResponse), 0x804Du)]
    [DataRow(nameof(GameOpcode.CreateCreature), 0x2013u)]
    [DataRow(nameof(GameOpcode.CreateCharacter), 0x2015u)]
    [DataRow(nameof(GameOpcode.CreateCharacterExtended), 0x2016u)]
    [DataRow(nameof(GameOpcode.CreateVehicle), 0x201Du)]
    [DataRow(nameof(GameOpcode.CreateVehicleExtended), 0x201Eu)]
    [DataRow(nameof(GameOpcode.CreateSimpleObject), 0x2012u)]
    [DataRow(nameof(GameOpcode.CreateWheelSet), 0x201Bu)]
    [DataRow(nameof(GameOpcode.CreateWeapon), 0x201Cu)]
    [DataRow(nameof(GameOpcode.CreatePowerPlant), 0x2018u)]
    [DataRow(nameof(GameOpcode.CreateArmor), 0x2060u)]
    [DataRow(nameof(GameOpcode.DestroyObject), 0x2020u)]
    [DataRow(nameof(GameOpcode.RequestObject), 0x2011u)]
    [DataRow(nameof(GameOpcode.TransferFromGlobal), 0x2000u)]
    [DataRow(nameof(GameOpcode.TransferFromGlobalStage2), 0x2001u)]
    [DataRow(nameof(GameOpcode.TransferFromGlobalStage3), 0x2002u)]
    [DataRow(nameof(GameOpcode.TransferToSector), 0x801Bu)]
    [DataRow(nameof(GameOpcode.VehicleMoved), 0x200Au)]
    [DataRow(nameof(GameOpcode.CreatureMoved), 0x2008u)]
    [DataRow(nameof(GameOpcode.Firing), 0x2022u)]
    [DataRow(nameof(GameOpcode.Broadcast), 0x2021u)]
    [DataRow(nameof(GameOpcode.LogicStateChange), 0x206Bu)]
    [DataRow(nameof(GameOpcode.MissionDialog), 0x206Du)]
    [DataRow(nameof(GameOpcode.MissionDialogResponse), 0x206Eu)]
    [DataRow(nameof(GameOpcode.SpecialEvent), 0x20A9u)]
    [DataRow(nameof(GameOpcode.InventoryAddItem), 0x2047u)]
    [DataRow(nameof(GameOpcode.InventoryAddItemResponse), 0x2047u)]
    [DataRow(nameof(GameOpcode.Unknown2048), 0x2048u)]
    [DataRow(nameof(GameOpcode.Chat), 0x8000u)]
    [DataRow(nameof(GameOpcode.ConvoyMissionsRequest), 0x800Fu)]
    [DataRow(nameof(GameOpcode.ConvoyMissionsResponse), 0x8010u)]
    [DataRow(nameof(GameOpcode.AddFriend), 0x801Fu)]
    [DataRow(nameof(GameOpcode.RemoveFriend), 0x8021u)]
    [DataRow(nameof(GameOpcode.GetFriends), 0x8023u)]
    [DataRow(nameof(GameOpcode.AddIgnore), 0x8026u)]
    [DataRow(nameof(GameOpcode.RemoveIgnore), 0x8028u)]
    [DataRow(nameof(GameOpcode.GetIgnored), 0x802Au)]
    [DataRow(nameof(GameOpcode.AddEnemy), 0x802Cu)]
    [DataRow(nameof(GameOpcode.GetEnemies), 0x802Eu)]
    [DataRow(nameof(GameOpcode.RemoveEnemy), 0x8031u)]
    [DataRow(nameof(GameOpcode.RequestClanInfo), 0x803Au)]
    public void HighValueOpcode_MatchesRetailNumeric(string name, uint expected)
    {
        Assert.IsTrue(Enum.TryParse<GameOpcode>(name, out var opcode), name);
        Assert.AreEqual(expected, (uint)opcode, name);
    }

    [TestMethod]
    public void InventoryAddItem_AndResponse_ShareClientHandledValue()
    {
        Assert.AreEqual(GameOpcode.InventoryAddItem, GameOpcode.InventoryAddItemResponse);
        Assert.AreEqual(0x2047u, (uint)GameOpcode.InventoryAddItem);
        Assert.AreNotEqual(0x2047u, (uint)GameOpcode.Unknown2048);
    }

    [TestMethod]
    public void GameOpcode_HasNoUnexpectedDuplicateValues_ExceptDocumentedAlias()
    {
        var values = Enum.GetValues<GameOpcode>()
            .GroupBy(v => (uint)v)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.AreEqual(1, values.Count, "Only 0x2047 alias should duplicate.");
        Assert.AreEqual(0x2047u, values[0].Key);
        CollectionAssert.AreEquivalent(
            new[] { GameOpcode.InventoryAddItem, GameOpcode.InventoryAddItemResponse },
            values[0].ToArray());
    }

    [TestMethod]
    public void TnlInterface_VersionAndFragment_MatchRetail175And220()
    {
        Assert.AreEqual(175, AutoCore.Game.TNL.TNLInterface.Version);
        var iface = new AutoCore.Game.TNL.TNLInterface(doGhosting: false, skipNetworkBind: true);
        Assert.AreEqual(220, iface.FragmentSize);
        Assert.AreEqual(175, iface.ExpectedVersion);
    }
}
