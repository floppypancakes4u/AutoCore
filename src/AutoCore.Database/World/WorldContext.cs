using System;

using Microsoft.EntityFrameworkCore;

namespace AutoCore.Database.World
{
    using Models;

    public class WorldContext : DbContext
    {
        public static string ConnectionString { get; private set; }

        public DbSet<ExperienceLevel> ExperienceLevels { get; set; }

        public WorldContext()
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
}
