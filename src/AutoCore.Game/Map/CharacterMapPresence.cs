namespace AutoCore.Game.Map;

/// <summary>
/// Per-character, per-continent mission-world presence.
/// Retail Create/Delete of map-template objects apply on the receiving client via 0x206C
/// (<c>CVOGReaction_SpawnObject</c> / <c>RemoveObject</c>); the server must not rewrite a shared
/// <see cref="SectorMap"/> so every player inherits one visitor's mid-mission state.
/// </summary>
public sealed class CharacterMapPresence
{
    readonly HashSet<long> _suppressed = new();
    readonly HashSet<long> _materialized = new();
    readonly HashSet<long> _ownedCombat = new();
    /// <summary>
    /// Ground-loot COIDs this character has already received a CreateSimpleObject for. Ground loot
    /// carries no ghost, so TNL's own scope bookkeeping cannot dedupe it — without this ledger the
    /// per-packet scope query would re-create every nearby item forever.
    /// </summary>
    readonly HashSet<long> _groundLootDelivered = new();
    /// <summary>Deliver CBIDs that already received Create+CreateCreature this continent visit.</summary>
    readonly HashSet<int> _deliverTurnInReady = new();
    /// <summary>
    /// Mission ids for which we already sent a one-shot client resync after AutoPatrol on a
    /// finished (prior-sequence) patrol pad — stops per-tick spam while the client still shows
    /// old waypoints after server advanced (Track This seq2 deliver while client patrols).
    /// </summary>
    readonly HashSet<int> _stalePatrolResyncedMissions = new();
    /// <summary>
    /// AutoPatrol target COID → quest-state fingerprint last fully handled as a no-op or
    /// already-applied hit. Client spams 0x20B3 every tick inside a pad volume; identical
    /// state must not re-scan missions or log every frame.
    /// </summary>
    readonly Dictionary<long, string> _autoPatrolHandled = new();

    public int ContinentId { get; private set; } = -1;

    /// <summary>
    /// <see cref="SectorMap.InstanceSerial"/> this ledger is bound to. Per-player instances of
    /// one continent mint identical live-spawn COIDs, so the ledger must reset when the
    /// character lands on a different INSTANCE even when the continent id is unchanged.
    /// </summary>
    public int InstanceSerial { get; private set; } = -1;

    /// <summary>Map COIDs this character has deleted (client RemoveObject / no interact).</summary>
    public IReadOnlyCollection<long> SuppressedCoids => _suppressed;

    /// <summary>Map COIDs this character has created/activated beyond fam default.</summary>
    public IReadOnlyCollection<long> MaterializedCoids => _materialized;

    /// <summary>Server combat entities (MapNpcIdentity) owned by this character.</summary>
    public IReadOnlyCollection<long> OwnedCombatCoids => _ownedCombat;

    /// <summary>
    /// Binds presence to one map instance. Clears the ledger when the character changes maps —
    /// including a different instance of the SAME continent (fresh tutorial copy on relog).
    /// </summary>
    public void EnsureContinent(int continentId, int instanceSerial)
    {
        if (ContinentId == continentId && InstanceSerial == instanceSerial)
            return;

        ContinentId = continentId;
        InstanceSerial = instanceSerial;
        _suppressed.Clear();
        _materialized.Clear();
        _ownedCombat.Clear();
        _deliverTurnInReady.Clear();
        _stalePatrolResyncedMissions.Clear();
        _autoPatrolHandled.Clear();
        _groundLootDelivered.Clear();
    }

    public void Clear()
    {
        ContinentId = -1;
        InstanceSerial = -1;
        _suppressed.Clear();
        _materialized.Clear();
        _ownedCombat.Clear();
        _deliverTurnInReady.Clear();
        _stalePatrolResyncedMissions.Clear();
        _autoPatrolHandled.Clear();
        _groundLootDelivered.Clear();
    }

    /// <summary>
    /// True when this AutoPatrol target was already handled for the same quest fingerprint
    /// (mission ids, active sequences, pad progress). Skip full handler work and logging.
    /// </summary>
    public bool ShouldSkipRedundantAutoPatrol(long targetCoid, string questStateFingerprint)
    {
        if (targetCoid <= 0 || string.IsNullOrEmpty(questStateFingerprint))
            return false;
        return _autoPatrolHandled.TryGetValue(targetCoid, out var prior)
            && prior == questStateFingerprint;
    }

    /// <summary>
    /// Record that AutoPatrol for <paramref name="targetCoid"/> produced no further useful work
    /// at <paramref name="questStateFingerprint"/> (already-counted pad, sibling ready, no-match).
    /// </summary>
    public void NoteAutoPatrolHandled(long targetCoid, string questStateFingerprint)
    {
        if (targetCoid <= 0 || string.IsNullOrEmpty(questStateFingerprint))
            return;
        _autoPatrolHandled[targetCoid] = questStateFingerprint;
    }

    /// <summary>
    /// True after a successful one-shot deliver turn-in setup (Create + CreateCreature) for this CBID.
    /// Prevents AutoPatrol (client spam while in pad volume) from re-firing every tick.
    /// </summary>
    public bool IsDeliverTurnInReady(int deliverCbid)
        => deliverCbid > 0 && _deliverTurnInReady.Contains(deliverCbid);

    public void MarkDeliverTurnInReady(int deliverCbid)
    {
        if (deliverCbid > 0)
            _deliverTurnInReady.Add(deliverCbid);
    }

    /// <summary>True if we already re-synced client after stale AutoPatrol for this mission this map.</summary>
    public bool HasStalePatrolResync(int missionId)
        => missionId > 0 && _stalePatrolResyncedMissions.Contains(missionId);

    public void MarkStalePatrolResync(int missionId)
    {
        if (missionId > 0)
            _stalePatrolResyncedMissions.Add(missionId);
    }

    /// <summary>True once this character has been sent the create for a ground-loot COID.</summary>
    public bool HasGroundLootDelivered(long coid) => _groundLootDelivered.Contains(coid);

    public void MarkGroundLootDelivered(long coid) => _groundLootDelivered.Add(coid);

    /// <summary>
    /// Forgets a ground-loot COID (picked up, despawned, or otherwise off the map). Loot COIDs are
    /// never reissued (see <see cref="MapLootIdentity"/>), so this is about keeping the ledger from
    /// growing without bound for the life of a map visit rather than about COID reuse.
    /// </summary>
    public void ForgetGroundLoot(long coid) => _groundLootDelivered.Remove(coid);

    public bool IsSuppressed(long coid) => coid > 0 && _suppressed.Contains(coid);

    public bool IsMaterialized(long coid) => coid > 0 && _materialized.Contains(coid);

    public bool OwnsCombat(long coid) => coid > 0 && _ownedCombat.Contains(coid);

    /// <summary>
    /// True when this character may treat the COID as present for interact / visibility:
    /// not suppressed, and either fam-default still valid or explicitly materialized.
    /// </summary>
    public bool IsPresentForCharacter(long coid, bool famDefaultActive)
    {
        if (coid <= 0)
            return false;
        if (_suppressed.Contains(coid))
            return false;
        if (_materialized.Contains(coid))
            return true;
        return famDefaultActive;
    }

    public void Suppress(long coid)
    {
        if (coid <= 0)
            return;
        _suppressed.Add(coid);
        _materialized.Remove(coid);
    }

    /// <summary>
    /// Clears personal suppress so a fam-default (or later re-materialized) COID is interactable again.
    /// Used when phase rules previously suppressed a same-NPC deliver giver incorrectly.
    /// </summary>
    public void Unsuppress(long coid)
    {
        if (coid <= 0)
            return;
        _suppressed.Remove(coid);
    }

    public void Materialize(long coid)
    {
        if (coid <= 0)
            return;
        _materialized.Add(coid);
        _suppressed.Remove(coid);
    }

    public void TrackOwnedCombat(long coid)
    {
        if (coid > 0)
            _ownedCombat.Add(coid);
    }

    public void UntrackOwnedCombat(long coid)
    {
        if (coid > 0)
            _ownedCombat.Remove(coid);
    }

    public void SuppressMany(IEnumerable<long> coids)
    {
        if (coids == null)
            return;
        foreach (var coid in coids)
            Suppress(coid);
    }

    public void MaterializeMany(IEnumerable<long> coids)
    {
        if (coids == null)
            return;
        foreach (var coid in coids)
            Materialize(coid);
    }
}
