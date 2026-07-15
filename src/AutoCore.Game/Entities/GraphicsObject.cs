using System.Linq;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
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
    /// Plain <see cref="GhostObject"/> + local map TFID crashes the client at
    /// <c>0x005B0EFF</c> (FUN_005b0ed0 null iface after waiting-bind). Map props stay
    /// un-ghosted; server HP / delayed DestroyObject do not need a combat ghost.
    /// Vehicles/creatures use their own ghost types and override as needed.
    /// </summary>
    protected virtual bool GhostWhenDamagable => false;

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

    public override int RestoreHealth(int amount)
    {
        if (amount <= 0)
            return 0;

        EnsureHealthInitialized();

        if (IsCorpse)
        {
            // Some scripted mission damage leaves the player vehicle at positive HP while the
            // death flag remains latched. That state is alive on the wire (e.g. 1/100) and must
            // be repairable, but a genuinely dead zero-HP entity must not be resurrected here.
            if (HP <= 0)
                return 0;

            Logger.WriteLog(LogType.Debug,
                "RestoreHealth: clearing stale corpse state for {0} coid={1} hp={2}/{3}",
                GetType().Name,
                ObjectId.Coid,
                HP,
                MaxHP);
            IsCorpse = false;
            DeathType = DeathType.Silent;
            Murderer = new TFID();
        }

        var restored = Math.Min(amount, Math.Max(0, MaxHP - HP));
        if (restored == 0)
            return 0;

        HP += restored;
        EnsureCombatGhost();
        Ghost?.SetMaskBits(GhostObject.HealthMask);

        // Type-7 health% conditions (SCAB pad etc.) only re-eval on movement by default;
        // recheck collision volumes so full-heal gates fire while standing still.
        NotifyPlayerHealthChangedForTriggers();
        return restored;
    }

    /// <summary>
    /// After player-owned HP increases, re-run collision condition checks so live-computed
    /// vars (type 7 health percent) can open volume gates without a new move packet.
    /// Does not advance repair-pad skill cadence (see <see cref="Managers.TriggerManager.OnPlayerHealthChanged"/>).
    /// </summary>
    protected void NotifyPlayerHealthChangedForTriggers()
    {
        if (Map == null)
            return;

        var character = GetAsCharacter() ?? GetSuperCharacter(false);
        if (character == null)
            return;

        // Prefer the vehicle volume collider the pad/gate uses.
        var activator = character.CurrentVehicle ?? (ClonedObjectBase)character;
        if (activator.Map == null)
            return;

        Managers.TriggerManager.Instance.OnPlayerHealthChanged(activator);
    }

    public override void Revive()
    {
        EnsureHealthInitialized();
        HP = Math.Max(1, MaxHP);
        base.Revive();
    }

    /// <summary>
    /// When set (e.g. by ram), loot spawns here instead of <see cref="Position"/>.
    /// Server map-prop coords can be wrong/stale; vehicle pose is the reliable ram point.
    /// </summary>
    public Vector3? DeathLootOverridePosition { get; set; }

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

        // Map props: loot at death point; keep corpse on map ~12.5s then leave.
        // Broadcast DestroyObject immediately so weapon fire is visible — ram has client-local
        // collision FX, but server gun hits have no client prediction for un-ghosted scenery.
        TrySpawnMapPropDeathLoot(map);

        BroadcastDeath(
            map,
            objectId,
            deathType,
            Murderer,
            Ghost,
            useInitCreateDeath: deathType != DeathType.Silent);

        Logger.WriteLog(LogType.Debug,
            "OnDeath map-prop coid={0} type={1} deathType={2} cbid={3} pos={4} — destroy now, leave map in {5}ms",
            objectId.Coid,
            GetType().Name,
            deathType,
            CBID,
            Position,
            Combat.MapPropCorpseDespawn.DespawnDelayMs);

        Combat.MapPropCorpseDespawn.Schedule(this, map, deathType, Murderer);
    }

    /// <summary>Exposed for delayed corpse despawn finalizer (map props only).</summary>
    public static void BroadcastDeathPublic(
        SectorMap map,
        Structures.TFID objectId,
        DeathType deathType,
        Structures.TFID murderer,
        GhostObject ghost,
        bool useInitCreateDeath = true) =>
        BroadcastDeath(map, objectId, deathType, murderer, ghost, useInitCreateDeath);

    /// <summary>
    /// Drops salvage/junk near the death point for pure map <see cref="GraphicsObject"/> deaths.
    /// Vehicles/creatures skip this via <see cref="RemoveFromMapOnDeath"/> = false.
    /// </summary>
    private void TrySpawnMapPropDeathLoot(Map.SectorMap map)
    {
        try
        {
            Character killer = null;
            ClonedObjectBase murdererObj = null;
            if (Murderer.Coid > 0)
            {
                murdererObj = Managers.ObjectManager.Instance.GetObject(Murderer);
                killer = murdererObj?.GetSuperCharacter(false);
            }

            // Prefer killer level for commodity level bands; fall back to map min level.
            byte level = 1;
            if (killer != null)
                level = killer.Level;
            else if (map.ContinentObject != null && map.ContinentObject.MinLevel > 0)
                level = (byte)Math.Clamp(map.ContinentObject.MinLevel, 1, 255);

            var lootPos = ResolveMapPropLootPosition(murdererObj);
            // Prefer killer vehicle rotation (prop quat can be garbage from map deserialize).
            var lootRot = murdererObj?.Rotation ?? Rotation;
            if (lootRot.W == 0f && lootRot.X == 0f && lootRot.Y == 0f && lootRot.Z == 0f)
                lootRot = new Quaternion(0f, 0f, 0f, 1f);

            Logger.WriteLog(LogType.Debug,
                "Map prop death loot coid={0} cbid={1} lootPos={2} propPos={3} override={4} murderer={5}",
                ObjectId.Coid,
                CBID,
                lootPos,
                Position,
                DeathLootOverridePosition.HasValue ? DeathLootOverridePosition.Value.ToString() : "none",
                murdererObj != null ? murdererObj.Position.ToString() : "none");

            Managers.LootManager.Instance.ProcessDeathLoot(new Managers.LootManager.DeathLootRequest
            {
                Map = map,
                Position = lootPos,
                Rotation = lootRot,
                Killer = killer,
                VictimCbid = CBID,
                Level = level,
                LootTableId = 0,
                TemplateLootChance = 0,
                GearRolls = 0,
                UseCreatureDropFormula = false,
                // Tight scatter — must land at the wreck, not map-scale noise.
                LootScatterRadius = 0.25f,
                // Retail: only tLootWeights for this CBID (no commodity pool on random scenery).
                MapPropSalvage = true,
            });

            DeathLootOverridePosition = null;
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "Map prop death loot failed coid={0} cbid={1}: {2}",
                ObjectId.Coid,
                CBID,
                ex.Message);
        }
    }

    /// <summary>
    /// Map-prop server coords are often wrong/stale vs client fam placement.
    /// Prefer explicit ram override, then murderer (vehicle) pose, then prop pose.
    /// </summary>
    private Vector3 ResolveMapPropLootPosition(ClonedObjectBase murdererObj)
    {
        if (DeathLootOverridePosition.HasValue)
            return DeathLootOverridePosition.Value;

        // Killer vehicle pose is the reliable contact point for ram / combat kills.
        if (murdererObj != null)
        {
            var m = murdererObj.Position;
            if (Math.Abs(m.X) > 0.01f || Math.Abs(m.Y) > 0.01f || Math.Abs(m.Z) > 0.01f)
                return m;

            // Murderer at origin is suspicious — fall through to prop if prop looks valid.
        }

        var p = Position;
        if (Math.Abs(p.X) > 0.01f || Math.Abs(p.Y) > 0.01f || Math.Abs(p.Z) > 0.01f)
            return p;

        return murdererObj?.Position ?? p;
    }

    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

        Ghost = new GhostObject();
        Ghost.SetParent(this);
        GhostObjectDiag.RecordEntity("CreateGhost", this, extra: "source=GraphicsObject");
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
        // Vehicles/creatures inherit SetInvincible → here, but they do not use plain GhostObject
        // combat ghosts (GhostWhenDamagable=false). Only log the path that can feed 0x005B0EFF.
        if (GhostWhenDamagable)
            GhostObjectDiag.RecordEntity("BecameDamagable", this, extra: "masks=Health|Position");
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
        {
            GhostObjectDiag.RecordEntity("EnsureCombatGhost", this, extra: "created=1");
            ScopeGhostToMapPlayers();
        }
        else if (Ghost != null)
            GhostObjectDiag.RecordEntity("EnsureCombatGhost", this, extra: "created=0");
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
                GhostObjectDiag.RecordEntity(
                    "ScopeAlways",
                    this,
                    playerCoid: character.ObjectId?.Coid ?? 0,
                    extra: "via=ScopeGhostToMapPlayers");
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

    /// <summary>
    /// Combat death network (call <b>before</b> <c>SetMap(null)</c>).
    /// <para>
    /// Client RE (important split):
    /// </para>
    /// <list type="bullet">
    /// <item><b>Map props</b> (clone types 1 / 3): <see cref="InitCreateObjectPacket"/>
    /// DoDeath → <c>CVOGReaction_RemoveObject(..., death=1)</c> → vfunc+0x50(Violent).
    /// Do <b>not</b> also send DestroyObject first — RemoveObject already tears down.</item>
    /// <item><b>Creature / vehicle</b> (types 18 / 14): only
    /// <see cref="DestroyObjectPacket"/> with non-silent <see cref="DeathType"/> →
    /// CompletelyDestroyObject → primary vtable+0x50 death FX.
    /// InitCreateObject DoDeath is harmful: RemoveObject removes without FX, so DestroyObject
    /// fails to resolve.</item>
    /// </list>
    /// </summary>
    /// <param name="useInitCreateDeath">
    /// True only for Object / ObjectGraphicsPhysics map props. False for creatures/vehicles.
    /// </param>
    protected static void BroadcastDeath(
        SectorMap map,
        Structures.TFID objectId,
        DeathType deathType = DeathType.Silent,
        Structures.TFID murderer = null,
        GhostObject ghost = null,
        bool useInitCreateDeath = false)
    {
        if (map == null || objectId == null)
            return;

        var playDeathFx = deathType != DeathType.Silent;

        // Props: InitCreateObject DoDeath alone (RemoveObject owns death FX + teardown).
        // Creatures/vehicles: DestroyObject with death type (CompletelyDestroyObject → OnDeath FX).
        // Silent: DestroyObject force-teardown (loot pickup, re-scope).
        InitCreateObjectPacket initDeath = null;
        DestroyObjectPacket destroyPacket = null;
        if (playDeathFx && useInitCreateDeath)
        {
            initDeath = new InitCreateObjectPacket(objectId.Coid, create: false, doDeath: true);
        }
        else
        {
            destroyPacket = new DestroyObjectPacket(
                objectId,
                deathType,
                murderer,
                force: !playDeathFx);
        }

        foreach (var character in map.Objects.Values.OfType<Character>().Where(c => c.OwningConnection != null))
        {
            try
            {
                if (ForceNetworkHelperFailureForTests)
                    throw new InvalidOperationException("test-forced destroy send failure");

                var conn = character.OwningConnection;

                if (ghost != null && ghost.IsGhostedTo(conn))
                {
                    ghost.SetMaskBits(GhostObject.HealthMask);
                    conn.FlushDeathGhostUpdate();
                }

                if (initDeath != null)
                    conn.SendGamePacket(initDeath);

                if (destroyPacket != null)
                    conn.SendGamePacket(destroyPacket);

                Logger.WriteLog(LogType.Network,
                    "DeathNet coid={0} deathType={1} murderer={2} initDoDeath={3} destroy={4} force={5} player={6}",
                    objectId.Coid,
                    deathType,
                    murderer?.Coid ?? -1,
                    initDeath != null ? 1 : 0,
                    destroyPacket != null ? 1 : 0,
                    destroyPacket?.Force == true ? 1 : 0,
                    character.ObjectId?.Coid ?? 0);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "BroadcastDeath failed for coid={0}: {1}", objectId.Coid, ex.Message);
            }
        }
    }

    /// <summary>
    /// Silent / non-combat despawn (item pickup, foreign re-scope).
    /// </summary>
    protected static void BroadcastDestroy(
        SectorMap map,
        Structures.TFID objectId,
        DeathType deathType = DeathType.Silent,
        Structures.TFID murderer = null,
        bool force = false)
    {
        BroadcastDeath(map, objectId, deathType, murderer, ghost: null, useInitCreateDeath: false);
    }
}
