using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.EntityTemplates;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;

[TestClass]
public class CreatureTemplateTests
{
    [TestCleanup]
    public void Cleanup()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManager.Instance.SetTestCreatureAiProfiles(null);
    }

    [TestMethod]
    public void AllocateTemplateFromCBID_Creature_ReturnsCreatureTemplate()
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(2978, aiBehaviorId: 21, isNpc: 0);
        var tpl = ObjectTemplate.AllocateTemplateFromCBID(2978);
        Assert.IsInstanceOfType(tpl, typeof(CreatureTemplate));
    }

    [TestMethod]
    public void Create_Turret_AttachesNpcAiAndIsHittable()
    {
        const int aiId = 21;
        AssetManagerTestHelper.RegisterCreatureCloneBase(2978, aiBehaviorId: aiId, baseLevel: 50, isNpc: 0, hasTurret: 1);
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = aiId }
        });

        var tpl = new CreatureTemplate
        {
            CBID = 2978,
            COID = 100,
            Faction = 0,
        };
        tpl.Location = new Vector4(1f, 2f, 3f, 0f);

        var obj = tpl.Create();
        Assert.IsInstanceOfType(obj, typeof(Creature));
        var creature = (Creature)obj;
        Assert.IsNotNull(creature.NpcAi);
        Assert.AreEqual(0, creature.Faction);
        Assert.IsFalse(creature.IsInvincible);
        Assert.AreEqual(50, creature.Level);
        Assert.AreEqual(1f, creature.Position.X, 0.01f);
    }

    /// <summary>
    /// Walking FAM wildlife (AIBehavior set, no turret) must not run server combat AI.
    /// Their skill packets made the client unpack a GhostVehicle with a null wheelset
    /// (AV 0x004F5566) on rustyironmine.
    /// </summary>
    [TestMethod]
    public void Create_FamWalkingCreature_DoesNotAttachCombatAi()
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(4100, aiBehaviorId: 21, isNpc: 0, hasTurret: 0);
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = 21 }
        });

        var creature = (Creature)new CreatureTemplate { CBID = 4100, COID = 200, Faction = 21 }.Create();
        Assert.IsNull(creature.NpcAi, "FAM wildlife must not tick/fire — that arms client Havok AV 0x004F5566");
    }

    /// <summary>
    /// FAM objects already exist in the client map. Never ghost them. Turrets still get AI.
    /// </summary>
    [TestMethod]
    public void InitializeLocalObjects_FamTurret_HasAiButNoGhost()
    {
        const int cbid = 2978;
        const int aiId = 21;
        const int coid = 0x50014CCE;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, aiBehaviorId: aiId, isNpc: 0, hasTurret: 1);
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = aiId }
        });

        var map = CreateMineMap();
        map.MapData.Templates[coid] = new CreatureTemplate
        {
            CBID = cbid,
            COID = coid,
            Faction = 0,
        };

        map.InitializeLocalObjectsForTests();

        var creature = map.GetObjectByCoid(coid) as Creature;
        Assert.IsNotNull(creature);
        Assert.IsNotNull(creature.NpcAi, "turret still runs combat AI");
        Assert.IsNull(creature.Ghost,
            "must not ghost FAM-local creatures (client already has the FAM object; AV 0x004F5566)");
    }

    [TestMethod]
    public void InitializeLocalObjects_FamWildlife_HasNoAiAndNoGhost()
    {
        const int cbid = 4100;
        const int coid = 0x50014CCF;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, aiBehaviorId: 21, isNpc: 0, hasTurret: 0);
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = 21 }
        });

        var map = CreateMineMap();
        map.MapData.Templates[coid] = new CreatureTemplate
        {
            CBID = cbid,
            COID = coid,
            Faction = 21,
        };

        map.InitializeLocalObjectsForTests();

        var creature = map.GetObjectByCoid(coid) as Creature;
        Assert.IsNotNull(creature);
        Assert.IsNull(creature.NpcAi);
        Assert.IsNull(creature.Ghost);
    }

    private static SectorMap CreateMineMap()
    {
        return SectorMap.CreateForTests(new ContinentObject
        {
            Id = 460,
            MapFileName = "tm_fam_creature",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        }, new Vector4(0, 0, 0, 0));
    }

    [TestMethod]
    public void Create_InteractiveNpc_HasNoCombatAi()
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(4001, aiBehaviorId: 21, isNpc: 1);
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = 21 }
        });

        var tpl = new CreatureTemplate { CBID = 4001, COID = 101, Faction = 0 };
        var creature = (Creature)tpl.Create();
        Assert.IsNull(creature.NpcAi);
    }
}
