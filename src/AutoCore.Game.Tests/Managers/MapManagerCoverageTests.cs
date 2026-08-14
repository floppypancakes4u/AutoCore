using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.Managers;

/// <summary>
/// Residual <see cref="MapManager"/> coverage: tick helpers, combat mode, transfer request.
/// </summary>
[TestClass]
public class MapManagerCoverageTests
{
    private readonly List<AutoCore.Game.Packets.BasePacket> _sent = new();

    [TestInitialize]
    public void Init()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        MapManager.Instance.ClearMapsForTests();
        MapManager.Instance.ResolveMapForTests = null;
        MapManager.Instance.SuppressCreatePacketsForTests = false;
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        MapManager.Instance.ClearMapsForTests();
        MapManager.Instance.ResolveMapForTests = null;
        MapManager.Instance.SuppressCreatePacketsForTests = false;
    }

    [TestMethod]
    public void Initialize_WithNoContinentObjects_ReturnsTrue()
    {
        // Production AssetManager has no continents in the test process.
        Assert.IsTrue(MapManager.Instance.Initialize());
    }

    [TestMethod]
    public void RebucketAllGrids_EmptyAndWithMap_DoesNotThrow()
    {
        MapManager.Instance.RebucketAllGrids();
        var map = CreateMap(9101);
        MapManager.Instance.RegisterMapForTests(map);
        MapManager.Instance.RebucketAllGrids();
    }

    [TestMethod]
    public void TickNpcs_SkipsEmptyMaps_TicksMapsWithPlayers()
    {
        var empty = CreateMap(9102);
        MapManager.Instance.RegisterMapForTests(empty);
        MapManager.Instance.TickNpcs(Environment.TickCount64, 0.05f);

        var populated = CreateMap(9103);
        var character = new Character();
        character.SetCoid(1, true);
        character.AttachTestDataForTests();
        character.SetMap(populated);
        MapManager.Instance.RegisterMapForTests(populated);
        Assert.IsTrue(populated.PlayerCount > 0);
        MapManager.Instance.TickNpcs(Environment.TickCount64, 0.05f);
    }

    [TestMethod]
    public void ForcePathVehiclePoseDirty_OnlyDiriesGhostedPathVehicles()
    {
        var map = CreateMap(9104);
        MapManager.Instance.RegisterMapForTests(map);

        // Empty map with no players → 0
        Assert.AreEqual(0, MapManager.Instance.ForcePathVehiclePoseDirty());

        var character = new Character();
        character.SetCoid(10, true);
        character.AttachTestDataForTests();
        character.SetMap(map);

        // Path vehicle without ghost refs → skipped
        var pathVeh = new Vehicle();
        pathVeh.SetCoid(11, true);
        pathVeh.CoidCurrentPath = 55;
        pathVeh.CreateGhost();
        pathVeh.NpcAi = new NpcAiState();
        pathVeh.SetMap(map);
        Assert.IsTrue(map.NpcAiEntities.Contains(pathVeh));

        Assert.AreEqual(0, MapManager.Instance.ForcePathVehiclePoseDirty(),
            "Without a live GhostInfo ref, path vehicle must not count as force-dirtied.");

        // Scope the ghost so GetFirstObjectRef is non-null.
        var connection = new ScopeProbeConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.BeginGhostingForTests();
        connection.ObjectInScope(pathVeh.Ghost!);

        Assert.AreEqual(1, MapManager.Instance.ForcePathVehiclePoseDirty());
        Assert.AreNotEqual(0UL, GetDirtyMaskBits(pathVeh.Ghost!) & GhostObject.PositionMask);
    }

    [TestMethod]
    public void HandleChangeCombatModeRequest_SendsSuccessResponse()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        var character = new Character();
        character.SetCoid(20, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(0); // pad 4
            w.Write(20L); // CharacterCoid
            w.Write((byte)2); // Mode
            w.Write(new byte[7]);
        }

        ms.Position = 0;
        MapManager.Instance.HandleChangeCombatModeRequest(character, new BinaryReader(ms));

        var response = _sent.OfType<ChangeCombatModeResponsePacket>().Single();
        Assert.AreEqual(20L, response.CharacterCoid);
        Assert.AreEqual((byte)2, response.Mode);
        Assert.IsTrue(response.Success);
    }

    [TestMethod]
    public void HandleTransferRequestPacket_NonContinentType_NoTransfer()
    {
        var character = new Character();
        character.SetCoid(30, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(new TNLConnection());
        character.SetCurrentVehicleForTests(new Vehicle());
        character.CurrentVehicle.SetCoid(31, true);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((int)MapTransferType.Highway);
            w.Write(1); // Data
            w.Write(0); // pad
            w.Write(0L);
            w.Write(false);
            w.Write(new byte[50]); // GMParameter
            w.Write(new byte[5]);
        }

        ms.Position = 0;
        MapManager.Instance.HandleTransferRequestPacket(character, new BinaryReader(ms));
        Assert.IsNull(character.Map);
    }

    [TestMethod]
    public void HandleTransferRequestPacket_ContinentObject_UsesResolver()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(40, true);
        character.AttachTestDataForTests();
        character.SetLastTownIdForTests(1);
        character.SetOwningConnection(connection);
        character.SetCurrentVehicleForTests(new Vehicle());
        character.CurrentVehicle.SetCoid(41, true);
        character.CurrentVehicle.AttachTestDataForTests();

        var dest = CreateMap(9200);
        MapManager.Instance.ResolveMapForTests = id => id == 9200 ? dest : null;
        MapManager.Instance.SuppressCreatePacketsForTests = true;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((int)MapTransferType.ContinentObject);
            w.Write(9200);
            w.Write(0);
            w.Write(0L);
            w.Write(false);
            w.Write(new byte[50]);
            w.Write(new byte[5]);
        }

        ms.Position = 0;
        MapManager.Instance.HandleTransferRequestPacket(character, new BinaryReader(ms));
        Assert.AreSame(dest, character.Map);
    }

    [TestMethod]
    public void RegisterMapForTests_GetMap_ReturnsRegistered()
    {
        var map = CreateMap(9300);
        MapManager.Instance.RegisterMapForTests(map);
        Assert.AreSame(map, MapManager.Instance.GetMap(9300));
    }

    private static SectorMap CreateMap(int id)
    {
        var continent = new ContinentObject
        {
            Id = id,
            MapFileName = $"map_mgr_{id}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private sealed class ScopeProbeConnection : TNLConnection
    {
    }

    private static readonly System.Reflection.FieldInfo DirtyMaskBitsField =
        typeof(NetObject).GetField("_dirtyMaskBits",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private static ulong GetDirtyMaskBits(NetObject obj) => (ulong)DirtyMaskBitsField.GetValue(obj)!;
}
