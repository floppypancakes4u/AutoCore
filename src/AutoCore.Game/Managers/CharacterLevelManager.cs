namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils.Memory;

/// <summary>
/// In-memory character mana/power state for client sync.
/// Max mana is replicated via <see cref="CharacterLevelPacket"/>; current mana also via
/// <see cref="GhostVehicle.PowerMask"/>.
/// </summary>
public class CharacterLevelManager : Singleton<CharacterLevelManager>
{
    private readonly ConcurrentDictionary<long, CharacterManaState> _cache = new();

    public CharacterManaState GetOrCreate(long characterCoid)
    {
        return _cache.GetOrAdd(characterCoid, _ => new CharacterManaState
        {
            CurrentMana = 10,
            MaxMana = 10
        });
    }

    public CharacterLevelPacket BuildPacket(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var state = GetOrCreate(character.ObjectId.Coid);
        lock (state)
        {
            return new CharacterLevelPacket
            {
                CharacterId = character.ObjectId,
                Level = character.Level,
                Experience = character.Experience,
                Currency = character.Credits,
                SkillPoints = character.SkillPoints,
                AttributePoints = character.AttributePoints,
                ResearchPoints = character.ResearchPoints,
                CurrentMana = state.CurrentMana,
                MaxMana = state.MaxMana
            };
        }
    }

    /// <summary>
    /// Sets current mana. When <paramref name="sendPacket"/> is true and the character has an
    /// owning connection, sends CharacterLevel; always dirties vehicle PowerMask when possible.
    /// </summary>
    public CharacterLevelPacket SetCurrentMana(Character character, short newCurrent, bool sendPacket = true)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var state = GetOrCreate(character.ObjectId.Coid);
        lock (state)
        {
            var maxMana = Math.Max(state.MaxMana, (short)0);
            state.CurrentMana = Math.Clamp(newCurrent, (short)0, maxMana);
        }

        var packet = BuildPacket(character);
        character.CurrentVehicle?.EnsureGhostMaskDelivery(GhostVehicle.PowerMask);

        if (sendPacket)
            character.OwningConnection?.SendGamePacket(packet);

        return packet;
    }

    public CharacterLevelPacket SetMaxMana(Character character, short newMax, bool sendPacket = true)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var state = GetOrCreate(character.ObjectId.Coid);
        lock (state)
        {
            var clampedMax = Math.Max(newMax, (short)0);
            state.MaxMana = clampedMax;
            if (state.CurrentMana > clampedMax)
                state.CurrentMana = clampedMax;
        }

        var packet = BuildPacket(character);
        character.CurrentVehicle?.EnsureGhostMaskDelivery(GhostVehicle.PowerMask);

        if (sendPacket)
            character.OwningConnection?.SendGamePacket(packet);

        return packet;
    }

    public short GetCurrentMana(long characterCoid)
    {
        var state = GetOrCreate(characterCoid);
        lock (state)
            return state.CurrentMana;
    }

    /// <summary>Test helper: drop all cached mana state between tests.</summary>
    internal void ClearAllForTests() => _cache.Clear();
}

/// <summary>In-memory mana/power for one character.</summary>
public class CharacterManaState
{
    public short CurrentMana { get; set; }
    public short MaxMana { get; set; }
}
