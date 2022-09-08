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

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CharacterExploration>().HasKey(ce => new { ce.CharacterCoid, ce.ContinentId });
        modelBuilder.Entity<CharacterSocial>().HasKey(cs => new { cs.CharacterCoid, cs.TargetCoid });
        modelBuilder.Entity<ClanMember>().HasKey(cm => new { cm.ClanId, cm.CharacterCoid });
    }
}
