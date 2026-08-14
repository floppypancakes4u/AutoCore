using System.IO;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

/// <summary>
/// Round-trip coverage for <see cref="MapInfoPacket"/> manual packing layout.
/// </summary>
[TestClass]
public class MapInfoPacketTests
{
    [TestMethod]
    public void WriteThenRead_RoundTripsCoreFields()
    {
        var original = new MapInfoPacket
        {
            RegionId = 7,
            RegionType = (TilesetType)3,
            RegionLevel = 12,
            LayerId = 4,
            ObjectiveIndex = 2,
            MapName = "tm_arkbay",
            IsTown = true,
            IsArena = false,
            OwningFaction = 1,
            ContinentObjectId = 88,
            IsPersistent = true,
            MapIterationVersion = 5,
            ContestedMissionId = -1,
            Coid = 1001,
            TemporalRandomSeed = 42,
            CoidMap = 2002,
            PositionX = 10.5f,
            PositionY = 20.25f,
            PositionZ = -3f,
            WeatherUpdateSize = 4,
        };
        original.ModulePlacements.Add(new MapInfoModulePlacement { ModuleId = 164 });
        original.ModulePlacements.Add(new MapInfoModulePlacement { ModuleId = 165 });

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            original.Write(writer);

        ms.Position = 0;
        var restored = new MapInfoPacket();
        using (var reader = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            restored.Read(reader);

        Assert.AreEqual(original.RegionId, restored.RegionId);
        Assert.AreEqual(original.RegionType, restored.RegionType);
        Assert.AreEqual(original.RegionLevel, restored.RegionLevel);
        Assert.AreEqual(original.LayerId, restored.LayerId);
        Assert.AreEqual(original.ObjectiveIndex, restored.ObjectiveIndex);
        Assert.AreEqual(original.MapName, restored.MapName);
        Assert.IsTrue(restored.IsTown);
        Assert.IsFalse(restored.IsArena);
        Assert.AreEqual(original.OwningFaction, restored.OwningFaction);
        Assert.AreEqual(original.ContinentObjectId, restored.ContinentObjectId);
        Assert.IsTrue(restored.IsPersistent);
        Assert.AreEqual(original.MapIterationVersion, restored.MapIterationVersion);
        Assert.AreEqual(original.ContestedMissionId, restored.ContestedMissionId);
        Assert.AreEqual(original.Coid, restored.Coid);
        Assert.AreEqual(original.TemporalRandomSeed, restored.TemporalRandomSeed);
        Assert.AreEqual(original.CoidMap, restored.CoidMap);
        Assert.AreEqual(2, restored.NumModulePlacements);
        Assert.AreEqual(2, restored.ModulePlacements.Count);
        Assert.AreEqual(164, restored.ModulePlacements[0].ModuleId);
        Assert.AreEqual(165, restored.ModulePlacements[1].ModuleId);
        Assert.AreEqual(original.PositionX, restored.PositionX, 1e-5f);
        Assert.AreEqual(original.PositionY, restored.PositionY, 1e-5f);
        Assert.AreEqual(original.PositionZ, restored.PositionZ, 1e-5f);
        Assert.AreEqual(original.WeatherUpdateSize, restored.WeatherUpdateSize);
    }

    [TestMethod]
    public void Write_ZeroModulesAndWeather_RoundTrips()
    {
        var original = new MapInfoPacket
        {
            MapName = "x",
            NumModulePlacements = 0,
            WeatherUpdateSize = 0,
            PositionX = 1f,
            PositionY = 2f,
            PositionZ = 3f,
        };

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            original.Write(writer);

        ms.Position = 0;
        var restored = new MapInfoPacket();
        using (var reader = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            restored.Read(reader);

        Assert.AreEqual(0, restored.NumModulePlacements);
        Assert.AreEqual(0, restored.WeatherUpdateSize);
        Assert.AreEqual(1f, restored.PositionX, 1e-5f);
        Assert.AreEqual(2f, restored.PositionY, 1e-5f);
        Assert.AreEqual(3f, restored.PositionZ, 1e-5f);
    }

    /// <summary>
    /// Client FUN_00637990 skipOpcode bitstream (Pass 2): first 0x3C0 bits (120 bytes),
    /// then u32 seed, u64 CoidMap, u16 module count, 24*N modules, XYZ, u16 weather size.
    /// Offsets below are into the Form A body AutoCore writes (no opcode prefix).
    /// </summary>
    [TestMethod]
    public void Write_SkipOpcodeBody_MatchesClientBitstreamOffsets()
    {
        var packet = new MapInfoPacket
        {
            RegionId = 0x11111111,
            RegionType = (TilesetType)0x22222222,
            RegionLevel = 0x33,
            LayerId = 0x44444444,
            ObjectiveIndex = 7,
            MapName = "tm_arkbay",
            IsTown = true,
            IsArena = false,
            OwningFaction = 2,
            ContinentObjectId = 88,
            IsPersistent = true,
            MapIterationVersion = 0x55667788,
            ContestedMissionId = -1,
            Coid = 0x0102030405060708L,
            TemporalRandomSeed = 0x12345678,
            CoidMap = 0x0A0B0C0D0E0F1011L,
            NumModulePlacements = 0,
            PositionX = 1f,
            PositionY = 2f,
            PositionZ = 3f,
            WeatherUpdateSize = 0,
        };

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);

        var bytes = ms.ToArray();

        // 120-byte header + 4 seed + 8 coidMap + 2 count + 12 xyz + 2 weatherSize
        Assert.AreEqual(148, bytes.Length);

        Assert.AreEqual(0x11111111, BitConverter.ToInt32(bytes, 0x00));
        // MapName at bitstream +0x14 (RecvMapInfo normalized +0x1C after injected opcode dword pair).
        Assert.AreEqual((byte)'t', bytes[0x14]);
        Assert.AreEqual((byte)'m', bytes[0x15]);
        Assert.AreEqual(1, bytes[0x55]); // IsTown
        Assert.AreEqual(0, bytes[0x56]); // IsArena
        Assert.AreEqual(0x55667788, BitConverter.ToInt32(bytes, 0x64)); // MapIterationVersion
        Assert.AreEqual(0x0102030405060708L, BitConverter.ToInt64(bytes, 0x70)); // Coid in header
        Assert.AreEqual(0x12345678, BitConverter.ToInt32(bytes, 0x78)); // TemporalRandomSeed (first field after 120-byte header)
        Assert.AreEqual(0x0A0B0C0D0E0F1011L, BitConverter.ToInt64(bytes, 0x7C));
        Assert.AreEqual((short)0, BitConverter.ToInt16(bytes, 0x84));
        Assert.AreEqual(1f, BitConverter.ToSingle(bytes, 0x86), 1e-5f);
        Assert.AreEqual(2f, BitConverter.ToSingle(bytes, 0x8A), 1e-5f);
        Assert.AreEqual(3f, BitConverter.ToSingle(bytes, 0x8E), 1e-5f);
        Assert.AreEqual((short)0, BitConverter.ToInt16(bytes, 0x92));
    }

    [TestMethod]
    public void Write_OneModule_Inserts24BytesBeforePosition()
    {
        var packet = new MapInfoPacket
        {
            MapName = "x",
            PositionX = 9f,
            WeatherUpdateSize = 0,
        };
        packet.ModulePlacements.Add(new MapInfoModulePlacement());

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);

        var bytes = ms.ToArray();
        Assert.AreEqual(148 + 24, bytes.Length);
        Assert.AreEqual((short)1, BitConverter.ToInt16(bytes, 0x84));
        Assert.AreEqual(9f, BitConverter.ToSingle(bytes, 0x86 + 24), 1e-5f);
    }

    /// <summary>
    /// Client FUN_00637990 reads count*0xC0 bits (24 bytes) per module. SetMapInfo
    /// 0x004CE230 copies six uint32s. FUN_004DFCC0 matches dword0/1 as the placement
    /// TFID and uses +0x08/+0x0C as the COID rebase and +0x10 as the module-id key.
    /// Skipping 24 bytes writes zeros and drops a real overlay.
    /// </summary>
    [TestMethod]
    public void Write_ModuleRecord_EmitsSixDwords_NotSkippedZeros()
    {
        var packet = new MapInfoPacket
        {
            MapName = "x",
            PositionX = 1f,
            PositionY = 2f,
            PositionZ = 3f,
            WeatherUpdateSize = 0,
        };
        packet.ModulePlacements.Add(new MapInfoModulePlacement
        {
            PlacementCoidLow = 0x11111111,
            PlacementCoidHigh = 0x22222222,
            RebaseCoidLow = 0x33333333,
            RebaseCoidHigh = 0x44444444,
            ModuleId = 0x000001A4,
            Unknown14 = 0x55555555,
        });

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);

        var bytes = ms.ToArray();
        Assert.AreEqual(1, packet.NumModulePlacements);
        Assert.AreEqual(148 + 24, bytes.Length);
        Assert.AreEqual((short)1, BitConverter.ToInt16(bytes, 0x84));
        Assert.AreEqual(0x11111111, BitConverter.ToInt32(bytes, 0x86));
        Assert.AreEqual(0x22222222, BitConverter.ToInt32(bytes, 0x8A));
        Assert.AreEqual(0x33333333, BitConverter.ToInt32(bytes, 0x8E));
        Assert.AreEqual(0x44444444, BitConverter.ToInt32(bytes, 0x92));
        Assert.AreEqual(0x000001A4, BitConverter.ToInt32(bytes, 0x96));
        Assert.AreEqual(0x55555555, BitConverter.ToInt32(bytes, 0x9A));
        Assert.AreEqual(1f, BitConverter.ToSingle(bytes, 0x9E), 1e-5f);
    }
}
