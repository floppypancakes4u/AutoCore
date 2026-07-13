using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Continent transfers must land at the destination EnterPoint keyed by the source continent
/// (e.g. Upside 558 → Back Range EP type=ContinentObject data=558), not the map default EntryPoint.
/// </summary>
[TestClass]
public class MapTransferEnterPointTests
{
    private const int UpsideId = 558;
    private const int BackRangeId = 693;

    [TestMethod]
    public void TryResolveTransferSpawn_PrefersEnterPointForSourceContinent()
    {
        var map = CreateDestMap(BackRangeId, entry: new Vector4(1801f, 131f, 1525f, 0f));
        AddEnterPoint(map, coid: 8498, sourceContinentId: UpsideId,
            location: new Vector4(1240.8f, 61.9f, 2302.2f, 0f),
            rotation: new Quaternion(0f, 0.45f, 0f, 0.89f));
        AddEnterPoint(map, coid: 10308, sourceContinentId: 789,
            location: new Vector4(406f, 26f, 2299f, 0f),
            rotation: Quaternion.Default);

        var usedEnterPoint = MapTransferSpawn.TryResolve(
            map, UpsideId, out var pos, out var rot);

        Assert.IsTrue(usedEnterPoint);
        Assert.AreEqual(1240.8f, pos.X, 0.01f);
        Assert.AreEqual(61.9f, pos.Y, 0.01f);
        Assert.AreEqual(2302.2f, pos.Z, 0.01f);
        Assert.AreEqual(0.45f, rot.Y, 0.01f);
        Assert.AreEqual(0.89f, rot.W, 0.01f);
    }

    [TestMethod]
    public void TryResolveTransferSpawn_FallsBackToEntryPointWhenNoMatch()
    {
        var map = CreateDestMap(BackRangeId, entry: new Vector4(1801f, 131f, 1525f, 0f));
        AddEnterPoint(map, coid: 8498, sourceContinentId: UpsideId,
            location: new Vector4(1240f, 62f, 2302f, 0f),
            rotation: Quaternion.Default);

        var usedEnterPoint = MapTransferSpawn.TryResolve(
            map, sourceContinentId: 999, out var pos, out var rot);

        Assert.IsFalse(usedEnterPoint);
        Assert.AreEqual(1801f, pos.X);
        Assert.AreEqual(131f, pos.Y);
        Assert.AreEqual(1525f, pos.Z);
        Assert.AreEqual(Quaternion.Default.W, rot.W);
    }

    [TestMethod]
    public void TryResolveTransferSpawn_IgnoresNonContinentObjectEnterPoints()
    {
        var map = CreateDestMap(BackRangeId, entry: new Vector4(1f, 2f, 3f, 0f));
        // Repair-station style (type 5) must not steal continent-origin matches.
        var ep = new EnterPointTemplate
        {
            COID = 6044,
            MapTransferType = (byte)MapTransferType.RepairStation,
            MapTransferData = UpsideId,
            Location = new Vector4(999f, 999f, 999f, 0f),
            Rotation = Quaternion.Default,
        };
        map.MapData.Templates[6044] = ep;

        Assert.IsFalse(MapTransferSpawn.TryResolve(map, UpsideId, out var pos, out _));
        Assert.AreEqual(1f, pos.X);
    }

    [TestMethod]
    public void TransferCharacterToMap_FromUpside_SpawnsAtBackRangeUpsideGate()
    {
        var sourceContinent = new ContinentObject
        {
            Id = UpsideId,
            MapFileName = "sec_f_h_town_j2_upside_01",
            DisplayName = "Upside",
            IsTown = true,
            IsPersistent = true,
        };
        var sourceMap = SectorMap.CreateForTests(sourceContinent, new Vector4(178f, 33f, 385f, 0f));

        var destMap = CreateDestMap(BackRangeId, entry: new Vector4(1801f, 131f, 1525f, 0f));
        AddEnterPoint(destMap, coid: 8498, sourceContinentId: UpsideId,
            location: new Vector4(1240.8f, 61.9f, 2302.2f, 0f),
            rotation: new Quaternion(0f, 0.5f, 0f, 0.866f));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18325, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);
        character.SetCurrentVehicleForTests(new Vehicle());
        character.CurrentVehicle.SetCoid(18326, true);
        character.CurrentVehicle.AttachTestDataForTests();
        character.SetMap(sourceMap);
        character.CurrentVehicle.SetMap(sourceMap);
        character.Position = new Vector3(100f, 0f, 100f);

        var previousResolver = MapManager.Instance.ResolveMapForTests;
        var previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        try
        {
            MapManager.Instance.ResolveMapForTests = id =>
            {
                Assert.AreEqual(BackRangeId, id);
                return destMap;
            };
            MapManager.Instance.SuppressCreatePacketsForTests = true;

            Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, BackRangeId));
            Assert.AreEqual(1240.8f, character.Position.X, 0.01f,
                "Must spawn at Back Range enter point for origin Upside, not default EntryPoint");
            Assert.AreEqual(61.9f, character.Position.Y, 0.01f);
            Assert.AreEqual(2302.2f, character.Position.Z, 0.01f);
            Assert.AreEqual(0.5f, character.Rotation.Y, 0.01f);
            Assert.AreEqual(character.Position.X, character.CurrentVehicle.Position.X);
            Assert.AreEqual(character.Position.Z, character.CurrentVehicle.Position.Z);
        }
        finally
        {
            MapManager.Instance.ResolveMapForTests = previousResolver;
            MapManager.Instance.SuppressCreatePacketsForTests = previousSuppress;
        }
    }

    private static SectorMap CreateDestMap(int continentId, Vector4 entry)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_xfer_{continentId}",
            DisplayName = "dest",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, entry);
    }

    private static void AddEnterPoint(
        SectorMap map,
        long coid,
        int sourceContinentId,
        Vector4 location,
        Quaternion rotation)
    {
        var ep = new EnterPointTemplate
        {
            COID = (int)coid,
            MapTransferType = (byte)MapTransferType.ContinentObject,
            MapTransferData = sourceContinentId,
            Location = location,
            Rotation = rotation,
        };
        map.MapData.Templates[coid] = ep;
    }
}
