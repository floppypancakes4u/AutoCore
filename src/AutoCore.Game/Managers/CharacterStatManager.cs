namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils.Memory;

public class CharacterStatManager : Singleton<CharacterStatManager>
{
    private readonly ConcurrentDictionary<long, CharacterStatsState> _cache = new();

    /// <summary>
    /// Gets or loads character stats from database. Creates default entry if missing.
    /// </summary>
    public CharacterStatsState GetOrLoad(long characterCoid)
    {
        if (_cache.TryGetValue(characterCoid, out var cached))
            return cached;

        CharacterStatsData dbStats;
        try
        {
            using var context = new CharContext();
            dbStats = context.CharacterStats.FirstOrDefault(s => s.CharacterCoid == characterCoid);
        }
        catch (Exception ex) when (ex.Message.Contains("character_stats") && ex.Message.Contains("doesn't exist"))
        {
            // Existing DB may not have been bootstrapped yet. Ensure schema and retry once.
            CharContext.EnsureCreated();

            using var context = new CharContext();
            dbStats = context.CharacterStats.FirstOrDefault(s => s.CharacterCoid == characterCoid);
        }

        if (dbStats == null)
        {
            // Create default stats entry
            dbStats = new CharacterStatsData
            {
                CharacterCoid = characterCoid,
                Currency = 0,
                Experience = 0,
                CurrentMana = 100,
                MaxMana = 100,
                AttributeTech = 1,
                AttributeCombat = 1,
                AttributeTheory = 1,
                AttributePerception = 1,
                AttributePoints = 0,
                SkillPoints = 0,
                ResearchPoints = 0
            };

            using var context = new CharContext();
            context.CharacterStats.Add(dbStats);
            context.SaveChanges();
        }

        var state = new CharacterStatsState(dbStats);
        _cache.TryAdd(characterCoid, state);
        return state;
    }

    /// <summary>
    /// Updates character stats using the provided mutator action, then persists to database.
    /// </summary>
    public CharacterStatsState Update(long characterCoid, Action<CharacterStatsState> mutator)
    {
        var state = GetOrLoad(characterCoid);

        lock (state)
        {
            mutator(state);

            // Persist to database
            using var context = new CharContext();
            var dbStats = context.CharacterStats.FirstOrDefault(s => s.CharacterCoid == characterCoid);
            
            if (dbStats == null)
            {
                dbStats = new CharacterStatsData { CharacterCoid = characterCoid };
                context.CharacterStats.Add(dbStats);
            }

            // Update DB entity from state
            dbStats.Currency = state.Currency;
            dbStats.Experience = state.Experience;
            dbStats.CurrentMana = state.CurrentMana;
            dbStats.MaxMana = state.MaxMana;
            dbStats.AttributeTech = state.AttributeTech;
            dbStats.AttributeCombat = state.AttributeCombat;
            dbStats.AttributeTheory = state.AttributeTheory;
            dbStats.AttributePerception = state.AttributePerception;
            dbStats.AttributePoints = state.AttributePoints;
            dbStats.SkillPoints = state.SkillPoints;
            dbStats.ResearchPoints = state.ResearchPoints;

            context.SaveChanges();
        }

        return state;
    }

    /// <summary>
    /// Builds a CharacterStatsPacket from the cached stats and character level.
    /// </summary>
    public CharacterStatsPacket BuildPacket(Character character)
    {
        var stats = GetOrLoad(character.ObjectId.Coid);
        
        lock (stats)
        {
            return new CharacterStatsPacket
            {
                CharacterId = character.ObjectId,
                Level = character.Level,
                Currency = stats.Currency,
                Experience = stats.Experience,
                CurrentMana = stats.CurrentMana,
                MaxMana = stats.MaxMana,
                AttributeTech = stats.AttributeTech,
                AttributeCombat = stats.AttributeCombat,
                AttributeTheory = stats.AttributeTheory,
                AttributePerception = stats.AttributePerception,
                AttributePoints = stats.AttributePoints,
                SkillPoints = stats.SkillPoints,
                ResearchPoints = stats.ResearchPoints
            };
        }
    }

    /// <summary>
    /// Applies level-up rewards for the specified number of levels gained.
    /// Per level: +1 combat, +1 tech, +1 theory, +1 perception, +2 attribute points, +2 skill points, +3 max mana.
    /// </summary>
    public void ApplyLevelUpRewards(long characterCoid, int levelsGained)
    {
        if (levelsGained <= 0)
            return;

        Update(characterCoid, stats =>
        {
            // Store old max mana for current mana adjustment
            var oldMaxMana = stats.MaxMana;
            
            // Apply rewards for each level gained
            stats.AttributeCombat += (short)levelsGained;
            stats.AttributeTech += (short)levelsGained;
            stats.AttributeTheory += (short)levelsGained;
            stats.AttributePerception += (short)levelsGained;
            stats.AttributePoints += (short)(levelsGained * 2);
            stats.SkillPoints += (short)(levelsGained * 2); // 2 skill points per level
            stats.MaxMana += (short)(levelsGained * 3);
            
            // Note: ResearchPoints are NOT modified on level up - they remain unchanged
            
            // Also increase current mana to match max if it was at max before leveling
            if (stats.CurrentMana >= oldMaxMana)
            {
                stats.CurrentMana = stats.MaxMana;
            }
        });
    }

    /// <summary>
    /// Grants XP to a character from a kill and handles level-ups if XP thresholds are met.
    /// Applies rewards using tExperienceLevel table values for points, while keeping base stat increases.
    /// </summary>
    public void GrantKillXPAndHandleLevelUps(Character killer, uint xpGained)
    {
        if (killer == null || xpGained == 0)
            return;

        var killerCoid = killer.ObjectId.Coid;
        var oldLevel = killer.Level;

        // Add XP
        Update(killerCoid, stats => stats.Experience += (int)xpGained);

        // Get updated stats to check level thresholds
        var stats = GetOrLoad(killerCoid);
        int newLevel;
        lock (stats)
        {
            newLevel = oldLevel;
            
            // Calculate new level by checking XP thresholds.
            // IMPORTANT: In AA's tExperienceLevel, the row keyed by your CURRENT level contains
            // the total XP needed to advance to the NEXT level (e.g., Level=1 row has 1000 XP to reach level 2).
            while (true)
            {
                var currentLevelData = AssetManager.Instance.GetExperienceLevel((byte)newLevel);
                if (currentLevelData == null)
                    break;

                // Don't go past the max level available in the table.
                var candidateNextLevel = (byte)(newLevel + 1);
                if (AssetManager.Instance.GetExperienceLevel(candidateNextLevel) == null)
                    break;

                if (stats.Experience >= (int)currentLevelData.Experience)
                    newLevel = candidateNextLevel;
                else
                    break;
            }
        }

        var levelsGained = newLevel - oldLevel;

        // If level increased, persist level and apply rewards
        if (levelsGained > 0)
        {
            // Persist level to CharacterData table (same pattern as /level command)
            using (var context = new CharContext())
            {
                var charData = context.Characters.FirstOrDefault(c => c.Coid == killerCoid);
                if (charData != null)
                {
                    charData.Level = (byte)newLevel;
                    context.SaveChanges();
                }
            }

            // Update in-memory character level
            killer.SetLevel((byte)newLevel);

            // Apply level-up rewards
            // Keep base stat increases (combat/tech/theory/perception + mana)
            // Use tExperienceLevel table for point rewards
            Update(killerCoid, stats =>
            {
                // Store old max mana for current mana adjustment
                var oldMaxMana = stats.MaxMana;

                // Apply base stat increases per level (from existing logic)
                stats.AttributeCombat += (short)levelsGained;
                stats.AttributeTech += (short)levelsGained;
                stats.AttributeTheory += (short)levelsGained;
                stats.AttributePerception += (short)levelsGained;
                stats.MaxMana += (short)(levelsGained * 3);

                // Sum point rewards from tExperienceLevel table for each level gained
                int totalSkillPoints = 0;
                int totalAttributePoints = 0;
                int totalResearchPoints = 0;

                for (byte level = (byte)(oldLevel + 1); level <= newLevel; level++)
                {
                    var levelData = AssetManager.Instance.GetExperienceLevel(level);
                    if (levelData != null)
                    {
                        totalSkillPoints += levelData.SkillPoints;
                        totalAttributePoints += levelData.AttributePoints;
                        totalResearchPoints += levelData.ResearchPoints;
                    }
                }

                stats.SkillPoints += (short)totalSkillPoints;
                stats.AttributePoints += (short)totalAttributePoints;
                stats.ResearchPoints += (short)totalResearchPoints;

                // Increase current mana to match max if it was at max before leveling
                if (stats.CurrentMana >= oldMaxMana)
                {
                    stats.CurrentMana = stats.MaxMana;
                }
            });
        }

        // Send packets to killer
        if (killer.OwningConnection != null)
        {
            // This is wrong. GiveXPPacket seems more like some kind of XP multiplier or something.
            // Send XP gain notification
            //killer.OwningConnection.SendGamePacket(new GiveXPPacket { XP = (int)xpGained });

            // Send updated stats packet
            var statsPacket = BuildPacket(killer);
            statsPacket.Level = (byte)newLevel;
            killer.OwningConnection.SendGamePacket(statsPacket);
        }
    }

    /// <summary>
    /// Removes character stats from cache (e.g., on logout).
    /// </summary>
    public void RemoveFromCache(long characterCoid)
    {
        _cache.TryRemove(characterCoid, out _);
    }
}

/// <summary>
/// Thread-safe in-memory representation of character stats.
/// </summary>
public class CharacterStatsState
{
    public long Currency { get; set; }
    public int Experience { get; set; }
    public short CurrentMana { get; set; }
    public short MaxMana { get; set; }
    public short AttributeTech { get; set; }
    public short AttributeCombat { get; set; }
    public short AttributeTheory { get; set; }
    public short AttributePerception { get; set; }
    public short AttributePoints { get; set; }
    public short SkillPoints { get; set; }
    public short ResearchPoints { get; set; }

    public CharacterStatsState(CharacterStatsData dbData)
    {
        Currency = dbData.Currency;
        Experience = dbData.Experience;
        CurrentMana = dbData.CurrentMana;
        MaxMana = dbData.MaxMana;
        AttributeTech = dbData.AttributeTech;
        AttributeCombat = dbData.AttributeCombat;
        AttributeTheory = dbData.AttributeTheory;
        AttributePerception = dbData.AttributePerception;
        AttributePoints = dbData.AttributePoints;
        SkillPoints = dbData.SkillPoints;
        ResearchPoints = dbData.ResearchPoints;
    }
}

