using Microsoft.EntityFrameworkCore;

namespace AutoCore.Database.Auth;

using AutoCore.Database.Auth.Models;

public class AuthContext : DbContext
{
    public static string ConnectionString { get; private set; }

    public DbSet<Account> Accounts { get; set; }

    public AuthContext()
    {
    }

    public static void InitializeConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentNullException(nameof(connectionString));

        if (!string.IsNullOrEmpty(ConnectionString))
            throw new ArgumentException("The data source is already set up for the AuthContext!", nameof(connectionString));

        ConnectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));
}
