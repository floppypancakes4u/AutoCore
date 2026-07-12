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

    public int ContinentId { get; private set; } = -1;

    /// <summary>Map COIDs this character has deleted (client RemoveObject / no interact).</summary>
    public IReadOnlyCollection<long> SuppressedCoids => _suppressed;

    /// <summary>Map COIDs this character has created/activated beyond fam default.</summary>
    public IReadOnlyCollection<long> MaterializedCoids => _materialized;

    /// <summary>Server combat entities (MapNpcIdentity) owned by this character.</summary>
    public IReadOnlyCollection<long> OwnedCombatCoids => _ownedCombat;

    /// <summary>
    /// Binds presence to a continent. Clears ledger when the character changes maps.
    /// </summary>
    public void EnsureContinent(int continentId)
    {
        if (ContinentId == continentId)
            return;

        ContinentId = continentId;
        _suppressed.Clear();
        _materialized.Clear();
        _ownedCombat.Clear();
    }

    public void Clear()
    {
        ContinentId = -1;
        _suppressed.Clear();
        _materialized.Clear();
        _ownedCombat.Clear();
    }

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
