using Microsoft.EntityFrameworkCore;
using x3squaredcircles.License.Server.Models;

namespace x3squaredcircles.License.Server.Data
{
    /// <summary>
    /// The Entity Framework DbContext for the Licensing Container.
    /// Manages all interactions with the local license.db SQLite database.
    /// </summary>
    public class LicenseDbContext : DbContext
    {
        public LicenseDbContext(DbContextOptions<LicenseDbContext> options) : base(options)
        {
        }

        /// <summary>
        /// Stores the single, authoritative license configuration for this container instance.
        /// </summary>
        public DbSet<LicenseConfig> LicenseConfigs { get; set; }

        /// <summary>
        /// Tracks burst usage over rolling 92-day periods.
        /// </summary>
        public DbSet<QuarterlyUsage> QuarterlyUsages { get; set; }

        /// <summary>
        /// Stores all currently active, real-time tool execution sessions.
        /// </summary>
        public DbSet<ActiveSession> ActiveSessions { get; set; }

        /// <summary>
        /// Stores the daily snapshot of the total contributor count for the annual true-up.
        /// </summary>
        public DbSet<DailyContributorHistory> DailyContributorHistories { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // -----------------------------------------------------------------
            // LicenseConfig Table Configuration
            // -----------------------------------------------------------------
            modelBuilder.Entity<LicenseConfig>(entity =>
            {
                entity.ToTable("license_config");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.MaxConcurrent).IsRequired();
                entity.Property(e => e.ToolsLicensed).IsRequired();
                entity.Property(e => e.BurstMultiplier).HasDefaultValue(2);
                entity.Property(e => e.BurstAllowancePerQuarter).HasDefaultValue(2);
            });

            // -----------------------------------------------------------------
            // QuarterlyUsage Table Configuration
            // -----------------------------------------------------------------
            modelBuilder.Entity<QuarterlyUsage>(entity =>
            {
                entity.ToTable("quarterly_usage");
                entity.HasKey(e => e.QuarterId);
                entity.Property(e => e.QuarterId).HasMaxLength(10); // Format: "YYYY-MM-DD"
                entity.Property(e => e.BurstEventsUsed).HasDefaultValue(0);
                entity.Property(e => e.QuarterStartDate).IsRequired();
            });

            // -----------------------------------------------------------------
            // ActiveSession Table Configuration
            // -----------------------------------------------------------------
            modelBuilder.Entity<ActiveSession>(entity =>
            {
                entity.ToTable("active_sessions");
                entity.HasKey(e => e.SessionId);
                entity.Property(e => e.SessionId).HasMaxLength(50);
                entity.Property(e => e.ToolName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ToolVersion).IsRequired().HasMaxLength(20);
                entity.Property(e => e.StartTime).IsRequired();
                entity.Property(e => e.LastHeartbeat).IsRequired();
                entity.Property(e => e.IpAddress).HasMaxLength(45); // Supports IPv6
                entity.Property(e => e.BuildId).HasMaxLength(50);

                entity.HasIndex(e => e.LastHeartbeat);
            });

            // -----------------------------------------------------------------
            // DailyContributorHistory Table Configuration
            // -----------------------------------------------------------------
            modelBuilder.Entity<DailyContributorHistory>(entity =>
            {
                entity.ToTable("daily_contributor_history");
                entity.HasKey(e => e.Date);
                entity.Property(e => e.Date).HasMaxLength(10); // Format: "YYYY-MM-DD"
                entity.Property(e => e.ContributorCount).IsRequired();
            });
        }
    }
}