namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

public class SimpleObject : GraphicsObject
{
    #region Properties
    #region Database SimpleObject data
    private SimpleObjectData DBData { get; set; }
    #endregion

    protected int[] Prefixes { get; set; }
    protected int[] Gadgets { get; set; }
    protected short MaxGadgets { get; set; }
    protected int TeamFaction { get; set; }
    protected int Quantity { get; set; }
    // HP / MaxHP inherited from GraphicsObject (mutable combat fields).

    /// <inheritdoc />
    /// Inventory / vehicle / creature entities do not use the map-prop destroy-on-death path.
    protected override bool RemoveFromMapOnDeath => false;

    /// <inheritdoc />
    /// Vehicles/creatures manage their own ghost lifecycle (not map-prop combat ghosting).
    protected override bool GhostWhenDamagable => false;
    protected int ItemTemplateId { get; set; }
    protected byte InventoryPositionX { get; set; }
    protected byte InventoryPositionY { get; set; }
    protected byte SkillLevel1 { get; set; }
    protected byte SkillLevel2 { get; set; }
    protected byte SkillLevel3 { get; set; }
    protected bool AlreadyAssembled { get; set; }

    /// <summary>
    /// When true, ground CreateSimpleObject and inventory claim treat this as mission gear
    /// (PossibleMissionItem / IsMissionItem). Set for Collect kill-to-loot drops.
    /// </summary>
    public bool PossibleMissionItem { get; set; }
    #endregion

    public override int GetCurrentHP() => HP;
    public override int GetMaximumHP() => MaxHP;
    public override int GetBareTeamFaction() => TeamFaction;

    public override int TakeDamage(int damage)
    {
        // damage <= 0 guard matches GraphicsObject/Vehicle — negative damage must not heal.
        if (IsInvincible || IsCorpse || damage <= 0)
            return 0;

        var actualDamage = Math.Min(damage, HP);
        HP = Math.Max(0, HP - actualDamage);

        DirtyHealthMasks(GhostObject.HealthMask | GhostObject.HealthMaxMask);
        NotifyOwnerHealthHud();

        return actualDamage;
    }

    public override void Revive()
    {
        HP = Math.Max(1, MaxHP);
        base.Revive();
    }

    /// <summary>
    /// Ghost wire packs HP as 18-bit unsigned ints; keep server values within that range.
    /// </summary>
    public const int MaxWireHP = (1 << 18) - 1; // 262143

    // SS-34: health dirty bits raised before the ghost exists latch here and flush on CreateGhost.
    private ulong _pendingGhostMasks;

    /// <summary>Test seam: masks latched while no ghost existed (SS-34).</summary>
    internal ulong PendingGhostMasksForTests => _pendingGhostMasks;

    /// <summary>
    /// Dirties health-related ghost bits. Vehicles override to use
    /// <see cref="Vehicle.EnsureGhostMaskDelivery"/> so bits are not dropped without a GhostInfo ref.
    /// SS-34: with no ghost yet (creature scoped in later), the bits latch instead of being lost.
    /// </summary>
    protected virtual void DirtyHealthMasks(ulong mask)
    {
        if (Ghost == null)
        {
            _pendingGhostMasks |= mask;
            return;
        }

        Ghost.SetMaskBits(mask);
    }

    /// <summary>Delivers masks latched before the ghost existed. Call after ghost creation.</summary>
    protected void FlushPendingGhostMasks()
    {
        var pending = _pendingGhostMasks;
        if (pending == 0 || Ghost == null)
            return;

        _pendingGhostMasks = 0;
        Ghost.SetMaskBits(pending);
    }

    /// <summary>
    /// Absolute current HP set. Clamps to [0, MaxHP] and dirties <see cref="GhostObject.HealthMask"/>.
    /// Restoring HP above 0 clears corpse state (same idea as <see cref="GraphicsObject.RestoreHealth"/>)
    /// so combat pools, targeting, and living client state resume after /hp or script heals.
    /// When <paramref name="triggerGhostUpdate"/> is true, dirties the ghost. When
    /// <paramref name="notifyOwnerHud"/> is true (default), also notifies the owner HUD
    /// (<see cref="NotifyOwnerHealthHud"/> — CharacterLevel for vehicles).
    /// Chat commands set <paramref name="notifyOwnerHud"/> false and return the packet themselves
    /// so ChatManager is the single send path (same pattern as /power).
    /// </summary>
    public void SetCurrentHP(int hp, bool triggerGhostUpdate = true, bool notifyOwnerHud = true)
    {
        var newHp = Math.Clamp(hp, 0, Math.Max(MaxHP, 0));
        var wasCorpse = IsCorpse;
        if (HP == newHp && !(newHp > 0 && wasCorpse))
            return;

        var before = HP;
        var increased = newHp > HP;
        HP = newHp;

        // Death sets IsCorpse; /hp and admin heals only call SetCurrentHP (not Revive).
        // Sector combat-pool Advance skips corpses, so leave corpse stuck forever without this.
        if (newHp > 0 && wasCorpse)
        {
            IsCorpse = false;
            DeathType = DeathType.Silent;
            Murderer = new TFID();
            increased = true;
        }

        if (triggerGhostUpdate)
            DirtyHealthMasks(GhostObject.HealthMask);
        if (notifyOwnerHud && triggerGhostUpdate)
            NotifyOwnerHealthHud();

        // Chat / admin / scripts that raise HP must also open type-7 health gates.
        if (increased)
            NotifyPlayerHealthChangedForTriggers();

        // /reportbug: absolute HP changes that reach the client (skip silent pool seeding).
        if (triggerGhostUpdate)
        {
            if (increased)
            {
                var healed = newHp - before;
                if (healed > 0)
                    Diagnostics.PlayerCombatTrace.OnHeal(this, healed, "SetCurrentHP");
            }
            else if (before > newHp)
            {
                var lost = before - newHp;
                if (lost > 0)
                    Diagnostics.PlayerCombatTrace.OnDamage(this, attacker: null, lost);
            }
        }
    }

    /// <summary>
    /// Absolute max HP set. Clamps to [1, MaxWireHP], pulls current HP down when needed,
    /// and dirties <see cref="GhostObject.HealthMaxMask"/> (and Health if current changed).
    /// </summary>
    public void SetMaximumHP(int maxHp, bool triggerGhostUpdate = true, bool notifyOwnerHud = true)
    {
        var newMax = Math.Clamp(maxHp, 1, MaxWireHP);

        if (MaxHP == newMax)
        {
            if (HP > MaxHP)
                SetCurrentHP(MaxHP, triggerGhostUpdate, notifyOwnerHud);
            return;
        }

        MaxHP = newMax;
        var oldHp = HP;
        HP = Math.Clamp(HP, 0, MaxHP);

        if (!triggerGhostUpdate)
            return;

        DirtyHealthMasks(GhostObject.HealthMaxMask);
        if (HP != oldHp)
            DirtyHealthMasks(GhostObject.HealthMask);
        if (notifyOwnerHud)
            NotifyOwnerHealthHud();
    }

    /// <summary>
    /// Owner-facing absolute HUD sync after HP changes. Vehicles send CharacterLevel (0x2017).
    /// </summary>
    protected virtual void NotifyOwnerHealthHud(bool sendPacket = true)
    {
    }

    /// <summary>
    /// Test helper: set current HP without dirtying ghost masks.
    /// </summary>
    internal void SetHPForTests(int hp)
    {
        HP = Math.Clamp(hp, 0, Math.Max(1, MaxHP));
    }

    public SimpleObject(GraphicsObjectType type)
        : base(type)
    {
        MaxGadgets = 0;
        TeamFaction = 0;
        HP = MaxHP = 500;
        InventoryPositionX = 0;
        InventoryPositionY = 0;
        AlreadyAssembled = false;
        Quantity = 1;
        ItemTemplateId = -1;
        SkillLevel1 = 1;
        SkillLevel2 = 1;
        SkillLevel3 = 1;
    }

    public virtual bool LoadFromDB(CharContext context, long coid, bool isInCharacterSelection = false)
    {
        SetCoid(coid, true);

        DBData = context.SimpleObjects.FirstOrDefault(so => so.Coid == coid);
        if (DBData == null)
            return false;

        // SS-31 residual: equipment loaders share this path. Never treat a character/vehicle
        // identity row as inventory gear — that is how a coid collision used to launder a
        // corrupted row into CreateCharacter/CreateVehicle (client AV 0x0080A62A).
        if (DBData.Type == (byte)CloneBaseObjectType.Character
            || DBData.Type == (byte)CloneBaseObjectType.Vehicle)
        {
            Logger.WriteLog(LogType.Error,
                "SimpleObject.LoadFromDB: coid={0} is type={1} (character/vehicle identity); " +
                "refusing equipment load (SS-31)",
                coid, DBData.Type);
            return false;
        }

        if (DBData.CBID <= 0)
        {
            Logger.WriteLog(LogType.Error,
                "SimpleObject.LoadFromDB: coid={0} cbid={1} — skipping (SS-31 placeholder/corrupt identity)",
                coid, DBData.CBID);
            return false;
        }

        LoadCloneBase(DBData.CBID);

        SetupCBFields();

        return true;
    }

    public void SetupCBFields()
    {
        HP = MaxHP = CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;
        Faction = CloneBaseObject.SimpleObjectSpecific.Faction;
    }

    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

        Ghost = new GhostObject();
        Ghost.SetParent(this);
        GhostObjectDiag.RecordEntity("CreateGhost", this, extra: "source=SimpleObject");
        FlushPendingGhostMasks();
    }

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        if (CloneBaseObject == null)
        {
            Logger.WriteLog(LogType.Error, $"SimpleObject.WriteToPacket: CloneBaseObject is null for object with COID {ObjectId.Coid}. Cannot write packet.");
            throw new InvalidOperationException($"CloneBaseObject is null. Object was not properly loaded.");
        }

        packet.CBID = CBID;
        packet.ObjectId = ObjectId;
        packet.CurrentHealth = HP;
        packet.MaximumHealth = MaxHP;
        packet.Quantity = Quantity;
        packet.InventoryPositionX = InventoryPositionX;
        packet.InventoryPositionY = InventoryPositionY;
        packet.Value = CloneBaseObject.CloneBaseSpecific.BaseValue;
        packet.Faction = Faction;
        packet.TeamFaction = TeamFaction;
        packet.CoidStore = -1;
        packet.IsCorpse = IsCorpse;
        packet.SkillLevel1 = SkillLevel1;
        packet.SkillLevel2 = SkillLevel2;
        packet.SkillLevel3 = SkillLevel3;
        packet.IsIdentified = true;
        packet.PossibleMissionItem = PossibleMissionItem;
        packet.TempItem = false;
        packet.WillEquip = false;
        packet.IsInInventory = false;
        packet.IsItemLink = false;
        packet.IsBound = true;
        packet.UsesLeft = CloneBaseObject.SimpleObjectSpecific.MaxUses;
        packet.CustomizedName = string.Empty;
        packet.MadeFromMemory = false;
        packet.IsMail = false;
        packet.CustomValue = CustomValue;
        packet.IsKit = false;
        packet.IsInfinite = false;

        for (var i = 0; i < 5; ++i)
        {
            packet.Prefixes[i] = -1;
            packet.PrefixLevels[i] = 0;

            packet.Gadgets[i] = -1;
            packet.GadgetLevels[i] = 0;
        }

        packet.MaxGadgets = MaxGadgets;
        packet.ItemTemplateId = ItemTemplateId;
        packet.RequiredLevel = CloneBaseObject.SimpleObjectSpecific.RequiredLevel;
        packet.RequiredCombat = CloneBaseObject.SimpleObjectSpecific.RequiredCombat;
        packet.RequiredPerception = CloneBaseObject.SimpleObjectSpecific.RequiredPerception;
        packet.RequiredTech = CloneBaseObject.SimpleObjectSpecific.RequiredTech;
        packet.RequiredTheory = CloneBaseObject.SimpleObjectSpecific.RequiredTheory;
        packet.Scale = Scale;
        packet.Position = Position;
        packet.Rotation = Rotation;
    }
}
