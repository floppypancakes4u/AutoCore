namespace AutoCore.Game.Entities;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

public abstract class ClonedObjectBase
{
    public CloneBaseObject CloneBaseObject { get; private set; }
    public CloneBaseObjectType Type => CloneBaseObject.Type;
    public int CBID => CloneBaseObject.CloneBaseSpecific.CloneBaseId;
    
    public int Faction { get; set; }
    public GhostObject Ghost { get; protected set; }
    //public int LastServerUpdate { get; protected set; }
    //public int TimeOfDeath { get; protected set; }
    public TFID Murderer { get; protected set; }
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

    public void LoadCloneBase(int cbid)
    {
        CloneBaseObject = AssetManager.Instance.GetCloneBase<CloneBaseObject>(cbid);

        Value = CloneBaseObject.CloneBaseSpecific.BaseValue;
        //GameMass = CloneBaseObject.SimpleObjectSpecific.Mass;
        IsInvincible = ((CloneBaseObject.SimpleObjectSpecific.Flags >> 12) & 1) != 0;
        RequiredLevel = CloneBaseObject.SimpleObjectSpecific.RequiredLevel;
        RequiredCombat = CloneBaseObject.SimpleObjectSpecific.RequiredCombat;
        RequiredPerception = CloneBaseObject.SimpleObjectSpecific.RequiredPerception;
        RequiredTech = CloneBaseObject.SimpleObjectSpecific.RequiredTech;
        RequiredTheory = CloneBaseObject.SimpleObjectSpecific.RequiredTheory;
        UsesLeft = CloneBaseObject.SimpleObjectSpecific.MaxUses;
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
        Ghost?.SetParent(null);
        Ghost = null;
        //LastServerUpdate = Environment.TickCount; // TODO: linux time or what?
    }

    public void SetMap(SectorMap map)
    {
        var oldMap = Map;

        Map = map;

        if (oldMap != map)
        {
            if (oldMap != null)
            {
                oldMap.LeaveMap(this);
            }

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

    public void OnDeath(DeathType deathType)
    {
        //TimeOfDeath = Environment.TickCount; // TODO: linux time or what?
        Ghost?.SetMaskBits(8);

        IsCorpse = true;
        // TODO: DeathType?
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
