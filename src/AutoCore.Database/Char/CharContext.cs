using Microsoft.EntityFrameworkCore;

namespace AutoCore.Database.Char;

using AutoCore.Database.Char.Models;

public class CharContext : DbContext
{
    private static readonly string[] MissionCompatibilityMigrationSql =
    {
        """
        INSERT IGNORE INTO `character_mission_completed` (`CharacterCoid`, `MissionId`)
        SELECT `CharacterCoid`, `MissionId` FROM `character_completed_mission`
        """,
        """
        INSERT IGNORE INTO `character_mission`
            (`CharacterCoid`, `MissionId`, `ActiveObjectiveSequence`, `State`, `ObjectiveProgress`)
        SELECT legacy.`CharacterCoid`, legacy.`MissionId`, legacy.`ActiveObjectiveSequence`,
               legacy.`State`, legacy.`ObjectiveProgress`
        FROM `character_quest` legacy
        LEFT JOIN `character_mission_completed` completed
          ON completed.`CharacterCoid` = legacy.`CharacterCoid`
         AND completed.`MissionId` = legacy.`MissionId`
        WHERE completed.`MissionId` IS NULL
        """,
        """
        DELETE active
        FROM `character_mission` active
        INNER JOIN `character_mission_completed` completed
          ON completed.`CharacterCoid` = active.`CharacterCoid`
         AND completed.`MissionId` = active.`MissionId`
        """,
    };

    public static string ConnectionString { get; private set; }

    public DbSet<Account> Accounts { get; set; }
    public DbSet<CharacterData> Characters { get; set; }
    public DbSet<CharacterExploration> CharacterExplorations { get; set; }
    public DbSet<CharacterQuestData> CharacterQuests { get; set; }
    public DbSet<CharacterCompletedMissionData> CharacterCompletedMissions { get; set; }
    public DbSet<CharacterSocial> CharacterSocials { get; set; }
    public DbSet<CharacterInventoryData> CharacterInventories { get; set; }
    public DbSet<CharacterLearnedSkillData> CharacterLearnedSkills { get; set; }
    public DbSet<CharacterQuickBarSlotData> CharacterQuickBarSlots { get; set; }
    public DbSet<VehicleData> Vehicles { get; set; }
    public DbSet<Clan> Clans { get; set; }
    public DbSet<ClanMember> ClanMembers { get; set; }
    public DbSet<SimpleObjectData> SimpleObjects { get; set; }

    public CharContext()
    {
    }

    /// <summary>Options-based constructor for unit tests (InMemory / SQLite) without MySQL.</summary>
    public CharContext(DbContextOptions<CharContext> options)
        : base(options)
    {
    }

    public static void InitializeConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentNullException(nameof(connectionString));

        if (!string.IsNullOrEmpty(ConnectionString))
            throw new ArgumentException("The data source is already set up for the CharContext!", nameof(connectionString));

        ConnectionString = connectionString;
    }

    public static void EnsureCreated()
    {
        using var context = new CharContext();
        context.Database.EnsureCreated();
        context.EnsureInventorySchema();
        context.EnsureCharacterEconomySchema();
        context.EnsureCharacterProgressSchema();
        context.EnsureMissionSchema();
        context.EnsureSkillSchema();
    }

    /// <summary>
    /// Adds inventory columns/tables to existing MySQL databases created before cargo persistence.
    /// Safe to call repeatedly (ignores duplicate-column / already-exists errors).
    /// </summary>
    public void EnsureInventorySchema()
    {
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `CargoWidth` INT NOT NULL DEFAULT 24
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `CargoPageCount` INT NOT NULL DEFAULT 13
            """);
        TryExecute("""
            CREATE TABLE IF NOT EXISTS `character_inventory` (
                `Id` BIGINT NOT NULL AUTO_INCREMENT,
                `CharacterCoid` BIGINT NOT NULL,
                `ItemCoid` BIGINT NOT NULL,
                `Cbid` INT NOT NULL,
                `Type` TINYINT UNSIGNED NOT NULL,
                `SlotX` TINYINT UNSIGNED NOT NULL,
                `SlotY` TINYINT UNSIGNED NOT NULL,
                `Quantity` INT NOT NULL DEFAULT 1,
                PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_character_inventory_ItemCoid` (`ItemCoid`),
                KEY `IX_character_inventory_CharacterCoid` (`CharacterCoid`)
            )
            """);
    }

    /// <summary>
    /// Adds currency columns for existing character DBs created before economy persistence.
    /// </summary>
    public void EnsureCharacterEconomySchema()
    {
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `Credits` BIGINT NOT NULL DEFAULT 0
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `CreditDebt` BIGINT NOT NULL DEFAULT 0
            """);
    }

    /// <summary>
    /// Adds XP / level-up pool columns for existing character DBs (docs/XP.md).
    /// Safe to call repeatedly.
    /// </summary>
    public void EnsureCharacterProgressSchema()
    {
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `Experience` INT NOT NULL DEFAULT 0
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `SkillPoints` SMALLINT NOT NULL DEFAULT 0
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `AttributePoints` SMALLINT NOT NULL DEFAULT 0
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `ResearchPoints` SMALLINT NOT NULL DEFAULT 0
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `AttributeTech` SMALLINT NOT NULL DEFAULT 0
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `AttributeCombat` SMALLINT NOT NULL DEFAULT 0
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `AttributeTheory` SMALLINT NOT NULL DEFAULT 0
            """);
        TryExecute("""
            ALTER TABLE `character`
            ADD COLUMN `AttributePerception` SMALLINT NOT NULL DEFAULT 0
            """);
    }

    /// <summary>
    /// Adds mission-persistence tables to existing character DBs. Safe to call repeatedly.
    /// </summary>
    public void EnsureMissionSchema()
    {
        TryExecute("""
            CREATE TABLE IF NOT EXISTS `character_mission` (
                `CharacterCoid` BIGINT NOT NULL,
                `MissionId` INT NOT NULL,
                `ActiveObjectiveSequence` TINYINT UNSIGNED NOT NULL DEFAULT 0,
                `State` TINYINT UNSIGNED NOT NULL DEFAULT 0,
                `ObjectiveProgress` LONGBLOB NULL,
                PRIMARY KEY (`CharacterCoid`, `MissionId`),
                KEY `IX_character_mission_CharacterCoid` (`CharacterCoid`)
            )
            """);
        TryExecute("""
            CREATE TABLE IF NOT EXISTS `character_mission_completed` (
                `CharacterCoid` BIGINT NOT NULL,
                `MissionId` INT NOT NULL,
                PRIMARY KEY (`CharacterCoid`, `MissionId`),
                KEY `IX_character_mission_completed_CharacterCoid` (`CharacterCoid`)
            )
            """);

        foreach (var sql in MissionCompatibilityMigrationSql)
            TryExecute(sql);
    }

    public void EnsureSkillSchema()
    {
        TryExecute("""
            CREATE TABLE IF NOT EXISTS `character_learned_skill` (
                `CharacterCoid` BIGINT NOT NULL, `SkillId` INT NOT NULL, `Rank` TINYINT UNSIGNED NOT NULL,
                PRIMARY KEY (`CharacterCoid`, `SkillId`)
            )
            """);
        TryExecute("""
            CREATE TABLE IF NOT EXISTS `character_quickbar` (
                `CharacterCoid` BIGINT NOT NULL, `Slot` TINYINT UNSIGNED NOT NULL,
                `ItemCoid` BIGINT NOT NULL DEFAULT -1, `SkillId` INT NOT NULL DEFAULT 0,
                PRIMARY KEY (`CharacterCoid`, `Slot`)
            )
            """);
    }

    private void TryExecute(string sql)
    {
        try
        {
            Database.ExecuteSqlRaw(sql);
        }
        catch
        {
            // Column/table already exists on upgraded databases.
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        // Skip when constructed with DbContextOptions (unit tests inject InMemory/SQLite).
        if (options.IsConfigured)
            return;

        options.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CharacterLearnedSkillData>()
            .HasKey(x => new { x.CharacterCoid, x.SkillId });
        modelBuilder.Entity<CharacterQuickBarSlotData>()
            .HasKey(x => new { x.CharacterCoid, x.Slot });

        modelBuilder.Entity<CharacterExploration>().HasKey(ce => new { ce.CharacterCoid, ce.ContinentId });
        modelBuilder.Entity<CharacterQuestData>().HasKey(cq => new { cq.CharacterCoid, cq.MissionId });
        modelBuilder.Entity<CharacterCompletedMissionData>().HasKey(cm => new { cm.CharacterCoid, cm.MissionId });
        modelBuilder.Entity<CharacterSocial>().HasKey(cs => new { cs.CharacterCoid, cs.TargetCoid });
        modelBuilder.Entity<ClanMember>().HasKey(cm => new { cm.ClanId, cm.CharacterCoid });
        modelBuilder.Entity<CharacterInventoryData>()
            .HasIndex(ci => ci.ItemCoid)
            .IsUnique();
        modelBuilder.Entity<CharacterInventoryData>()
            .HasIndex(ci => ci.CharacterCoid);
    }
}
