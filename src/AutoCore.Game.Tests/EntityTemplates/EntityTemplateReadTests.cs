using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.EntityTemplates;

/// <summary>
/// Binary Read/Create coverage for fam-backed templates (pure memory streams).
/// </summary>
[TestClass]
public class EntityTemplateReadTests
{
    [TestMethod]
    public void SpawnPointTemplate_Read_MapVersion32_AllFieldsAndFactionDirty()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // TriggerEvents (3 x int64)
            writer.Write(1L);
            writer.Write(2L);
            writer.Write(3L);

            // Location Vector4 + Rotation Quaternion
            writer.Write(1f); writer.Write(2f); writer.Write(3f); writer.Write(4f);
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);

            writer.Write(12.5f); // Radius
            writer.Write(30f); // RespawnTime
            writer.Write(40f); // ActivationRange
            writer.Write(true); // UseGenerator
            writer.Write(true); // HasChampion
            writer.Write((byte)25); // ChampionChance
            writer.Write((byte)80); // SpawnChance
            writer.Write(true); // IsActive
            writer.Write(true); // RandomlyOffsetSpawnPosition (v>=31)

            // 12 spawn list slots (v>=29)
            for (var i = 0; i < 12; i++)
            {
                writer.Write((byte)1); // Lower
                writer.Write((byte)3); // Upper
                writer.Write((short)0); // pad
                writer.Write(i == 0 ? 500 : -1); // SpawnType
                writer.Write(unchecked((byte)-1)); // LevelOffset — signed on the wire (SS-40)
                writer.Write(i == 0); // IsTemplate
                writer.Write((short)0); // pad
            }

            writer.Write(9); // Loot
            writer.Write(0.75f); // LootPercent
            writer.Write(700L); // MapPathCoid
            writer.Write(15f); // InitialPatrolDistance
            writer.Write(true); // FactionDirty (v>=15)
            writer.Write(42); // OriginalFaction
            writer.Write(0.33f); // LootChance (v>=24)
            writer.WriteLengthedString("ChampName"); // v>=32
        }

        ms.Position = 0;
        var template = new SpawnPointTemplate { COID = 100, CBID = 1 };
        template.Read(new BinaryReader(ms), mapVersion: 32);

        Assert.AreEqual(12.5f, template.Radius);
        Assert.AreEqual(30f, template.RespawnTime);
        Assert.AreEqual(40f, template.ActivationRange);
        Assert.IsTrue(template.UseGenerator);
        Assert.IsTrue(template.HasChampion);
        Assert.AreEqual((byte)25, template.ChampionChance);
        Assert.AreEqual((byte)80, template.SpawnChance);
        Assert.IsTrue(template.IsActive);
        Assert.IsTrue(template.OriginalIsActive);
        Assert.IsTrue(template.RandomlyOffsetSpawnPosition);
        Assert.AreEqual(12, template.Spawns.Count);
        Assert.AreEqual(500, template.Spawns[0].SpawnType);
        Assert.IsTrue(template.Spawns[0].IsTemplate);
        Assert.AreEqual((byte)1, template.Spawns[0].LowerNumberOfSpawns);
        Assert.AreEqual((byte)3, template.Spawns[0].UpperNumberOfSpawns);
        Assert.AreEqual((sbyte)-1, template.Spawns[0].LevelOffset,
            "SS-40: fam LevelOffset is signed — 0xFF is retail -1, not +255 (level-255 NPCs)");
        Assert.AreEqual(9, template.Loot);
        Assert.AreEqual(0.75f, template.LootPercent);
        Assert.AreEqual(700L, template.MapPathCoid);
        Assert.AreEqual(15f, template.InitialPatrolDistance);
        Assert.IsTrue(template.FactionDirty);
        Assert.AreEqual(42, template.OriginalFaction);
        Assert.AreEqual(42, template.Faction, "FactionDirty promotes OriginalFaction");
        Assert.AreEqual(0.33f, template.LootChance);
        Assert.AreEqual("ChampName", template.MaybeChampionName);
    }

    [TestMethod]
    public void SpawnPointTemplate_GetSpawn_EmptyAndSingleAndCreate()
    {
        var empty = new SpawnPointTemplate();
        empty.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = -1 });
        Assert.IsNull(empty.GetSpawn());

        var single = new SpawnPointTemplate { Layer = 3, Faction = 7 };
        single.Location = new Vector4(1, 2, 3, 0);
        single.Rotation = Quaternion.Default;
        single.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = 99, IsTemplate = false });
        var picked = single.GetSpawn();
        Assert.IsNotNull(picked);
        Assert.AreEqual(99, picked!.SpawnType);

        // multi-slot path exercises random pick without asserting which entry
        single.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = 100 });
        Assert.IsNotNull(single.GetSpawn());

        var created = single.Create() as SpawnPoint;
        Assert.IsNotNull(created);
        Assert.AreEqual(3, created!.Layer);
        Assert.AreEqual(7, created.Faction);
        Assert.AreEqual(1f, created.Position.X);
    }

    [TestMethod]
    public void SpawnPointTemplate_ApplyFactionDirty_NoOpWhenClean()
    {
        var tpl = new SpawnPointTemplate { Faction = 1, OriginalFaction = 9, FactionDirty = false };
        tpl.ApplyFactionDirtyAuthoredFaction();
        Assert.AreEqual(1, tpl.Faction);
    }

    [TestMethod]
    public void ReactionTemplate_Read_ActivateWithObjectsAndConditions()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUtf8StringOn("ActRx", 65);
            writer.Write((byte)ReactionType.Activate);
            writer.Write(false); // ActOnActivator
            writer.Write(11); // ObjectiveIDCheck
            writer.Write(false); // DoForConvoy
            writer.Write(5); // GenericVar1
            writer.Write(1.5f); // GenericVar2
            writer.Write(6); // GenericVar3

            writer.Write(2); // object count
            writer.Write(100); // object coid (int32 via ReadCOIDFromFile)
            writer.Write(200);

            writer.Write(1); // reaction count
            writer.Write(300);

            // mapVersion >= 8
            writer.Write(true); // AllConditionsNeeded
            writer.Write(1); // condition count
            writer.Write(1); // LeftId
            writer.Write(2); // RightId
            writer.Write((byte)0); // Type
            writer.Write(new byte[3]); // pad
            writer.Write(true); // DoForAllPlayers
        }

        ms.Position = 0;
        var template = new ReactionTemplate();
        template.Read(new BinaryReader(ms), mapVersion: 15);

        Assert.AreEqual("ActRx", template.Name);
        Assert.AreEqual(ReactionType.Activate, template.ReactionType);
        Assert.AreEqual(11, template.ObjectiveIDCheck);
        Assert.AreEqual(5, template.GenericVar1);
        Assert.AreEqual(1.5f, template.GenericVar2);
        Assert.AreEqual(6, template.GenericVar3);
        CollectionAssert.AreEqual(new long[] { 100, 200 }, template.Objects);
        CollectionAssert.AreEqual(new long[] { 300 }, template.Reactions);
        Assert.IsTrue(template.AllConditionsNeeded);
        Assert.IsTrue(template.DoForAllPlayers);
        Assert.AreEqual(1, template.Conditions.Count);
        Assert.AreEqual(1, template.Conditions[0].LeftId);

        Assert.IsInstanceOfType(template.Create(), typeof(Reaction));
    }

    [TestMethod]
    public void ReactionTemplate_Read_TransferMap_AndText_AndWaypoint_AndDialog()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUtf8StringOn("Xfer", 65);
            writer.Write((byte)ReactionType.TransferMap);
            writer.Write(true);
            writer.Write(-1);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0f);
            writer.Write(0);
            writer.Write((byte)MapTransferType.ContinentObject); // MapTransfer
            writer.Write(55); // MapTransferData
            writer.Write(0); // reactions
            // v>=8
            writer.Write(false);
            writer.Write(0); // conditions
            writer.Write(false);
        }

        ms.Position = 0;
        var xfer = new ReactionTemplate();
        xfer.Read(new BinaryReader(ms), mapVersion: 12);
        Assert.AreEqual(ReactionType.TransferMap, xfer.ReactionType);
        Assert.AreEqual(MapTransferType.ContinentObject, xfer.MapTransfer);
        Assert.AreEqual(55, xfer.MapTransferData);

        // Text reaction with nested ReactionText
        ms.SetLength(0);
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUtf8StringOn("Txt", 65);
            writer.Write((byte)ReactionType.Text);
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0f);
            writer.Write(0);
            writer.Write(0); // objects
            writer.Write(0); // reactions
            writer.Write(true); // has text
            writer.Write((byte)ReactionTextType.OKDialog);
            writer.Write((byte)ReactionTextTargetType.Client);
            writer.WriteLengthedString("Hello");
            writer.Write(1); // param count
            writer.Write((byte)ReactionTextParamType.PlayerName);
            writer.Write(new byte[3]);
            writer.Write(0); // id
            writer.Write(1.0f); // cached (v>=14)
            writer.Write(1); // choice count
            writer.Write(900L); // trigger coid
            writer.WriteLengthedString("Yes");
            writer.Write(0); // choice params
            // v>=8
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
        }

        ms.Position = 0;
        var textTpl = new ReactionTemplate();
        textTpl.Read(new BinaryReader(ms), mapVersion: 14);
        Assert.IsNotNull(textTpl.Text);
        Assert.AreEqual(ReactionTextType.OKDialog, textTpl.Text.Type);
        Assert.AreEqual("Hello", textTpl.Text.Main);
        Assert.AreEqual(1, textTpl.Text.Params.Count);
        Assert.AreEqual(1.0f, textTpl.Text.Params[0].CachedValue);
        Assert.AreEqual(1, textTpl.Text.Choices.Count);
        Assert.AreEqual(900L, textTpl.Text.Choices[0].TriggerCoid);

        // Waypoint fields (AddWaypoint, v>=10)
        ms.SetLength(0);
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUtf8StringOn("Wp", 65);
            writer.Write((byte)ReactionType.AddWaypoint);
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0f);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            writer.Write((int)ReactionWaypointType.Kill);
            writer.WriteLengthedString("Kill bandits");
        }

        ms.Position = 0;
        var wp = new ReactionTemplate();
        wp.Read(new BinaryReader(ms), mapVersion: 12);
        Assert.AreEqual(ReactionWaypointType.Kill, wp.WaypointType);
        Assert.AreEqual("Kill bandits", wp.WaypointText);

        // MiscText for PlayMusic (v>=9)
        ms.SetLength(0);
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUtf8StringOn("Mus", 65);
            writer.Write((byte)ReactionType.PlayMusic);
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0f);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            writer.WriteLengthedString("theme_a");
        }

        ms.Position = 0;
        var mus = new ReactionTemplate();
        mus.Read(new BinaryReader(ms), mapVersion: 12);
        Assert.AreEqual("theme_a", mus.MiscText);

        // GiveMissionDialog missions (mapVersion > 16)
        ms.SetLength(0);
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUtf8StringOn("Dlg", 65);
            writer.Write((byte)ReactionType.GiveMissionDialog);
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0f);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            writer.Write(1); // mission type count
            writer.Write(4);
            writer.Write(2); // mission count
            writer.Write(100);
            writer.Write(101);
        }

        ms.Position = 0;
        var dlg = new ReactionTemplate();
        dlg.Read(new BinaryReader(ms), mapVersion: 20);
        CollectionAssert.AreEqual(new[] { 4 }, dlg.MissionTypes);
        CollectionAssert.AreEqual(new[] { 100, 101 }, dlg.Missions);
    }

    [TestMethod]
    public void ReactionTemplate_Read_MapVersion16_GiveMissionDialogLegacySkip()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUtf8StringOn("Leg", 65);
            writer.Write((byte)ReactionType.Activate); // any type; mapVersion==16 always reads mission block
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0f);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(false);
            // mapVersion == 16 mission block
            writer.Write(0); // mission types
            writer.Write(0); // missions
            // legacy skip (mapVersion < 20)
            writer.Write(2); // count
            writer.Write(1);
            writer.Write(2);
            writer.Write(0); // +4 padding after count*4
            writer.Write(3); // size
            writer.Write(new byte[3]);
        }

        ms.Position = 0;
        var tpl = new ReactionTemplate();
        tpl.Read(new BinaryReader(ms), mapVersion: 16);
        Assert.AreEqual(0, tpl.Missions.Count);
        Assert.AreEqual(ms.Length, ms.Position, "legacy block fully consumed");
    }

    [TestMethod]
    public void EnterPointTemplate_Read_WithFaction()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1f); writer.Write(2f); writer.Write(3f); writer.Write(0f);
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);
            writer.Write((byte)2);
            writer.Write(77);
            writer.Write(9); // Faction v>=7
        }

        ms.Position = 0;
        var tpl = new EnterPointTemplate();
        tpl.Read(new BinaryReader(ms), mapVersion: 10);
        Assert.AreEqual((byte)2, tpl.MapTransferType);
        Assert.AreEqual(77, tpl.MapTransferData);
        Assert.AreEqual(9, tpl.Faction);
    }

    [TestMethod]
    public void MapPathTemplate_Read_Points()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(50); // StaticDefaultPathCBID
            writer.Write(true); // Reverse
            writer.WriteUtf8StringOn("patrol", 64);
            writer.Write(1); // point count
            writer.Write(10f); writer.Write(0f); writer.Write(20f); // Position
            writer.Write(2.5f); // AcceptDistance
            writer.Write(123L); // ReactionCoid
            writer.Write(500); // WaitTime
            writer.Write(0); // pad 4
        }

        ms.Position = 0;
        var tpl = new MapPathTemplate();
        tpl.Read(new BinaryReader(ms), mapVersion: 1);
        Assert.AreEqual(50, tpl.StaticDefaultPathCBID);
        Assert.IsTrue(tpl.ReverseDirection);
        Assert.AreEqual("patrol", tpl.PathName);
        Assert.AreEqual(1, tpl.Points.Count);
        Assert.AreEqual(10f, tpl.Points[0].Position.X);
        Assert.AreEqual(2.5f, tpl.Points[0].AcceptDistance);
        Assert.AreEqual(123L, tpl.Points[0].ReactionCoid);
        Assert.AreEqual(500, tpl.Points[0].WaitTime);
    }

    [TestMethod]
    public void StoreTemplate_Read_MapVersion61()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(0f);
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);
            // 30 items when mapVersion > 50
            for (var i = 0; i < 30; i++)
            {
                writer.Write((byte)1);
                writer.Write(0.5f);
                writer.Write(100 + i);
                writer.Write(i == 0);
                writer.Write(2000 + i);
            }

            writer.WriteLengthedString("Vendor");
            writer.Write(1); // MinLevel
            writer.Write(20); // MaxLevel
            writer.Write(true); // IsJunkyard
            writer.Write(true); // IsVehicleStore
            writer.Write(true); // IsSouvenirStore
        }

        ms.Position = 0;
        var tpl = new StoreTemplate();
        tpl.Read(new BinaryReader(ms), mapVersion: 61);
        Assert.AreEqual(30, tpl.Items.Count);
        Assert.AreEqual(2000, tpl.Items[0].CBID);
        Assert.IsTrue(tpl.Items[0].Unlimited);
        Assert.AreEqual("Vendor", tpl.Name);
        Assert.AreEqual(1, tpl.MinLevel);
        Assert.AreEqual(20, tpl.MaxLevel);
        Assert.IsTrue(tpl.IsJunkyard);
        Assert.IsTrue(tpl.IsVehicleStore);
        Assert.IsTrue(tpl.IsSouvenirStore);
    }

    [TestMethod]
    public void TriggerTemplate_Read_MapVersion60_AndCreate()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1f); writer.Write(2f); writer.Write(3f); writer.Write(0f);
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);
            writer.Write(2.0f); // Scale
            writer.WriteUtf8StringOn("TrigA", 64);
            writer.Write(1.5f); // RetriggerDelay
            writer.Write(0.5f); // ActivateDelay
            writer.Write(3); // ActivationCount
            writer.Write((byte)TriggerTargetType.Players);
            writer.Write(true); // DoCollision
            writer.Write(true); // DoConditionals
            writer.Write(true); // ShowMapTransitionDecals v>=44
            writer.Write(true); // DoOnActivate
            writer.Write(true); // AllConditionsNeeded
            writer.Write(true); // ApplyToAllColliders v>=60

            writer.Write(1); // reaction count
            writer.Write(111);
            writer.Write(1); // target count
            writer.Write(true); // Global
            writer.Write(222); // Coid
            writer.Write(1); // condition count
            writer.Write(1);
            writer.Write(2);
            writer.Write((byte)1);
            writer.Write(new byte[3]);
            writer.Write(0x11223344u); // Color v>=9
            writer.Write(99u); // TriggerId v>=55
        }

        ms.Position = 0;
        var tpl = new TriggerTemplate { COID = 5 };
        tpl.Read(new BinaryReader(ms), mapVersion: 60);

        Assert.AreEqual("TrigA", tpl.Name);
        Assert.AreEqual(1.5f, tpl.RetriggerDelay);
        Assert.AreEqual(0.5f, tpl.ActivateDelay);
        Assert.AreEqual(3, tpl.ActivationCount);
        Assert.AreEqual(TriggerTargetType.Players, tpl.TargetType);
        Assert.IsTrue(tpl.DoCollision);
        Assert.IsTrue(tpl.ShowMapTransitionDecals);
        Assert.IsTrue(tpl.ApplyToAllColliders);
        Assert.AreEqual(1, tpl.Reactions.Count);
        Assert.AreEqual(111L, tpl.Reactions[0]);
        Assert.AreEqual(1, tpl.TargetList.Count);
        Assert.AreEqual(222, tpl.TargetList[0].Coid);
        Assert.IsTrue(tpl.TargetList[0].Global);
        Assert.AreEqual(0x11223344u, tpl.Color);
        Assert.AreEqual(99u, tpl.TriggerId);

        var created = tpl.Create() as Trigger;
        Assert.IsNotNull(created);
        Assert.AreEqual(1f, created!.Position.X);
        Assert.AreEqual(2.0f, created.Scale);
    }

    [TestMethod]
    public void GraphicsObjectTemplate_Read_MapVersion62_AndCreateWithoutCbid()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteLengthedString("fx_extra"); // v>=21
            writer.Write(true); // DistantDraw v>=48
            writer.Write(100); // DistanceDrawOverride v>=62
            writer.WriteLengthedString("tip"); // tooltip v>=22
            writer.Write(1L); writer.Write(2L); writer.Write(3L); // triggers
            writer.Write(5f); writer.Write(6f); writer.Write(7f); writer.Write(0f);
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);
            writer.Write(1.5f); // Scale
            writer.Write(0.25f); // TerrainOffset
            writer.Write(true); // IsActive
        }

        ms.Position = 0;
        var tpl = new GraphicsObjectTemplate(GraphicsObjectType.Graphics)
        {
            COID = 9,
            CBID = 0,
            Faction = 3,
        };
        tpl.Read(new BinaryReader(ms), mapVersion: 62);

        Assert.AreEqual("fx_extra", tpl.FxCreateExtraName);
        Assert.IsTrue(tpl.DistantDraw);
        Assert.AreEqual(100, tpl.DistanceDrawOverride);
        Assert.AreEqual("tip", tpl.ToolTip);
        Assert.AreEqual(1.5f, tpl.Scale);
        Assert.AreEqual(0.25f, tpl.TerrainOffset);
        Assert.IsTrue(tpl.IsActive);

        var obj = tpl.Create() as GraphicsObject;
        Assert.IsNotNull(obj);
        Assert.AreEqual(3, obj!.Faction);
        Assert.AreEqual(1.5f, obj.Scale);
        Assert.AreEqual(5f, obj.Position.X);
    }

    [TestMethod]
    public void ObjectTemplate_Defaults_AndAllocateUnknownReturnsNullWithoutCloneBase()
    {
        var ot = new ObjectTemplate();
        ot.Read(new BinaryReader(new MemoryStream()), 1);
        Assert.IsNull(ot.Create());
        Assert.IsNull(ObjectTemplate.AllocateTemplateFromCBID(-1));
        Assert.IsNull(ObjectTemplate.AllocateTemplateFromCBID(99999999));
    }
}
