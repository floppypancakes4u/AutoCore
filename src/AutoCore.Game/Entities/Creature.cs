namespace AutoCore.Game.Entities;

using System.Linq;
using System.Text;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

public class Creature : SimpleObject
{
    public int EnhancementId { get; private set; } = -1;

    public Vector3 Velocity { get; private set; }
    public Vector3 AngularVelocity { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public long SpawnOwner { get; set; }
    public byte Level { get; set; } = 1;

    public Creature()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override Creature GetAsCreature() => this;
    public override Creature GetSuperCreature() => this;

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

        // Generate loot for this creature
        if (map != null)
        {
            var lootItems = LootManager.Instance.GenerateLoot(this);
            
            if (lootItems.Count > 0)
            {
                var random = new System.Random();
                foreach (var cbid in lootItems)
                {
                    // Equipment items (armor, weapons, etc.) require auto-loot since the client
                    // doesn't allow picking them up from the ground
                    if (LootManager.Instance.RequiresAutoLoot(cbid))
                    {
                        if (killerCharacter != null)
                        {
                            LootManager.Instance.AutoLootItem(cbid, killerCharacter);
                        }
                        else
                        {
                            // No killer found - spawn on ground anyway (won't be pickable but visible)
                            SpawnLootOnGround(cbid, random);
                        }
                    }
                    else
                    {
                        // Regular items (consumables, resources) can be picked up from ground
                        SpawnLootOnGround(cbid, random);
                    }
                }
            }

            // Remove creature from map (SetMap(null) handles calling LeaveMap internally)
            SetMap(null);

            // Broadcast destroy packet to all players in the map so they remove the creature client-side
            var destroyPacket = new DestroyObjectPacket(creatureObjectId);
            foreach (var character in map.Objects.Values.OfType<Character>().Where(c => c.OwningConnection != null))
            {
                character.OwningConnection.SendGamePacket(destroyPacket);
            }

            // Award XP to killer for NPC kill
            if (killerCharacter != null)
            {
                try
                {
                    var creatureLevel = GetLevel();
                    var killXP = AssetManager.Instance.GetCreatureKillXP(creatureLevel);
                    
                    if (killXP > 0)
                    {
                        CharacterStatManager.Instance.GrantKillXPAndHandleLevelUps(killerCharacter, killXP);
                    }
                }
                catch (Exception ex)
                {
                    // Never let XP awarding break death handling
                    Logger.WriteLog(LogType.Error, $"Failed to award XP on creature kill: {ex.Message}");
                }
            }

            // Notify killer that death animation packet is missing
            if (killerCharacter?.OwningConnection != null)
            {
                try
                {
                    var message = "Death animation packet is missing for creature death";
                    var msgLen = (short)(Encoding.UTF8.GetByteCount(message) + 1); // include null terminator
                    killerCharacter.OwningConnection.SendGamePacket(new BroadcastPacket
                    {
                        ChatType = ChatType.SystemMessage,
                        SenderCoid = (ulong)creatureObjectId.Coid,
                        IsGM = false,
                        Sender = "System",
                        MessageLength = msgLen,
                        Message = message
                    });
                }
                catch
                {
                    // Never let chat break death handling
                }
            }
        }
    }

    private void SpawnLootOnGround(int cbid, System.Random random)
    {
        // Calculate random offset: random angle, 1-2 units distance
        var angle = (float)(random.NextDouble() * 2.0 * System.Math.PI);
        var distance = 1.0f + (float)(random.NextDouble() * 1.0); // 1-2 units
        var offsetX = (float)(System.Math.Cos(angle) * distance);
        var offsetZ = (float)(System.Math.Sin(angle) * distance);
        
        var lootPosition = new Vector3(
            Position.X + offsetX,
            Position.Y,
            Position.Z + offsetZ
        );
        
        LootManager.Instance.SpawnLootItem(cbid, lootPosition, Rotation, Map);
    }

    public void HandleMovement(CreatureMovedPacket packet)
    {
        if (Ghost == null)
            return;

        if (packet.ObjectId != ObjectId)
            throw new Exception("WTF? Someone else moves me?");

        // Update position
        Position = packet.Location;
        Rotation = packet.Rotation;
        Velocity = packet.Velocity;
        AngularVelocity = packet.AngularVelocity;
        TargetPosition = packet.TargetPosition;

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
