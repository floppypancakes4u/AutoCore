using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

using AutoCore.Game.Map;

public class GhostCreature : GhostObject
{
    private static NetClassRepInstance<GhostCreature> _dynClassRep;

    public const ulong StateMask = 0x80000000ul;

    /// <summary>
    /// Diagnostics: non-initial <see cref="GhostObject.PositionMask"/> packs since the last sector
    /// diag sample, mirroring <see cref="GhostVehicle.PosePacksSinceDiag"/>. Read together with the
    /// moving-scoped-creature count it answers the question a live session otherwise cannot: is a
    /// creature's pose gap caused by priority starvation, or simply by more creatures competing for
    /// pose slots than the packet budget holds?
    /// </summary>
    public static int PosePacksSinceDiag;

    /// <summary>
    /// Diagnostics: <b>initial</b> creature ghost updates since the last sector diag sample.
    /// <para>
    /// An initial update carries the whole object (clonebase, health, faction, level, skills), not a
    /// 54-byte pose delta, and TNL packs it from the same fixed packet budget. A steady-state stream
    /// of these means creatures are repeatedly leaving and re-entering interest scope — every
    /// re-entry costs a full re-create — and pose deltas are being crowded out by them. That is a
    /// different bug from pose starvation and is not fixable by pose priority: measured against
    /// <see cref="PosePacksSinceDiag"/> it distinguishes the two.
    /// </para>
    /// </summary>
    public static int InitialPacksSinceDiag;

    /// <summary>
    /// When true (default), a <b>moving</b> creature competes for pose slots near vehicle level
    /// instead of sitting at the generic-prop weight. Flip off to A/B against legacy behaviour.
    /// </summary>
    public static bool EnableCreatureMovingPriority = true;

    /// <summary>
    /// Type weight for a moving creature. Deliberately below the player weight (0.5) so players are
    /// never starved by NPCs, but close enough to the moving-vehicle weight (0.5) that creatures are
    /// not simply whatever the vehicle pose stream leaves behind.
    /// <para>
    /// Measured before this existed (dense town, one client): vehicles ~8.3 Hz per entity, creatures
    /// ~1.7 Hz with worst samples at 0.68 Hz — ~1.5 s between poses. Creature packs collapsed exactly
    /// when vehicle packs spiked, i.e. creatures were the residual. At 0.15 a creature needed ~6 more
    /// skips than a vehicle just to tie, which is precisely that 5x rate gap.
    /// </para>
    /// </summary>
    internal const float MovingCreaturePosePriorityWeight = 0.40f;

    /// <summary>Type weight for a mission giver that is not moving (unchanged legacy value).</summary>
    internal const float MissionGiverPosePriorityWeight = 0.30f;

    /// <summary>Type weight for an idle creature (unchanged legacy value).</summary>
    internal const float IdleCreaturePosePriorityWeight = 0.15f;

    public override float GetUpdatePriority(NetObject scopeObject, ulong updateMask, int updateSkips)
    {
        // Self / viewer-target pins are policy-independent — defer to the base rules.
        if (ReferenceEquals(this, scopeObject))
            return 1.0f;

        var viewer = GetViewerParent(scopeObject);
        if (Parent != null && viewer != null && ReferenceEquals(viewer.Target, Parent))
            return 1.0f;

        if (!EnableCreatureMovingPriority || Parent == null || viewer == null)
            return base.GetUpdatePriority(scopeObject, updateMask, updateSkips);

        var creature = Parent.GetAsCreature();
        if (creature == null)
            return base.GetUpdatePriority(scopeObject, updateMask, updateSkips);

        var v = creature.Velocity;
        var moving = ((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z)) > 1e-6f;

        var weight = moving
            ? MovingCreaturePosePriorityWeight
            : (creature.IsMissionGiver ? MissionGiverPosePriorityWeight : IdleCreaturePosePriorityWeight);

        var dx = viewer.Position.X - Parent.Position.X;
        var dz = viewer.Position.Z - Parent.Position.Z;
        var distance = (float)Math.Sqrt((dx * dx) + (dz * dz));
        var falloff = Math.Clamp(1.0f - (distance / InterestSelector.BaseScopeDropRadius), 0.0f, 1.0f);
        return (weight * falloff) + (updateSkips * SkipStarvationBoost);
    }

    public new static void RegisterNetClassReps()
    {
        ImplementNetObject(out _dynClassRep);
    }

    public override NetClassRep GetClassRep()
    {
        return _dynClassRep;
    }

    public GhostCreature()
    {
        UpdatePriorityScalar = 1.0f;
    }

    public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
    {
        if (Parent == null)
            throw new Exception("Missing parent for GhostCreature!");

        var creature = Parent.GetAsCreature();

        if (PIsInitialUpdate)
        {
            System.Threading.Interlocked.Increment(ref InitialPacksSinceDiag);

            PackCommon(stream);

            if (stream.WriteFlag(creature.EnhancementId != -1)) // EnhancementId != -1
                stream.WriteInt((uint)creature.EnhancementId, 20); // EnhancementId

            if (stream.WriteFlag(creature.OnUseTriggerCoid != -1))
                stream.WriteInt((uint)creature.OnUseTriggerCoid, 20);

            if (stream.WriteFlag(creature.OnUseReactionCoid != -1))
                stream.WriteInt((uint)creature.OnUseReactionCoid, 20);

            if (stream.WriteFlag(false)) // CreatureSummoner TFID != (-1, false)
            {
                stream.WriteInt(0, 32); // CreatureSummoner TFID Coid
                stream.WriteInt(0, 32);

                stream.WriteFlag(false); // CreatureSummoner TFID Global
            }

            if (stream.WriteFlag(creature.SpawnOwner != -1))
                stream.Write(creature.SpawnOwner);

            stream.WriteFlag(false); // DoesntCountAsSummon
            stream.WriteBits(8, new byte[] { creature.Level }); // Level
            stream.WriteFlag(false); // IsElite

            PackSkills(stream, creature);
        }

        if (stream.WriteFlag((updateMask & MurdererMask) != 0))
        {
            stream.WriteInt(0, 32); // CoidMurderer
            stream.WriteInt(0, 32);
        }

        if (stream.WriteFlag((updateMask & HealthMask) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetCurrentHP(), 0), 18);
            stream.WriteFlag(Parent.GetIsCorpse());
        }

        if (stream.WriteFlag((updateMask & HealthMaxMask) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetMaximumHP(), 0), 18);
        }

        if (stream.WriteFlag((updateMask & StateMask) != 0))
        {
            stream.WriteBits(8, new byte[] { creature.AiCombatState }); // AI State
        }

        if (stream.WriteFlag((updateMask & PositionMask) != 0))
        {
            if (!PIsInitialUpdate)
            {
                System.Threading.Interlocked.Increment(ref PosePacksSinceDiag);
                // Sampled here, at serialisation, so it measures exactly what the client receives.
                // Keyed on this ghost instance, not the COID: local COIDs repeat across map
                // instances, and aliasing two creatures onto one track fabricates huge reversals.
                Diagnostics.CreatureMotionDiag.RecordPose(this, creature.Position, creature.ObjectId.Coid);
            }

            stream.Write(creature.Position.X);
            stream.Write(creature.Position.Y);
            stream.Write(creature.Position.Z);

            stream.Write(creature.Rotation.X);
            stream.Write(creature.Rotation.Y);
            stream.Write(creature.Rotation.Z);
            stream.Write(creature.Rotation.W);

            stream.Write(creature.Velocity.X);
            stream.Write(creature.Velocity.Y);
            stream.Write(creature.Velocity.Z);
            
            stream.Write(creature.TargetPosition.X);
            stream.Write(creature.TargetPosition.Y);
            stream.Write(creature.TargetPosition.Z);
        }

        if (stream.WriteFlag((updateMask & TargetMask) != 0))
        {
            if (Parent.Target != null)
            {
                stream.Write(Parent.Target.ObjectId.Coid);
                stream.WriteFlag(Parent.Target.ObjectId.Global);
            }
            else
            {
                // Retail cfidEmpty.coid == -1 (GhostCreature::packUpdate @ 0x005d2800).
                stream.Write((long)-1);
                stream.WriteFlag(false);
            }
        }

        return 0UL;
    }
}
