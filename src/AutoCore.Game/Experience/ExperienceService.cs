namespace AutoCore.Game.Experience;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

/// <summary>
/// Server-authoritative XP grants, level-up, and formula helpers (docs/XP.md).
/// Persist absolute progress before notifying the client.
/// </summary>
public sealed class ExperienceService : Singleton<ExperienceService>
{
    /// <summary>Kill XP global scalar (docs/XP.md — client BSS is 0; server uses 1.0).</summary>
    public const float GlobalKillScalar = 1.0f;

    public const int GreyLevelDiff = 10;
    public const double GreySlope = 1.5 * -0.1; // ≈ -0.15 per level above
    public const float HardKillInterpolate = 0.005f;
    public const float MultiKillCountBlend = 0.1f;
    public const float SpreeBonusPerStack = 0.05f;
    public const byte DefaultMaxLevel = 120;
    public const float MissionRoundBias = 0.5001f;

    internal ICharacterProgressPersistence Persistence { get; set; } = CharacterProgressPersistence.Instance;

    /// <summary>Cumulative XP threshold for a player level (tExperienceLevel.intExperience).</summary>
    internal Func<byte, uint> ResolveThreshold { get; set; }

    /// <summary>Full experience-level row for level-up point grants.</summary>
    internal Func<byte, ExperienceLevel> ResolveLevelRow { get; set; }

    /// <summary>Base kill XP for a creature level (tCreatureExperienceLevel).</summary>
    internal Func<int, int> ResolveCreatureXp { get; set; }

    /// <summary>Mission XP fraction by XPIndex (tQuestXPLookup.rlLevelXP).</summary>
    internal Func<int, float> ResolveQuestFrac { get; set; }

    /// <summary>Mission credit multiplier by CreditsIndex (tQuestCreditsLookup.rlLevelCredits).</summary>
    internal Func<int, float> ResolveQuestCreditsFrac { get; set; }

    /// <summary>Base mission credits by TargetLevel (tQuestBaseCredits.intBaseCredits).</summary>
    internal Func<int, int> ResolveQuestBaseCredits { get; set; }

    /// <summary>Area reward level index (ContinentArea.XPLevel); 0 = none.</summary>
    internal Func<int, byte, int> ResolveAreaXpLevel { get; set; }

    internal byte MaxLevel { get; set; } = DefaultMaxLevel;

    internal float PersonalXpGain { get; set; } = 1.0f;

    /// <summary>When false, GiveXp skips SaveProgress (unit tests without DB).</summary>
    internal bool PersistOnGrant { get; set; } = true;

    /// <summary>When false, skips SendGamePacket (tests without connection).</summary>
    internal bool SendPacketsOnGrant { get; set; } = true;

    /// <summary>Reset injectables between tests.</summary>
    internal void ResetForTests()
    {
        Persistence = CharacterProgressPersistence.Instance;
        ResolveThreshold = null;
        ResolveLevelRow = null;
        ResolveCreatureXp = null;
        ResolveQuestFrac = null;
        ResolveQuestCreditsFrac = null;
        ResolveQuestBaseCredits = null;
        ResolveAreaXpLevel = null;
        MaxLevel = DefaultMaxLevel;
        PersonalXpGain = 1.0f;
        PersistOnGrant = true;
        SendPacketsOnGrant = true;
    }

    /// <summary>
    /// Apply XP, level-up, persist, and build packets.
    /// Order: memory → SaveProgress → GiveXP → CharacterLevel if leveled.
    /// </summary>
    /// <param name="notifyClient">
    /// When false, persists and updates memory only (no GiveXP / CharacterLevel packets).
    /// Use for dialog deliver turn-in where the client already applied local CompleteObjective XP.
    /// </param>
    public GiveXpResult GiveXp(
        Character character,
        int amount,
        XpSource source,
        sbyte levelHint = -1,
        bool notifyClient = true)
    {
        if (character == null)
            return GiveXpResult.Fail("No character.");

        var coid = character.ObjectId.Coid;
        if (coid <= 0)
            return GiveXpResult.Fail($"Invalid character coid ({coid}).");

        if (amount == 0)
        {
            return new GiveXpResult
            {
                Success = true,
                Message = "Zero amount.",
                AppliedAmount = 0,
                TotalExperience = character.Experience,
                Level = character.Level,
                PreviousLevel = character.Level,
                Leveled = false
            };
        }

        var scaled = (int)(amount * PersonalXpGain);
        if (scaled == 0 && amount != 0)
            scaled = amount > 0 ? 1 : -1;

        var previousLevel = character.Level;
        var total = character.Experience;
        var level = previousLevel;

        // Cap: at max level, do not reach next threshold (client +0xc50 behavior simplified).
        if (level >= MaxLevel && scaled > 0)
        {
            var cap = (int)GetThreshold(level);
            if (cap > 0 && total >= cap - 1)
            {
                return new GiveXpResult
                {
                    Success = true,
                    Message = "At max level cap.",
                    AppliedAmount = 0,
                    TotalExperience = total,
                    Level = level,
                    PreviousLevel = previousLevel,
                    Leveled = false
                };
            }

            if (cap > 0 && total + scaled >= cap)
                scaled = Math.Max(0, cap - 1 - total);
            if (scaled == 0)
            {
                return new GiveXpResult
                {
                    Success = true,
                    Message = "At max level cap.",
                    AppliedAmount = 0,
                    TotalExperience = total,
                    Level = level,
                    PreviousLevel = previousLevel,
                    Leveled = false
                };
            }
        }

        total += scaled;
        if (total < 0)
            total = 0;

        var skill = character.SkillPoints;
        var attrib = character.AttributePoints;
        var research = character.ResearchPoints;
        var guard = 0;

        while (level < MaxLevel && guard++ < 300)
        {
            var nextThreshold = (int)GetThreshold(level);
            // Threshold for level L is cumulative XP to *be* level L / finish previous.
            // Client levels up while total >= threshold for *current* level row.
            // Experience[1]=1000 means need 1000 to leave level 1 → reach level 2.
            if (nextThreshold <= 0 || total < nextThreshold)
                break;

            level++;
            var row = GetLevelRow(level);
            if (row != null)
            {
                skill = (short)Math.Min(short.MaxValue, skill + row.SkillPoints);
                attrib = (short)Math.Min(short.MaxValue, attrib + row.AttributePoints);
                research = (short)Math.Min(short.MaxValue, research + row.ResearchPoints);
            }
        }

        // De-level path (negative XP) — simplified mirror of client de-level loop.
        guard = 0;
        while (level > 1 && guard++ < 300)
        {
            var prevThreshold = (int)GetThreshold((byte)(level - 1));
            if (total >= prevThreshold)
                break;
            level--;
        }

        character.SetExperience(total);
        character.SetLevel(level);
        character.SetSkillPoints(skill);
        character.SetAttributePoints(attrib);
        character.SetResearchPoints(research);

        var snapshot = new CharacterProgressSnapshot(level, total, skill, attrib, research);

        if (PersistOnGrant)
        {
            try
            {
                Persistence.SaveProgress(coid, snapshot);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"GiveXp persist failed coid={coid}: {ex.Message}");
                return GiveXpResult.Fail($"Persist failed: {ex.Message}");
            }
        }

        var leveledUp = level != previousLevel;

        GiveXPPacket givePacket = null;
        CharacterLevelPacket levelPacket = null;

        // GiveXP delta only when caller wants client notify (dialog turn-in suppresses this
        // to avoid double-counting client-local CompleteObjective XP).
        if (notifyClient && scaled != 0)
        {
            // When leveling on a notify path, pass new level as hint (client +0x738 flash).
            var hint = levelHint;
            if (leveledUp && hint < 0 && level <= sbyte.MaxValue)
                hint = (sbyte)level;

            givePacket = new GiveXPPacket
            {
                Amount = scaled,
                LevelHint = hint
            };
        }

        // Absolute CharacterLevel: always on notifyClient grants, and ALWAYS when level changes
        // even if XP delta was suppressed (deliver turn-in still needs mid-session level update).
        if (notifyClient || leveledUp)
            levelPacket = BuildCharacterLevelPacket(character);

        if (SendPacketsOnGrant && character.OwningConnection != null)
        {
            if (givePacket != null)
                character.OwningConnection.SendGamePacket(givePacket);
            if (levelPacket != null)
                character.OwningConnection.SendGamePacket(levelPacket);
        }

        Logger.WriteLog(
            LogType.Debug,
            "GiveXp: coid={0} source={1} applied={2} total={3} level={4}->{5} notify={6} leveled={7}",
            coid,
            source,
            scaled,
            total,
            previousLevel,
            level,
            notifyClient,
            leveledUp);

        return new GiveXpResult
        {
            Success = true,
            Message = leveledUp ? $"Granted {scaled} XP (level {previousLevel}->{level})." : $"Granted {scaled} XP.",
            AppliedAmount = scaled,
            TotalExperience = total,
            Level = level,
            PreviousLevel = previousLevel,
            Leveled = leveledUp,
            GiveXpPacket = givePacket,
            CharacterLevelPacket = levelPacket
        };
    }

    /// <summary>Absolute set of cumulative XP; recomputes level from thresholds.</summary>
    public GiveXpResult SetExperienceAbsolute(Character character, int absoluteExperience)
    {
        if (character == null)
            return GiveXpResult.Fail("No character.");

        var coid = character.ObjectId.Coid;
        if (coid <= 0)
            return GiveXpResult.Fail($"Invalid character coid ({coid}).");

        absoluteExperience = Math.Max(0, absoluteExperience);
        var previousXp = character.Experience;
        var previousLevel = character.Level;
        var delta = absoluteExperience - previousXp;

        var level = (byte)1;
        while (level < MaxLevel)
        {
            var th = (int)GetThreshold(level);
            if (th <= 0 || absoluteExperience < th)
                break;
            level++;
        }

        character.SetExperience(absoluteExperience);
        character.SetLevel(level);

        var snapshot = new CharacterProgressSnapshot(
            level,
            absoluteExperience,
            character.SkillPoints,
            character.AttributePoints,
            character.ResearchPoints);

        if (PersistOnGrant)
        {
            try
            {
                Persistence.SaveProgress(coid, snapshot);
            }
            catch (Exception ex)
            {
                return GiveXpResult.Fail($"Persist failed: {ex.Message}");
            }
        }

        var levelPacket = BuildCharacterLevelPacket(character);
        GiveXPPacket givePacket = null;
        if (delta > 0)
            givePacket = new GiveXPPacket { Amount = delta, LevelHint = -1 };

        if (SendPacketsOnGrant && character.OwningConnection != null)
        {
            if (givePacket != null)
                character.OwningConnection.SendGamePacket(givePacket);
            character.OwningConnection.SendGamePacket(levelPacket);
        }

        return new GiveXpResult
        {
            Success = true,
            Message = $"Set experience to {absoluteExperience}.",
            AppliedAmount = delta,
            TotalExperience = absoluteExperience,
            Level = level,
            PreviousLevel = previousLevel,
            Leveled = level != previousLevel,
            GiveXpPacket = givePacket,
            CharacterLevelPacket = levelPacket
        };
    }

    public CharacterLevelPacket BuildCharacterLevelPacket(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var mana = CharacterLevelManager.Instance.GetOrCreate(character.ObjectId.Coid);
        short currentMana;
        short maxMana;
        lock (mana)
        {
            currentMana = mana.CurrentMana;
            maxMana = mana.MaxMana;
        }

        return new CharacterLevelPacket
        {
            CharacterId = character.ObjectId,
            Level = character.Level,
            Experience = character.Experience,
            Currency = character.Credits,
            SkillPoints = character.SkillPoints,
            AttributePoints = character.AttributePoints,
            ResearchPoints = character.ResearchPoints,
            CurrentMana = currentMana,
            MaxMana = maxMana
        };
    }

    /// <summary>
    /// Reload progress from DB into the character and build absolute CharacterLevel (login restore).
    /// Always returns a packet when the character is valid — client create leaves XP at 0 until this.
    /// </summary>
    public CharacterLevelPacket TryCreateLoginRestorePacket(
        Character character,
        ICharacterProgressPersistence persistence = null)
    {
        if (character == null)
            return null;

        var coid = character.ObjectId.Coid;
        if (coid <= 0)
            return null;

        var store = persistence ?? Persistence;
        try
        {
            var loaded = store.LoadProgress(coid);
            character.SetLevel(loaded.Level);
            character.SetExperience(loaded.Experience);
            character.SetSkillPoints(loaded.SkillPoints);
            character.SetAttributePoints(loaded.AttributePoints);
            character.SetResearchPoints(loaded.ResearchPoints);

            Logger.WriteLog(
                LogType.Network,
                $"Login progress loaded: coid={coid} level={loaded.Level} xp={loaded.Experience} " +
                $"(memory now level={character.Level} xp={character.Experience})");
        }
        catch (Exception ex)
        {
            Logger.WriteLog(
                LogType.Error,
                $"Login progress LoadProgress failed coid={coid}: {ex.Message} — using in-memory level={character.Level} xp={character.Experience}");
        }

        // Absolute snapshot. Client handler FUN_00810f00 → apply Experience at packet+0x28.
        return BuildCharacterLevelPacket(character);
    }

    /// <summary>
    /// Push login progress to the client after CreateCharacterExtended.
    /// Create leaves client cumulative XP at 0. Strategy:
    /// 1) GiveXP(total) first — always applies to local player (no TFID); 0→total.
    /// 2) CharacterLevel absolute — sets Level/Currency/Experience/points when TFID resolves;
    ///    Experience field absolute-overwrites to the same total (no double-count).
    /// </summary>
    public void SendLoginProgressToClient(Character character)
    {
        if (character?.OwningConnection == null)
            return;

        var xp = character.Experience;
        if (xp > 0)
        {
            character.OwningConnection.SendGamePacket(new GiveXPPacket
            {
                Amount = xp,
                LevelHint = -1
            });
        }

        var packet = BuildCharacterLevelPacket(character);
        character.OwningConnection.SendGamePacket(packet);

        Logger.WriteLog(
            LogType.Network,
            $"Login progress sent: coid={character.ObjectId.Coid} level={packet.Level} xp={packet.Experience} " +
            $"credits={packet.Currency} giveXpSeed={(xp > 0 ? xp : 0)}");
    }

    // --- Formulas (docs/XP.md) ---

    public int ComputeKillXp(
        byte playerLevel,
        byte victimLevel,
        float xpPercent = 1f,
        float participation = 1f,
        int convoyCount = 0,
        byte spree = 0)
    {
        var mult = xpPercent * participation;
        if (mult <= 0f)
            return 0;

        var eff = LevelDiffBase(playerLevel, victimLevel);
        if (eff <= 0)
            return 0;

        if (convoyCount > 0)
        {
            var blended = eff + (int)(convoyCount * MultiKillCountBlend * eff);
            eff = (int)Math.Ceiling(blended / (double)convoyCount);
        }

        var raw = (int)Math.Ceiling(eff * GlobalKillScalar * mult);
        if (raw < 1)
            return 0;

        var stacks = spree <= 1 ? 0 : spree - 1;
        if (stacks > 0)
            raw += (int)Math.Ceiling(stacks * raw * SpreeBonusPerStack);

        return raw;
    }

    /// <summary>Level-difference base from tCreatureExperienceLevel (FUN_004c9800).</summary>
    public int LevelDiffBase(byte playerLevel, byte victimLevel)
    {
        int p = playerLevel;
        int v = victimLevel;

        // Prep clamp: high side of pair within low+3
        if (v - p > 3)
            v = p + 3;
        if (p - v > 3)
        {
            // For grey path the kill function clamps the higher arg; keep victim for table lookup.
        }

        var diff = p - v;

        if (diff >= GreyLevelDiff)
            return 0;

        if (diff >= 0)
        {
            var baseXp = GetCreatureXp(v);
            if (baseXp <= 0)
                return 0;
            if (diff == 0)
                return baseXp;

            var adj = (int)Math.Round(diff * GreySlope * baseXp);
            var result = baseXp + adj;
            return result < 0 ? 0 : result;
        }

        // Hard kill: victim higher — boost lookup level (diff clamped around -9)
        var hardDiff = diff < -9 ? -9 : diff;
        var boosted = v; // use victim level table row
        var baseHard = GetCreatureXp(boosted);
        if (baseHard <= 0)
            return 0;

        var extra = Math.Abs(hardDiff);
        var bonus = (int)(extra * baseHard * HardKillInterpolate);
        return baseHard + bonus;
    }

    public int ComputeMissionXp(Mission mission, MissionObjective objective)
    {
        if (mission == null || objective == null)
            return 0;

        var frac = GetQuestFrac(objective.XPIndex);
        if (frac <= 0f && objective.XP != 0 && objective.XPIndex == 0)
            return objective.XP; // optional static fallback

        var spanMult = objective.XPBalanceScaler * frac * objective.XPScaler;
        if (spanMult <= 0f)
            return 0;

        var L = mission.TargetLevel;
        if (L < 1)
            L = 1;

        var levelSpan = LevelSpan((byte)Math.Min(L, byte.MaxValue));
        if (levelSpan <= 0)
            return 0;

        var raw = levelSpan * spanMult;
        // DAT_00aaa6d0 ≈ 0.5001 nearest-int style
        return (int)Math.Floor(raw + MissionRoundBias);
    }

    /// <summary>
    /// Mission credit grant (client FUN_0059DF20):
    /// ceil(CreditScaler * tQuestCreditsLookup[CreditsIndex] * tQuestBaseCredits[TargetLevel]).
    /// Static <see cref="MissionObjective.Credits"/> is a fallback when CreditsIndex is 0.
    /// </summary>
    public int ComputeMissionCredits(Mission mission, MissionObjective objective)
    {
        if (mission == null || objective == null)
            return 0;

        var frac = GetQuestCreditsFrac(objective.CreditsIndex);
        if (frac <= 0f && objective.Credits != 0 && objective.CreditsIndex == 0)
            return objective.Credits;

        var L = mission.TargetLevel;
        if (L < 1)
            L = 1;

        var bas = GetQuestBaseCredits(L);
        if (bas <= 0 || frac <= 0f)
            return 0;

        var raw = objective.CreditScaler * frac * bas;
        if (raw <= 0f)
            return 0;

        // Client: ceil then ROUND for positive values → ceiling to int.
        return (int)Math.Ceiling(raw);
    }

    public int ComputeAreaXp(int continentId, byte areaId)
    {
        var xpLevel = GetAreaXpLevel(continentId, areaId);
        if (xpLevel <= 0)
            return 0;
        return GetCreatureXp(xpLevel);
    }

    public int LevelSpan(byte level)
    {
        if (level <= 1)
            return (int)GetThreshold(1);

        var cur = (int)GetThreshold(level);
        var prev = (int)GetThreshold((byte)(level - 1));
        var span = cur - prev;
        return span < 0 ? 0 : span;
    }

    public uint GetThreshold(byte level)
    {
        if (ResolveThreshold != null)
            return ResolveThreshold(level);

        try
        {
            var fromAssets = AssetManager.Instance.GetExperienceThreshold(level);
            if (fromAssets > 0)
                return fromAssets;
        }
        catch
        {
            // Asset manager not initialized in unit tests.
        }

        return DefaultRetailThreshold(level);
    }

    private ExperienceLevel GetLevelRow(byte level)
    {
        if (ResolveLevelRow != null)
            return ResolveLevelRow(level);

        try
        {
            return AssetManager.Instance.GetExperienceLevel(level);
        }
        catch
        {
            return null;
        }
    }

    public int GetCreatureXp(int creatureLevel)
    {
        if (ResolveCreatureXp != null)
            return ResolveCreatureXp(creatureLevel);

        try
        {
            var fromAssets = AssetManager.Instance.GetCreatureExperience(creatureLevel);
            if (fromAssets > 0)
                return fromAssets;
        }
        catch
        {
            // Asset manager not initialized in unit tests.
        }

        return DefaultCreatureXp(creatureLevel);
    }

    public float GetQuestFrac(int index)
    {
        if (ResolveQuestFrac != null)
            return ResolveQuestFrac(index);

        try
        {
            var fromAssets = AssetManager.Instance.GetQuestXpFraction(index);
            if (fromAssets > 0f)
                return fromAssets;
        }
        catch
        {
            // Asset manager not initialized in unit tests.
        }

        return DefaultQuestFrac(index);
    }

    public float GetQuestCreditsFrac(int index)
    {
        if (ResolveQuestCreditsFrac != null)
            return ResolveQuestCreditsFrac(index);

        try
        {
            var fromAssets = AssetManager.Instance.GetQuestCreditsFraction(index);
            if (fromAssets > 0f)
                return fromAssets;
        }
        catch
        {
            // Asset manager not initialized in unit tests.
        }

        return DefaultQuestCreditsFrac(index);
    }

    public int GetQuestBaseCredits(int targetLevel)
    {
        if (ResolveQuestBaseCredits != null)
            return ResolveQuestBaseCredits(targetLevel);

        try
        {
            var fromAssets = AssetManager.Instance.GetQuestBaseCredits(targetLevel);
            if (fromAssets > 0)
                return fromAssets;
        }
        catch
        {
            // Asset manager not initialized in unit tests.
        }

        return DefaultQuestBaseCredits(targetLevel);
    }

    public int GetAreaXpLevel(int continentId, byte areaId)
    {
        if (ResolveAreaXpLevel != null)
            return ResolveAreaXpLevel(continentId, areaId);

        try
        {
            return AssetManager.Instance.GetContinentAreaXpLevel(continentId, areaId);
        }
        catch
        {
            return 0;
        }
    }

    // Retail wad samples (docs/XP.md) — used when tables not injected/loaded.
    internal static uint DefaultRetailThreshold(byte level) => level switch
    {
        0 => 0,
        1 => 1000,
        2 => 3300,
        3 => 5600,
        4 => 8800,
        5 => 12000,
        6 => 16000,
        7 => 20000,
        8 => 26000,
        9 => 32000,
        10 => 39000,
        _ => (uint)(1000 + (level - 1) * 2500) // rough fallback past sample table
    };

    internal static int DefaultCreatureXp(int level) => level switch
    {
        <= 0 => 38,
        1 => 39,
        2 => 40,
        3 => 41,
        4 => 42,
        5 => 45,
        6 => 50,
        7 => 54,
        20 => 112,
        50 => 263,
        _ => 38 + Math.Max(0, level)
    };

    internal static float DefaultQuestFrac(int index) => index switch
    {
        0 => 0f,
        1 => 0.02f,
        2 => 0.04f,
        3 => 0.06f,
        4 => 0.08f,
        5 => 0.10f,
        6 => 0.15f,
        7 => 0.20f,
        8 => 0.25f,
        9 => 0.30f,
        _ => 0f
    };

    /// <summary>Retail tQuestCreditsLookup samples (rlLevelCredits).</summary>
    internal static float DefaultQuestCreditsFrac(int index) => index switch
    {
        0 => 0f,
        1 => 0.2f,
        2 => 0.4f,
        3 => 0.6f,
        4 => 0.8f,
        5 => 1.0f,
        6 => 1.2f,
        7 => 1.4f,
        8 => 1.6f,
        9 => 1.8f,
        10 => 2.0f,
        _ => 0f
    };

    /// <summary>Retail tQuestBaseCredits samples (intBaseCredits by TargetLevel).</summary>
    internal static int DefaultQuestBaseCredits(int targetLevel) => targetLevel switch
    {
        <= 0 => 0,
        1 => 3,
        2 => 10,
        3 => 16,
        4 => 25,
        5 => 34,
        6 => 47,
        7 => 59,
        8 => 78,
        9 => 97,
        _ => 3 + Math.Max(0, targetLevel - 1) * 10 // rough fallback past samples
    };
}
