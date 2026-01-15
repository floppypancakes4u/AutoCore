using Microsoft.EntityFrameworkCore;

namespace AutoCore.Database.Char;

using AutoCore.Database.Char.Models;

public class CharContext : DbContext
{
    public static string ConnectionString { get; private set; }

    public DbSet<Account> Accounts { get; set; }
    public DbSet<CharacterData> Characters { get; set; }
    public DbSet<CharacterExploration> CharacterExplorations { get; set; }
    public DbSet<CharacterSocial> CharacterSocials { get; set; }
    public DbSet<CharacterStatsData> CharacterStats { get; set; }
    public DbSet<VehicleData> Vehicles { get; set; }
    public DbSet<Clan> Clans { get; set; }
    public DbSet<ClanMember> ClanMembers { get; set; }
    public DbSet<SimpleObjectData> SimpleObjects { get; set; }

    public CharContext()
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
        EnsureCharacterStatsSchema(context);
    }

    public static void EnsureCharacterStatsSchema(CharContext context)
    {
        try
        {
            // Try to create table - IF NOT EXISTS handles existing tables gracefully
            context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS `character_stats` (
                    `CharacterCoid` BIGINT NOT NULL PRIMARY KEY,
                    `Currency` BIGINT NOT NULL DEFAULT 0,
                    `Experience` INT NOT NULL DEFAULT 0,
                    `CurrentMana` SMALLINT NOT NULL DEFAULT 100,
                    `MaxMana` SMALLINT NOT NULL DEFAULT 100,
                    `AttributeTech` SMALLINT NOT NULL DEFAULT 0,
                    `AttributeCombat` SMALLINT NOT NULL DEFAULT 0,
                    `AttributeTheory` SMALLINT NOT NULL DEFAULT 0,
                    `AttributePerception` SMALLINT NOT NULL DEFAULT 0,
                    `AttributePoints` SMALLINT NOT NULL DEFAULT 0,
                    `SkillPoints` SMALLINT NOT NULL DEFAULT 0,
                    `ResearchPoints` SMALLINT NOT NULL DEFAULT 0,
                    CONSTRAINT `FK_character_stats_character` FOREIGN KEY (`CharacterCoid`) REFERENCES `character`(`Coid`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            ");

            // Try to add missing columns if table already existed
            // MySQL doesn't support IF NOT EXISTS for ALTER TABLE, so we catch errors
            var alterStatements = new Dictionary<string, string>
            {
                { "Currency", "BIGINT NOT NULL DEFAULT 0" },
                { "Experience", "INT NOT NULL DEFAULT 0" },
                { "CurrentMana", "SMALLINT NOT NULL DEFAULT 100" },
                { "MaxMana", "SMALLINT NOT NULL DEFAULT 100" },
                { "AttributeTech", "SMALLINT NOT NULL DEFAULT 0" },
                { "AttributeCombat", "SMALLINT NOT NULL DEFAULT 0" },
                { "AttributeTheory", "SMALLINT NOT NULL DEFAULT 0" },
                { "AttributePerception", "SMALLINT NOT NULL DEFAULT 0" },
                { "AttributePoints", "SMALLINT NOT NULL DEFAULT 0" },
                { "SkillPoints", "SMALLINT NOT NULL DEFAULT 0" },
                { "ResearchPoints", "SMALLINT NOT NULL DEFAULT 0" }
            };

            foreach (var col in alterStatements)
            {
                try
                {
                    context.Database.ExecuteSqlRaw($"ALTER TABLE `character_stats` ADD COLUMN `{col.Key}` {col.Value}");
                }
                catch
                {
                    // Column already exists, ignore
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail startup - table might already exist with different schema
            System.Diagnostics.Debug.WriteLine($"Warning: Could not ensure character_stats schema: {ex.Message}");
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CharacterExploration>().HasKey(ce => new { ce.CharacterCoid, ce.ContinentId });
        modelBuilder.Entity<CharacterSocial>().HasKey(cs => new { cs.CharacterCoid, cs.TargetCoid });
        modelBuilder.Entity<ClanMember>().HasKey(cm => new { cm.ClanId, cm.CharacterCoid });
    }
}
