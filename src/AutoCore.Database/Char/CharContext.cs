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
    public DbSet<CharacterInventoryData> CharacterInventories { get; set; }
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
        context.EnsureInventorySchema();
        context.EnsureCharacterEconomySchema();
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

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CharacterExploration>().HasKey(ce => new { ce.CharacterCoid, ce.ContinentId });
        modelBuilder.Entity<CharacterSocial>().HasKey(cs => new { cs.CharacterCoid, cs.TargetCoid });
        modelBuilder.Entity<ClanMember>().HasKey(cm => new { cm.ClanId, cm.CharacterCoid });
        modelBuilder.Entity<CharacterInventoryData>()
            .HasIndex(ci => ci.ItemCoid)
            .IsUnique();
        modelBuilder.Entity<CharacterInventoryData>()
            .HasIndex(ci => ci.CharacterCoid);
    }
}
