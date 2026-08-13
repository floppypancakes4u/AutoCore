namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

/// <summary>
/// FAM-placed Creature (clone type 18). Binary layout matches
/// <see cref="GraphicsObjectTemplate"/>; <see cref="Create"/> materializes a
/// <see cref="Creature"/> with combat AI when <c>IsNPC == 0</c>.
/// </summary>
public sealed class CreatureTemplate : GraphicsObjectTemplate
{
    public CreatureTemplate()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override ClonedObjectBase Create()
    {
        var creature = new Creature();
        creature.SetCoid(COID, false);
        if (CBID > 0)
            creature.LoadCloneBase(CBID);
        creature.SetupCBFields();
        creature.Faction = Faction;
        creature.Scale = Scale;
        creature.Layer = Layer;
        creature.Position = Location.ToVector3();
        creature.Rotation = Rotation;

        if (creature.CloneBaseObject is CloneBaseCreature clone)
        {
            var spec = clone.CreatureSpecific;
            creature.Level = spec.BaseLevel > 0 ? (byte)spec.BaseLevel : (byte)1;
            creature.ScaleHealthForLevel(creature.Level);

            // Only authored turrets get server combat AI. Walking FAM wildlife already
            // exists on the client; giving them NpcAi made them emit SkillStatusEffect
            // and the client unpacked a GhostVehicle with a null +0x258 (AV 0x004F5566).
            if (spec.IsNPC == 0 && spec.AIBehavior > 0 && spec.HasTurret != 0)
            {
                creature.SetInvincible(false);
                var profile = AssetManager.Instance.GetCreatureAiProfile(spec.AIBehavior);
                if (profile != null)
                    creature.NpcAi = new NpcAiState
                    {
                        Profile = profile,
                        HomePosition = creature.Position,
                    };
            }
        }

        return creature;
    }
}
