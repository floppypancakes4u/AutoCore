using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

[TestClass]
public class WireDiagCreateVehicleTests
{
    [TestCleanup]
    public void TearDown() => WireDiag.ResetForTests();

    [TestMethod]
    public void FormatCreateVehicleDetail_EmptyNestedWheelset_ReportsMinusOne()
    {
        var packet = new CreateVehiclePacket
        {
            CBID = 12425,
            TemplateId = 580,
            IsActive = true,
            CoidSpawnOwner = -1,
            ObjectId = new TFID(MapNpcIdentity.CoidBase + 1, true),
        };

        var detail = WireDiag.FormatCreateVehicleDetail(packet);

        StringAssert.Contains(detail, "vehicleCbid=12425");
        StringAssert.Contains(detail, "wheelsetCbid=-1");
        StringAssert.Contains(detail, "nested=empty");
        StringAssert.Contains(detail, "wheelOk=0");
        StringAssert.Contains(detail, "templateId=580");
        StringAssert.Contains(detail, "isActive=1");
    }

    [TestMethod]
    public void FormatCreateVehicleDetail_FullNestedWheelset_ReportsPositiveCbid()
    {
        var packet = new CreateVehiclePacket
        {
            CBID = 15478,
            TemplateId = -1,
            IsActive = true,
            CreateWheelSet = new CreateWheelSetPacket { CBID = 52 },
            ObjectId = new TFID(MapNpcIdentity.CoidBase + 2, true),
        };

        var detail = WireDiag.FormatCreateVehicleDetail(packet);

        StringAssert.Contains(detail, "wheelsetCbid=52");
        StringAssert.Contains(detail, "nested=full");
        StringAssert.Contains(detail, "wheelOk=1");
    }

    [TestMethod]
    public void FormatCreateVehicleDetail_ZeroWheelCbid_ReportsWheelOkZero()
    {
        // Path A capture: client saw wheel_cbid=0 and never SetWheelset.
        var packet = new CreateVehiclePacket
        {
            CBID = 99,
            CreateWheelSet = new CreateWheelSetPacket { CBID = 0 },
        };

        var detail = WireDiag.FormatCreateVehicleDetail(packet);

        StringAssert.Contains(detail, "wheelsetCbid=0");
        StringAssert.Contains(detail, "nested=full");
        StringAssert.Contains(detail, "wheelOk=0");
    }

    [TestMethod]
    public void RecordGamePacket_WithDetail_AppearsInFormattedLine()
    {
        WireDiag.Enabled = true;
        WireDiag.RecordGamePacket("CreateVehicle", coid: 1, bytes: 100, playerCoid: 2, detail: "wheelsetCbid=52 wheelOk=1");

        var line = WireDiag.FormatLine(WireDiag.Snapshot().Single());
        StringAssert.Contains(line, "wheelsetCbid=52");
        StringAssert.Contains(line, "wheelOk=1");
    }

    [TestMethod]
    public void ExtractNestedWheelCbidFromWire_FindsCreateWheelSetOpcodePayload()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)0x201D); // CreateVehicle opcode
        writer.Write(12345); // pretend CBID
        writer.Write(new byte[100]);
        writer.Write((uint)0x201B); // CreateWheelSet
        writer.Write(52); // wheel CBID
        writer.Write(new byte[16]);

        var cbid = WireDiag.ExtractNestedWheelCbidFromWire(ms.ToArray());
        Assert.AreEqual(52, cbid);
    }

    [TestMethod]
    public void ExtractNestedWheelCbidFromWire_MissingOpcode_ReturnsMinValue()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        Assert.AreEqual(int.MinValue, WireDiag.ExtractNestedWheelCbidFromWire(bytes));
    }

    [TestMethod]
    public void FormatCreateVehicleDetail_IncludesWheelTfid()
    {
        var packet = new CreateVehiclePacket
        {
            CBID = 1,
            CreateWheelSet = new CreateWheelSetPacket
            {
                CBID = 52,
                ObjectId = new TFID(MapNpcIdentity.CoidBase + 9, true),
            },
        };

        var detail = WireDiag.FormatCreateVehicleDetail(packet);
        StringAssert.Contains(detail, "wheelTfid=");
        StringAssert.Contains(detail, ":1");
    }
}
