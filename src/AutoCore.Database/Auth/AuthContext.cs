using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace AutoCore.Database.Auth;

using AutoCore.Database.Auth.Models;

public class AuthContext : DbContext
{
    public static string ConnectionString { get; private set; } = string.Empty;

    public DbSet<Account> Accounts { get; set; }
    public DbSet<GlobalServer> GlobalServers { get; set; }

    public AuthContext()
    {
        Accounts = Set<Account>();
        GlobalServers = Set<GlobalServer>();
    }

    public static void InitializeConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        if (!string.IsNullOrEmpty(ConnectionString))
            throw new ArgumentException("The data source is already set up for the AuthContext!", nameof(connectionString));

        ConnectionString = connectionString;
    }

    public static void EnsureCreated()
    {
        using var context = new AuthContext();
        context.Database.EnsureCreated();
        SeedDefaultAccount(context);
    }

    private static void SeedDefaultAccount(AuthContext context)
    {
        // Only create default account if no accounts exist
        if (!context.Accounts.Any())
        {
            var salt = Account.CreateSalt();
            var defaultPassword = "admin123"; // Default password - should be changed after first login
            
            context.Accounts.Add(new Account
            {
                Email = "admin@autocore.local",
                Username = "admin",
                Password = Account.Hash(defaultPassword, salt),
                Salt = salt,
                Level = 255, // Admin level
                JoinDate = DateTime.Now,
                Validated = true,
                Locked = false
            });
            
            context.SaveChanges();
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));
}
