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

        EnsurePowerPlantCapacity(character);
        var state = GetOrCreate(character.ObjectId.Coid);
        lock (state)
        {
            return new CharacterLevelPacket
            {
                CharacterId = character.ObjectId,
                Level = character.Level,
                Experience = character.Experience,
                Health = character.CurrentVehicle?.GetCurrentHP() ?? character.GetCurrentHP(),
                HealthMaximum = character.CurrentVehicle?.GetMaximumHP() ?? character.GetMaximumHP(),
                Currency = character.Credits,
                SkillPoints = character.SkillPoints,
                AttributePoints = character.AttributePoints,
                ResearchPoints = character.ResearchPoints,
                AttributeTech = character.AttributeTech,
                AttributeCombat = character.AttributeCombat,
                AttributeTheory = character.AttributeTheory,
                AttributePerception = character.AttributePerception,
                CurrentMana = state.CurrentMana,
                MaxMana = state.MaxMana
            };
        }
    }

    /// <summary>
    /// Sets current mana. When <paramref name="sendPacket"/> is true and the character has an
    /// owning connection, sends the absolute CharacterLevel power snapshot.
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
        if (sendPacket)
            character.OwningConnection?.SendGamePacket(packet);

        return packet;
    }

    /// <summary>Set the unified power pool; legacy packet fields remain CurrentMana/MaxMana.</summary>
    public CharacterLevelPacket SetPower(Character character, short power, bool sendPacket = true)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var state = GetOrCreate(character.ObjectId.Coid);
        lock (state)
        {
            var value = Math.Max(power, (short)0);
            state.CurrentMana = value;
            state.MaxMana = value;
        }

        var packet = BuildPacket(character);
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

    /// <summary>
    /// Initialize the placeholder 10/10 pool from Theory + class + power plant (retail core formula).
    /// </summary>
    public void EnsurePowerPlantCapacity(Character character)
    {
        if (character?.CurrentVehicle == null)
            return;
        var specific = character.CurrentVehicle.PowerPlant?.CloneBasePowerPlant?.PowerPlantSpecific;
        var plantMax = specific?.PowerMaximum ?? 0;
        if (plantMax <= 0)
            return;

        byte classId = 0;
        if (character.CloneBaseObject is CloneBases.CloneBaseCharacter charCb)
            classId = charCb.CharacterSpecific.Class;

        var maximum = Combat.VehiclePowerCalculator.CalculatePlayerMaxPower(
            classId,
            character.Level,
            character.AttributeTheory,
            plantMax);
        if (maximum <= 0)
            return;

        var state = GetOrCreate(character.ObjectId.Coid);
        lock (state)
        {
            if (state.MaxMana == 10 && state.CurrentMana == 10)
            {
                var value = (short)Math.Clamp(maximum, 0, short.MaxValue);
                state.MaxMana = value;
                state.CurrentMana = value;
            }
        }
    }

    public (short Current, short Maximum) GetPower(long characterCoid)
    {
        var state = GetOrCreate(characterCoid);
        lock (state)
            return (state.CurrentMana, state.MaxMana);
    }

    /// <summary>
    /// Dirties the vehicle ghost <see cref="GhostVehicle.PowerMask"/> so the client receives
    /// absolute current power (same path as plant regen). Does not send <see cref="CharacterLevelPacket"/>.
    /// Use after cast rejections so optimistic client spend can be restored to server truth.
    /// Do not call on successful skill spend — client already deducted optimistically.
    /// </summary>
    public void SyncCurrentPowerGhost(Character character)
    {
        character?.CurrentVehicle?.EnsureGhostMaskDelivery(GhostVehicle.PowerMask);
    }

    /// <summary>
    /// Absolute owner HUD snapshot via <see cref="CharacterLevelPacket"/> (0x2017).
    /// Client <c>CVOGCharacter_ApplyCharacterLevelPacket</c> @ 0x00531E90 always applies mana and,
    /// when in-world, vehicle Health/HealthMaximum through combat setters.
    /// Same delivery path as <c>/power</c>; use for HP changes (heat/shield stay on ghost masks).
    /// </summary>
    public CharacterLevelPacket SyncOwnedCombatHud(Character character, bool sendPacket = true)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var packet = BuildPacket(character);
        if (sendPacket)
            character.OwningConnection?.SendGamePacket(packet);
        return packet;
    }
}

/// <summary>In-memory mana/power for one character.</summary>
public class CharacterManaState
{
    public short CurrentMana { get; set; }
    public short MaxMana { get; set; }
}
