namespace AutoCore.Game.Entities;

using System;
using System.Linq;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

public class Creature : SimpleObject
{
    public int EnhancementId { get; private set; } = -1;

    public Vector3 Velocity { get; private set; }
    public Vector3 AngularVelocity { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public long SpawnOwner { get; set; }
    public byte Level { get; set; } = 1;

    #region NPC AI fields (NPC.md)
    /// <summary>Ghost AI state byte sent under the creature StateMask (see HBAICombatState).</summary>
    public byte AiCombatState { get; set; }

    public long CoidCurrentPath { get; set; } = -1;
    public float PatrolDistance { get; set; }
    public bool PathReversing { get; set; }

    /// <summary>
    /// Server-side AI runtime state; must remain null for player-controlled <see cref="Character"/>
    /// instances — assign only when `this is not Character`.
    /// </summary>
    public NpcAiState NpcAi
    {
        get => _npcAi;
        set
        {
            if (value != null && this is Character)
                throw new InvalidOperationException("NpcAi must remain null for player-controlled Character instances.");

            _npcAi = value;
        }
    }
    private NpcAiState _npcAi;

    /// <summary>Whether interacting with this NPC can open the mission dialog.</summary>
    public bool IsMissionGiver { get; set; }
    #endregion

    public Creature()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override Creature GetAsCreature() => this;
    public override Creature GetSuperCreature() => this;

    /// <summary>
    /// Applies a server-authoritative pose/velocity/target update (NPC movement tick) and marks
    /// the ghost's PositionMask dirty. Safe to call before the ghost exists.
    /// </summary>
    public void ApplyServerMove(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 targetPosition)
    {
        Position = position;
        Rotation = rotation;
        Velocity = velocity;
        TargetPosition = targetPosition;

        Ghost?.SetMaskBits(GhostObject.PositionMask);
    }

    public virtual byte GetLevel() => Level;

    public void ScaleHealthForLevel(byte baseLevel)
    {
        // This is FAR from perfect or accurate, but it's based off extremely limited information that I have. - Floppy
        // Scale health based on level - linear scaling from level 1
        // BaseHP is the HP at level 1 (e.g., 37 HP for level 1 biomeck instructors)
        // Formula: BaseHP + (Level - 1) * HPPerLevel
        // Level 1: BaseHP (e.g., 37)
        // Level 6-8: ~79 HP, so HPPerLevel = (79 - 37) / (6-1) ≈ 8.4, use 8
        var baseHP = CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;
        var levelDiff = Level - 1; // Scale from level 1, not BaseLevel
        const int hpPerLevel = 8; // HP increase per level above level 1
        var scaledHP = baseHP + (levelDiff * hpPerLevel);
        HP = MaxHP = Math.Max(1, scaledHP);
    }

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        if (packet is CreateCharacterPacket)
            return;

        if (packet is CreateCreaturePacket creaturePacket)
        {
            creaturePacket.EnhancementId = EnhancementId;
            creaturePacket.Level = Level;
            creaturePacket.DoesntCountAsSummon = false;
            creaturePacket.IsElite = false;
            // CoidCurrentVehicle is set by the caller (foreign vehicle scope) when linking to a chassis.
        }
    }

    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

        Ghost = new GhostCreature();
        Ghost.SetParent(this);
    }

    public override void OnDeath(DeathType deathType)
    {
        base.OnDeath(deathType);

        // Save object ID and map reference before removal
        var creatureObjectId = ObjectId;
        var map = Map;

        // Try to find the killer character early (for loot and chat notification)
        Character killerCharacter = null;
        if (Murderer.Coid > 0)
        {
            var murdererObj = ObjectManager.Instance.GetObject(Murderer);
            killerCharacter = murdererObj?.GetSuperCharacter(false);
        }

        // SpawnPoint TriggerEvents before leave-map (same pattern as Vehicle).
        if (map != null && SpawnOwner > 0 && map.GetObjectByCoid(SpawnOwner) is SpawnPoint spawn)
        {
            ClonedObjectBase activator = killerCharacter?.CurrentVehicle != null
                ? killerCharacter.CurrentVehicle
                : killerCharacter != null
                    ? killerCharacter
                    : this;
            spawn.NotifySpawnedChildDied(this, activator);
        }

        // Multi-track death loot (gear / junk / consumable / credits / commodity).
        if (map != null)
        {
            if (CloneBaseObject is CloneBaseCreature creatureCloneBase)
            {
                var cs = creatureCloneBase.CreatureSpecific;
                LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
                {
                    Map = map,
                    Position = Position,
                    Rotation = Rotation,
                    Killer = killerCharacter,
                    VictimCbid = CBID,
                    Level = Level,
                    LootTableId = cs.LootTableId,
                    UseCreatureDropFormula = true,
                    CreatureBaseLootChance = cs.BaseLootChance,
                    GearRolls = 0,
                });
            }

            // Creature path: DestroyObject with DeathType only (CompletelyDestroyObject →
            // vtable+0x50 death FX). Do NOT send InitCreateObject DoDeath — RemoveObject
            // strips creatures without FX and then DestroyObject fails to resolve.
            BroadcastDeath(map, creatureObjectId, deathType, Murderer, Ghost, useInitCreateDeath: false);
            SetMap(null);
        }
    }

    public void HandleMovement(CreatureMovedPacket packet)
    {
        if (packet.ObjectId != ObjectId)
            throw new Exception("WTF? Someone else moves me?");

        // Always update pose (even if ghost is not ready) so town on-foot logout resume and
        // trigger volumes see the walked position. Matches Vehicle.HandleMovement — ghost is
        // only required for rebroadcast, not for authoritative server pose.
        Position = packet.Location;
        Rotation = packet.Rotation;
        Velocity = packet.Velocity;
        AngularVelocity = packet.AngularVelocity;
        TargetPosition = packet.TargetPosition;

        if (Ghost == null)
            return;

        Ghost.SetMaskBits(GhostObject.PositionMask);

        // Update target
        if (Target != null)
        {
            if (packet.Target.Coid == -1)
            {
                Target = null;

                Ghost.SetMaskBits(GhostObject.TargetMask);
            }
            else if (packet.Target != Target.ObjectId)
            {
                Target = ObjectManager.Instance.GetObject(packet.Target);

                Ghost.SetMaskBits(GhostObject.TargetMask);
            }
        }
        else if (packet.Target.Coid != -1)
        {
            Target = ObjectManager.Instance.GetObject(packet.Target);

            Ghost.SetMaskBits(GhostObject.TargetMask);
        }
    }
}
