namespace AutoCore.Game.Entities;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

public abstract class ClonedObjectBase
{
    public CloneBaseObject CloneBaseObject { get; private set; }
    public CloneBaseObjectType Type => CloneBaseObject?.Type ?? CloneBaseObjectType.Base;
    private int? _cbidOverride;
    public int CBID => _cbidOverride ?? CloneBaseObject?.CloneBaseSpecific.CloneBaseId ?? -1;

    /// <summary>Unit-test helper when clonebase assets are not loaded.</summary>
    internal void SetCbidForTests(int cbid) => _cbidOverride = cbid;
    
    public int Faction { get; set; }
    public GhostObject Ghost { get; protected set; }
    //public int LastServerUpdate { get; protected set; }
    //public int TimeOfDeath { get; protected set; }
    public TFID Murderer { get; protected set; }

    /// <summary>
    /// Sets the murderer (killer) of this object. Called before OnDeath for loot attribution.
    /// </summary>
    public void SetMurderer(TFID murderer)
    {
        // Copy — never share TFID references with live entities (ObjectId can be reassigned).
        Murderer = murderer == null
            ? new TFID()
            : new TFID(murderer.Coid, murderer.Global);
    }

    /// <summary>
    /// Sets the murderer from another object (e.g., the attacker's vehicle or character).
    /// </summary>
    public void SetMurderer(ClonedObjectBase murderer)
    {
        if (murderer?.ObjectId != null)
            Murderer = new TFID(murderer.ObjectId.Coid, murderer.ObjectId.Global);
    }

    /// <summary>
    /// Character COID of the player who first landed damage on this entity during its current
    /// life ("tagged" it), or null when no player has. Loot attribution requires a player to have
    /// taken part in the fight: without this, an NPC killing another NPC dropped a full loot pile
    /// that every player on the map then watched appear. The first tagger keeps the credit so a
    /// fight the player started still rewards them when an NPC lands the killing blow.
    /// Cleared on revive alongside <see cref="Murderer"/>.
    /// </summary>
    public TFID LootTaggedBy { get; private set; }

    /// <summary>
    /// Records the player who initiated combat against this entity. First tagger wins; calls with
    /// a non-player attacker (NPC vs NPC) are ignored so they never make the victim lootable.
    /// </summary>
    public void TagLootAttribution(ClonedObjectBase attacker)
    {
        if (LootTaggedBy != null || attacker == null)
            return;

        // Summons count: a player's pet fighting on their behalf is still player-initiated.
        var character = attacker.GetSuperCharacter(true);
        if (character?.ObjectId == null)
            return;

        LootTaggedBy = new TFID(character.ObjectId.Coid, character.ObjectId.Global);
    }

    /// <summary>Clears loot tagging (revive / respawn reuse of the same entity).</summary>
    public void ClearLootAttribution() => LootTaggedBy = null;

    /// <summary>
    /// The player credited with this entity's death for loot purposes: the killing blow when a
    /// player landed it, otherwise the player who initiated the fight (<see cref="LootTaggedBy"/>).
    /// Null means no player took part — the caller must not generate loot at all.
    /// </summary>
    public Character ResolveLootCreditCharacter()
        => ResolveCharacterByTfid(Murderer) ?? ResolveCharacterByTfid(LootTaggedBy);

    /// <summary>
    /// Resolves a TFID to its owning player. Looks on this entity's own map first (exact TFID, so
    /// it is immune to the global/local COID-space overlap that <c>GetObjectByCoid</c> warns
    /// about) and falls back to the global object table. Null when the id names no live player —
    /// an NPC attacker, or a player who has since logged out.
    /// </summary>
    private Character ResolveCharacterByTfid(TFID id)
    {
        if (id is not { Coid: > 0 })
            return null;

        var found = Map?.GetObjectByTfid(id) ?? ObjectManager.Instance.GetObject(id);
        return found?.GetSuperCharacter(false);
    }
    //public TFID LastMurderer { get; protected set; }
    //public float DamageByMurderer { get; protected set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public ClonedObjectBase Target { get; protected set; }
    public SectorMap Map { get; protected set; }
    public ClonedObjectBase Owner { get; protected set; }
    //public ushort StatusBitField { get; protected set; }
    public float Scale { get; set; }
    //public float TerrainOffset { get; protected set; }
    //public float GameMass { get; protected set; }
    public int Value { get; protected set; }
    public int CustomValue { get; protected set; }
    public DeathType DeathType { get; protected set; }
    //public float HPSkillScalar { get; protected set; }
    //public int HPSkillAdd { get; protected set; }
    //public short RequiredLevelPrefixOffset { get; protected set; }
    public short RequiredLevel { get; protected set; }
    public short RequiredCombat { get; protected set; }
    public short RequiredPerception { get; protected set; }
    public short RequiredTech { get; protected set; }
    public short RequiredTheory { get; protected set; }
    //public long CoidCustomizedBy { get; protected set; }
    //public bool MadeFromMemory { get; protected set; }
    //public string CustomizedName { get; protected set; }
    //public int DistanceDrawOverride { get; protected set; }
    //public float OverheadOffset { get; protected set; }
    //public int DamageState { get; protected set; }
    //public string MangledName { get; protected set; }
    public TFID ObjectId { get; protected set; }
    //public long CoidAssignedTo { get; protected set; }
    public byte Layer { get; set; }
    //public bool IsInKillQueue { get; protected set; }
    //public bool HasBeenDeleted { get; protected set; }
    //public bool TempItem { get; protected set; }
    //public bool Ghosted { get; protected set; }
    //public bool IsIdentified { get; protected set; }
    //public bool HasPhysics { get; protected set; }
    //public bool HasGraphics { get; protected set; }
    //public bool Dirty { get; protected set; }
    public bool IsCorpse { get; protected set; }
    //public bool IsActive { get; protected set; }
    public bool IsInvincible { get; protected set; }

    /// <summary>
    /// Runtime invincible flag (map reactions MakeInvincible / MakeNotInvincible).
    /// Client applies via 0x206C; server must match for combat authority.
    /// Clearing invincible also ensures the object can be ghosted for HP sync.
    /// </summary>
    public void SetInvincible(bool invincible)
    {
        IsInvincible = invincible;
        if (!invincible)
            OnBecameDamagable();
    }

    /// <summary>
    /// Called when an object transitions to a state where combat can damage it.
    /// Map props override to create/scope ghosts so HealthMask reaches clients.
    /// </summary>
    protected virtual void OnBecameDamagable()
    {
    }
    //public bool IsChampion { get; protected set; }
    //public bool CanRespawn { get; protected set; }
    //public bool Enabled { get; protected set; }
    //public bool FakeObject { get; protected set; }
    //public bool IsInfinite { get; protected set; }
    //public bool IsSoundInitialized { get; protected set; }
    //public bool StateDirty { get; protected set; }
    //public bool DistantDraw { get; protected set; }
    //public bool IsKit { get; protected set; }
    //public bool IsBound { get; protected set; }
    public ushort UsesLeft { get; protected set; }
    //public bool DrawSelectionArea { get; protected set; }
    //public bool StatusRendered { get; protected set; }
    //public bool DamageFXValid { get; protected set; }
    //public bool Stealthed { get; protected set; }


    public abstract int GetCurrentHP();
    public abstract int GetMaximumHP();
    public abstract int GetBareTeamFaction();

    public virtual int TakeDamage(int damage)
    {
        // Default implementation - subclasses should override
        return 0;
    }

    /// <summary>Restores hit points and returns the amount actually restored.</summary>
    public virtual int RestoreHealth(int amount) => 0;

    /// <summary>
    /// Applies damage and, when it actually lands from a known attacker, notifies the NPC combat
    /// brain so an idle NPC latches onto its attacker and may call for help (Stage 11). Delegates
    /// the HP math to the virtual <see cref="TakeDamage(int)"/> override.
    /// </summary>
    public int TakeDamage(int damage, ClonedObjectBase attacker)
    {
        var actual = TakeDamage(damage);
        if (actual > 0 && attacker != null)
        {
            // Loot attribution: landing real damage is what makes this a fight the attacker
            // started. Must precede OnDeath so a lethal hit is already tagged.
            TagLootAttribution(attacker);
            Npc.NpcCombatAi.OnDamaged(this, attacker);
        }

        // High-fidelity /reportbug: damage involving a player (as victim or attacker).
        if (actual > 0)
            Diagnostics.PlayerCombatTrace.OnDamage(this, attacker, actual);

        return actual;
    }

    public virtual Character GetAsCharacter() => null;
    public virtual Creature GetAsCreature() => null;
    public virtual Vehicle GetAsVehicle() => null;

    public virtual Creature GetSuperCreature()
    {
        return Owner?.GetSuperCreature();
    }

    public virtual Character GetSuperCharacter(bool includeSummons)
    {
        return Owner?.GetSuperCharacter(includeSummons);
    }

    public virtual bool GetIsCorpse()
    {
        return IsCorpse;
    }

    public ClonedObjectBase()
    {
        Faction = -1;
        Rotation = new(0.0f, 0.0f, 0.0f, 1.0f);
        Scale = 1.0f;
        CustomValue = -1;
        DeathType = DeathType.Silent;
        RequiredLevel = -1;
        ObjectId = new();
        //CoidAssignedTo = -1;
        Position = new(0.0f, 0.0f, 0.0f);
        Murderer = new();
    }

    /// <summary>Unit-test seam to attach a clonebase without AssetManager/wad.</summary>
    internal void AssignCloneBaseForTests(CloneBaseObject cloneBase) => CloneBaseObject = cloneBase;

    public void LoadCloneBase(int cbid)
    {
        CloneBaseObject = AssetManager.Instance.GetCloneBase<CloneBaseObject>(cbid);

        if (CloneBaseObject == null)
        {
            Logger.WriteLog(LogType.Error, $"LoadCloneBase: Failed to load CloneBase with CBID {cbid}. The CloneBase may not exist in the loaded game data.");
            throw new InvalidOperationException($"CloneBase with CBID {cbid} not found. Ensure the game data (clonebase.wad) is properly loaded and contains this CBID.");
        }

        Value = CloneBaseObject.CloneBaseSpecific.BaseValue;
        //GameMass = CloneBaseObject.SimpleObjectSpecific.Mass;
        IsInvincible = ((CloneBaseObject.SimpleObjectSpecific.Flags >> 12) & 1) != 0;
        RequiredLevel = CloneBaseObject.SimpleObjectSpecific.RequiredLevel;
        RequiredCombat = CloneBaseObject.SimpleObjectSpecific.RequiredCombat;
        RequiredPerception = CloneBaseObject.SimpleObjectSpecific.RequiredPerception;
        RequiredTech = CloneBaseObject.SimpleObjectSpecific.RequiredTech;
        RequiredTheory = CloneBaseObject.SimpleObjectSpecific.RequiredTheory;
        UsesLeft = CloneBaseObject.SimpleObjectSpecific.MaxUses;

        OnCloneBaseLoaded();
    }

    /// <summary>Hook for subclasses that need mutable runtime state from clonebase (e.g. HP).</summary>
    protected virtual void OnCloneBaseLoaded()
    {
    }

    public void SetCoid(long coid, bool global)
    {
        ObjectId.Coid = coid;
        ObjectId.Global = global;
    }

    public void SetOwner(ClonedObjectBase owner)
    {
        Owner = owner;
    }

    public void SetGhost(GhostObject ghost)
    {
        Ghost = ghost;
        Ghost?.SetParent(this);
        //LastServerUpdate = Environment.TickCount; // TODO: linux time or what?
    }

    public void ClearGhost()
    {
        if (Ghost != null && Ghost.IsGhostedToAny())
        {
            // Another connection still holds this exact ghost instance in active scope (e.g. a
            // second player still observing this vehicle as a foreign object). Drop our own
            // reference so a future CreateGhost() starts fresh, but leave Ghost.Parent intact so
            // that connection's PackUpdate keeps working instead of throwing. The other
            // connection's own scope query will naturally stop selecting this entity (it is
            // already off Map/Grid by the time ClearGhost runs) and detach it within 1-2 ticks.
            Logger.WriteLog(LogType.Network,
                $"ClearGhost: coid {ObjectId.Coid} still ghosted to another connection; deferring Parent clear");
            Ghost = null;
            return;
        }

        Ghost?.SetParent(null);
        Ghost = null;
        //LastServerUpdate = Environment.TickCount; // TODO: linux time or what?
    }

    public void SetMap(SectorMap map)
    {
        if (Map != map)
        {
            if (Map != null)
            {
                Map.LeaveMap(this);
            }

            Map = map;

            if (Map != null)
            {
                Map.EnterMap(this);
            }
        }
    }

    public void SetTargetObject(ClonedObjectBase target)
    {
        if (Target != target)
        {
            Ghost?.SetMaskBits(4);

            Target = target;
        }
    }

    public virtual void OnDeath(DeathType deathType)
    {
        //TimeOfDeath = Environment.TickCount; // TODO: linux time or what?
        Ghost?.SetMaskBits(GhostObject.HealthMask);

        IsCorpse = true;
        DeathType = deathType;

        // Credit kill objectives for the murderer's character (map props, NPCs, vehicles).
        try
        {
            Managers.MissionKillProgress.NotifyObjectKilled(this);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "NotifyObjectKilled failed for coid={0}: {1}",
                ObjectId.Coid,
                ex.Message);
        }

        try
        {
            Managers.MissionCollectProgress.NotifyObjectKilled(this);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "MissionCollectProgress.NotifyObjectKilled failed for coid={0}: {1}",
                ObjectId.Coid,
                ex.Message);
        }

        // Kill XP (docs/XP.md) — murderer full credit, grey/hard level diff.
        try
        {
            Experience.KillXpAward.TryAward(this);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "KillXpAward failed for coid={0}: {1}",
                ObjectId.Coid,
                ex.Message);
        }
    }

    /// <summary>
    /// Clears corpse state after INC respawn / revive. Subclasses restore HP.
    /// </summary>
    public virtual void Revive()
    {
        IsCorpse = false;
        DeathType = DeathType.Silent;
        Murderer = new TFID();
        Ghost?.SetMaskBits(GhostObject.HealthMask);
    }

    public int GetIDFaction()
    {
        var obj = this;

        for (var owner = obj.Owner; owner != null; owner = owner.Owner)
            obj = owner;

        return obj.Faction;
    }

    public virtual void CreateGhost()
    {
    }

    public virtual void WriteToPacket(CreateSimpleObjectPacket packet)
    {
    }

    public static ClonedObjectBase? AllocateNewObjectFromCBID(int cbid)
    {
        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        if (cloneBase is null)
            return null;

        switch (cloneBase.Type)
        {
            case CloneBaseObjectType.Armor:
                return new Armor();

            case CloneBaseObjectType.PowerPlant:
                return new PowerPlant();

            case CloneBaseObjectType.WheelSet:
                return new WheelSet();

            case CloneBaseObjectType.Weapon:
                return new Weapon();

            // Client CVOGReaction_GiveItemByCbid uses the same SimpleObject allocator
            // for Item (6) and Commodity (0x1a); other inventory-capable types that
            // are not specialized equipment also deserialize as SimpleObject.
            case CloneBaseObjectType.Item:
            case CloneBaseObjectType.Commodity:
            case CloneBaseObjectType.Gadget:
            case CloneBaseObjectType.TinkeringKit:
            case CloneBaseObjectType.Accessory:
            case CloneBaseObjectType.Ornament:
            case CloneBaseObjectType.RaceItem:
            case CloneBaseObjectType.Money:
            // Mission Inventory gear (deliver GiveItemOnStart / use-item objectives).
            case CloneBaseObjectType.QuestObject:
            case CloneBaseObjectType.MissionObject:
                return new SimpleObject(GraphicsObjectType.Graphics);

            default:
                Logger.WriteLog(LogType.Debug, $"Creating object of type {cloneBase.Type} is not yet supported! CBID: {cbid}");
                return null;
        }
    }

    public static int GetMoneyCBIDFromCredits(long credits)
    {
        if (credits >= 0x3B9ACA00) // Money Orb
            return 2827;

        if (credits >= 0xF4240) // Money Bars
            return 2825;

        if (credits >= 0x3E8) // Money Script
            return 2828;

        if (credits >= 1) // Money Clink
            return 2826;

        return -1;
    }
}
