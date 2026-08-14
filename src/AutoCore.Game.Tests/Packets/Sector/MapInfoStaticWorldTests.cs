using System.Net;
using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

/// <summary>
/// PDB Pass 16: MapInfo 0x2005 must carry destination FAM/static-world metadata.
/// Retail maps1-4.glm + misc.glm have NumModulePlacements == 0 on every .fam;
/// empty module list means "load the FAM VOGOs, do not overlay .mod packs".
/// </summary>
[TestClass]
public class MapInfoStaticWorldTests
{
    private const long CharCoid = 9_082_000_101L;
    private const long VehicleCoid = 9_082_000_102L;

    private readonly List<BasePacket> _sent = new();
    private Func<int, SectorMap> _previousResolver;
    private bool _previousSuppress;

    [TestInitialize]
    public void Init()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        _previousResolver = MapManager.Instance.ResolveMapForTests;
        _previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        TNLConnection.MissionFlushForTests = () => { };
        TNLConnection.WorldStatePersistenceForTests = new NoopWorldStatePersistence();
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.MissionFlushForTests = null;
        TNLConnection.WorldStatePersistenceForTests = null;
        MapManager.Instance.ResolveMapForTests = _previousResolver;
        MapManager.Instance.SuppressCreatePacketsForTests = _previousSuppress;
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
        _sent.Clear();
    }

    [TestMethod]
    public void MapInfo_MapName_SelectsCorrectFam()
    {
        var packet = FillTown();
        Assert.AreEqual("sec_f_m_map_town_c7_1_tocado_01.fam", packet.MapName);
        Assert.IsFalse(packet.MapName.EndsWith(".fam.fam", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void MapInfo_ModuleCountAndRecords_MatchMapMetadata()
    {
        var packet = FillTown();
        Assert.AreEqual(0, packet.NumModulePlacements);
        Assert.AreEqual(0, packet.ModulePlacements.Count);

        var map = CreateMap(398, "sec_f_b_map_hwy_a2_1_scrapvalley", isTown: false);
        map.MapData.ModulePlacements.Add(new MapInfoModulePlacement { ModuleId = 164 });
        var withModules = new MapInfoPacket();
        map.Fill(withModules);
        Assert.AreEqual(1, withModules.NumModulePlacements);
        Assert.AreEqual(164, withModules.ModulePlacements[0].ModuleId);
    }

    [TestMethod]
    public void MapInfo_IterationVersion_MatchesRetailMetadata()
    {
        var map = CreateMap(698, "sec_f_m_map_mis_c7_1_tierraroja_tutorial");
        map.MapData.SetMapInfoHeaderForTests(iterationVersion: 852);
        var packet = new MapInfoPacket();
        map.Fill(packet);
        Assert.AreEqual(852, packet.MapIterationVersion);
    }

    [TestMethod]
    public void MapInfo_LayerId_MatchesRetailMetadata()
    {
        var packet = FillTown();
        Assert.AreEqual(0, packet.LayerId,
            "ChooseLayer is not implemented; retail FAMs instantiate all VOGOs regardless. Layer 0 is the first of 8 XML slots.");
    }

    [TestMethod]
    public void MapInfo_CoidFields_MatchClientMeaning()
    {
        var packet = FillTown();
        Assert.AreEqual(392, packet.ContinentObjectId,
            "SetMapInfo dest+0xFC / FUN_008BB520 continent hash key");
        Assert.AreEqual(392, packet.Coid,
            "120-byte header Coid at body+0x70; not the local player TFID");
        Assert.AreEqual(392, packet.CoidMap,
            "dynamic tail u64 copied to dest+0x948; module COID rebase base");
    }

    [TestMethod]
    public void MapInfo_EmptyWeather_IsLegalOrCorrectlyPopulated()
    {
        var packet = FillTown();
        Assert.AreEqual(0, packet.WeatherUpdateSize,
            "FUN_00637990 readBits(0) is legal; FAM weather lives in the .fam, not this blob");

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);
        Assert.AreEqual(148, ms.Length);
    }

    [TestMethod]
    public void Town_MapInfo_Metadata()
    {
        var packet = FillTown();
        Assert.IsTrue(packet.IsTown);
        Assert.IsFalse(packet.IsArena);
        Assert.IsTrue(packet.IsPersistent);
        Assert.AreEqual("sec_f_m_map_town_c7_1_tocado_01.fam", packet.MapName);
        Assert.AreEqual(0, packet.NumModulePlacements);
    }

    [TestMethod]
    public void Field_MapInfo_Metadata()
    {
        var map = CreateMap(398, "sec_f_b_map_hwy_a2_1_scrapvalley", isTown: false, isPersistent: true);
        var packet = new MapInfoPacket();
        map.Fill(packet);
        Assert.IsFalse(packet.IsTown);
        Assert.IsTrue(packet.IsPersistent);
        Assert.AreEqual("sec_f_b_map_hwy_a2_1_scrapvalley.fam", packet.MapName);
        Assert.AreEqual(0, packet.NumModulePlacements);
    }

    [TestMethod]
    public void Instance_MapInfo_Metadata()
    {
        var map = CreateMap(698, "sec_f_m_map_mis_c7_1_tierraroja_tutorial", isTown: false, isPersistent: false);
        map.MapData.SetMapInfoHeaderForTests(iterationVersion: 852);
        var packet = new MapInfoPacket();
        map.Fill(packet);
        Assert.IsFalse(packet.IsTown);
        Assert.IsFalse(packet.IsPersistent);
        Assert.AreEqual("sec_f_m_map_mis_c7_1_tierraroja_tutorial.fam", packet.MapName);
        Assert.AreEqual(852, packet.MapIterationVersion);
        Assert.AreEqual(698, packet.ContinentObjectId);
        Assert.AreEqual(0, packet.NumModulePlacements);
    }

    [TestMethod]
    public void MapTransfer_UsesDestinationMetadata()
    {
        var dest = CreateMap(522, "sec_f_b_map_town_a2_1_fort-logan_01", isTown: true);
        dest.MapData.SetMapInfoHeaderForTests(iterationVersion: 508);
        var (character, _) = CreateTransferableOnSourceMap();
        MapManager.Instance.ResolveMapForTests = _ => dest;

        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, 522));

        var packet = _sent.OfType<MapInfoPacket>().Single();
        Assert.AreEqual("sec_f_b_map_town_a2_1_fort-logan_01.fam", packet.MapName);
        Assert.AreEqual(508, packet.MapIterationVersion);
        Assert.AreEqual(522, packet.ContinentObjectId);
        Assert.IsTrue(packet.IsTown);
        Assert.AreEqual(0, packet.NumModulePlacements);
    }

    [TestMethod]
    public void RepeatedTransfers_DoNotLeakPreviousMapMetadata()
    {
        var mapB = CreateMap(392, "sec_f_m_map_town_c7_1_tocado_01", isTown: true);
        mapB.MapData.SetMapInfoHeaderForTests(iterationVersion: 600);
        var mapC = CreateMap(398, "sec_f_b_map_hwy_a2_1_scrapvalley", isTown: false);
        mapC.MapData.SetMapInfoHeaderForTests(iterationVersion: 2155);

        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, mapB);
        var first = _sent.OfType<MapInfoPacket>().Last();
        Assert.AreEqual("sec_f_m_map_town_c7_1_tocado_01.fam", first.MapName);
        Assert.AreEqual(600, first.MapIterationVersion);
        Assert.IsTrue(first.IsTown);

        InvokeStage2(connection, character.ObjectId.Coid);
        InvokeStage3Ack(connection, character.ObjectId.Coid);
        _sent.Clear();

        TransferOnto(character, mapC);
        var second = _sent.OfType<MapInfoPacket>().Single();
        Assert.AreEqual("sec_f_b_map_hwy_a2_1_scrapvalley.fam", second.MapName);
        Assert.AreEqual(2155, second.MapIterationVersion);
        Assert.AreEqual(398, second.ContinentObjectId);
        Assert.IsFalse(second.IsTown);
        Assert.AreNotEqual(first.MapName, second.MapName);
        Assert.AreNotEqual(first.MapIterationVersion, second.MapIterationVersion);
    }

    [TestMethod]
    public void SameMapResync_DoesNotResendMapInfo()
    {
        var dest = CreateMap(392, "sec_f_m_map_town_c7_1_tocado_01", isTown: true);
        var (character, connection) = CreateTransferableOnSourceMap();
        connection.SuppressCreatePacketsForTests = true;
        character.SetMap(dest);
        character.CurrentVehicle.SetMap(dest);
        _sent.Clear();

        connection.ResyncLocalPlayerAtCurrentPose(character);

        Assert.IsFalse(_sent.OfType<MapInfoPacket>().Any(),
            "same-map pose snap must not re-send MapInfo/module metadata");
    }

    private static MapInfoPacket FillTown()
    {
        var map = CreateMap(392, "sec_f_m_map_town_c7_1_tocado_01", isTown: true, isPersistent: true);
        var packet = new MapInfoPacket();
        map.Fill(packet);
        return packet;
    }

    private static SectorMap CreateMap(int continentId, string mapFileName, bool isTown = false, bool isPersistent = true)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = mapFileName,
            DisplayName = mapFileName,
            IsTown = isTown,
            IsPersistent = isPersistent,
            Objective = isTown ? 1 : -1,
        };
        return SectorMap.CreateForTests(continent, new Vector4(10f, 20f, 30f, 0f));
    }

    private (Character Character, TNLConnection Connection) CreateTransferableOnSourceMap()
    {
        var source = CreateMap(558, "tm_mapinfo_src");
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));

        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(source);
        vehicle.SetMap(source);

        ObjectManager.Instance.Add(character);
        return (character, connection);
    }

    private void TransferOnto(Character character, SectorMap dest)
    {
        MapManager.Instance.ResolveMapForTests = _ => dest;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, dest.ContinentId));
    }

    private static void InvokeStage2(TNLConnection connection, long characterCoid)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0u);
            writer.Write(characterCoid);
        }

        InvokeHandler(connection, "HandleTransferFromGlobalStage2Packet", stream.ToArray());
    }

    private static void InvokeStage3Ack(TNLConnection connection, long characterCoid)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0u);
            writer.Write(characterCoid);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0);
        }

        InvokeHandler(connection, "HandleTransferFromGlobalStage3Packet", stream.ToArray());
    }

    private static void InvokeHandler(TNLConnection connection, string methodName, byte[] body)
    {
        var method = typeof(TNLConnection).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method, $"Missing handler {methodName}");
        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }

    private sealed class NoopWorldStatePersistence : ICharacterWorldStatePersistence
    {
        public void Save(CharacterWorldStateSnapshot snapshot)
        {
        }
    }
}
