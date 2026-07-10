using System.Linq;
using AutoCore.Game.Constants;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

namespace AutoCore.Game.Entities;

public enum GraphicsObjectType
{
    GraphicsPhysics,
    Graphics
}

/// <summary>
/// World/map visual (and physics) objects. Map templates create these for props that
/// can be mission destroy targets. Unlike inventory entities under <see cref="SimpleObject"/>,
/// map props must track mutable HP and (when damagable) ghost HealthMask updates so the
/// client HP bar and death match server combat.
/// </summary>
public class GraphicsObject : ClonedObjectBase
{
    public GraphicsObjectType ObjectType { get; }

    /// <summary>Runtime hit points for damagable map props.</summary>
    protected int HP { get; set; }

    /// <summary>Max hit points (from clonebase MaxHitPoint when loaded).</summary>
    protected int MaxHP { get; set; }

    /// <summary>
    /// When true, <see cref="OnDeath"/> removes the object from the map and broadcasts DestroyObject.
    /// Vehicles/items under <see cref="SimpleObject"/> keep corpse-only base behavior
    /// (creatures override fully).
    /// </summary>
    protected virtual bool RemoveFromMapOnDeath => true;

    /// <summary>
    /// Pure map props need combat ghosts; vehicles/creatures already ghost themselves.
    /// </summary>
    protected virtual bool GhostWhenDamagable => true;

    public GraphicsObject(GraphicsObjectType objectType)
    {
        ObjectType = objectType;
    }

    public override int GetCurrentHP() => HP;
    public override int GetMaximumHP() => MaxHP;
    public override int GetBareTeamFaction() => Faction;

    public override int TakeDamage(int damage)
    {
        if (IsInvincible || IsCorpse || damage <= 0)
            return 0;

        EnsureHealthInitialized();

        var before = HP;
        var actualDamage = Math.Min(damage, HP);
        HP = Math.Max(0, HP - actualDamage);

        if (actualDamage > 0)
        {
            EnsureCombatGhost();
            Ghost?.SetMaskBits(GhostObject.HealthMask);

            Logger.WriteLog(LogType.Debug,
                "TakeDamage: {0} coid={1} {2}->{3}/{4} dealt={5} (rolled={6})",
                GetType().Name,
                ObjectId.Coid,
                before,
                HP,
                MaxHP,
                actualDamage,
                damage);
        }

        return actualDamage;
    }

    public override void Revive()
    {
        EnsureHealthInitialized();
        HP = Math.Max(1, MaxHP);
        base.Revive();
    }

    public override void OnDeath(DeathType deathType)
    {
        base.OnDeath(deathType);

        Ghost?.SetMaskBits(GhostObject.HealthMask);

        if (!RemoveFromMapOnDeath)
            return;

        var objectId = ObjectId;
        var map = Map;
        if (map == null)
            return;

        Logger.WriteLog(LogType.Debug,
            "OnDeath map-prop coid={0} type={1} deathType={2} — DestroyObject broadcast",
            objectId.Coid,
            GetType().Name,
            deathType);

        // Leave map first so iterators over map objects stay consistent for send.
        SetMap(null);

        BroadcastDestroy(map, objectId);
    }

    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

        Ghost = new GhostObject();
        Ghost.SetParent(this);
    }

    /// <summary>
    /// Called from <see cref="ClonedObjectBase.LoadCloneBase"/> after clonebase fields are ready.
    /// </summary>
    protected override void OnCloneBaseLoaded()
    {
        var max = 0;
        if (CloneBaseObject != null)
            max = CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;
        MaxHP = Math.Max(1, max);
        HP = MaxHP;
        // Deliberately do not CreateGhost here. Mass-ghosting map props at load
        // fills the client ghost index and suppresses NPC ghosts. Ghost only when
        // the prop becomes a combat target (MakeNotInvincible / first damage).
    }

    protected override void OnBecameDamagable()
    {
        EnsureHealthInitialized();
        EnsureCombatGhost();
        Ghost?.SetMaskBits(GhostObject.HealthMask | GhostObject.PositionMask);
        ScopeGhostToMapPlayers();
    }

    /// <summary>Unit-test helper when clonebase.wad is unavailable.</summary>
    internal void InitializeHealthForTests(int maxHp)
    {
        MaxHP = Math.Max(1, maxHp);
        HP = MaxHP;
    }

    /// <summary>Test hook: force scope/broadcast helpers to hit error logging paths.</summary>
    internal static bool ForceNetworkHelperFailureForTests { get; set; }

    private void EnsureHealthInitialized()
    {
        if (MaxHP > 0)
            return;

        if (CloneBaseObject != null)
        {
            OnCloneBaseLoaded();
            return;
        }

        // Fallback so un-inited props are still killable (matches prior SimpleObject default of 500).
        MaxHP = 500;
        HP = MaxHP;
    }

    private void EnsureCombatGhost()
    {
        if (!GhostWhenDamagable)
            return;

        var created = Ghost == null;
        CreateGhost();
        if (created && Ghost != null)
            ScopeGhostToMapPlayers();
    }

    private void ScopeGhostToMapPlayers()
    {
        if (Ghost == null || Map == null)
            return;

        foreach (var character in Map.Objects.Values.OfType<Character>())
        {
            var conn = character.OwningConnection;
            if (conn == null)
                continue;

            try
            {
                if (ForceNetworkHelperFailureForTests)
                    throw new InvalidOperationException("test-forced scope failure");

                // Always-scope so HP updates are not lost if the prop was created/made
                // damagable after the player's initial scope query.
                conn.ObjectLocalScopeAlways(Ghost);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error,
                    "ScopeGhostToMapPlayers failed coid={0}: {1}",
                    ObjectId.Coid,
                    ex.Message);
            }
        }
    }

    protected static void BroadcastDestroy(SectorMap map, Structures.TFID objectId)
    {
        var destroyPacket = new DestroyObjectPacket(objectId);
        foreach (var character in map.Objects.Values.OfType<Character>().Where(c => c.OwningConnection != null))
        {
            try
            {
                if (ForceNetworkHelperFailureForTests)
                    throw new InvalidOperationException("test-forced destroy send failure");

                character.OwningConnection.SendGamePacket(destroyPacket);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "BroadcastDestroy failed for coid={0}: {1}", objectId.Coid, ex.Message);
            }
        }
    }
}
