using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.License.Server.Data;

namespace x3squaredcircles.License.Server.Services
{
    public interface IDatabaseMigrationService
    {
        Task MigrateAsync();
        Task<string> GetCurrentSchemaVersionAsync();
        Task<bool> IsUpgradeRequiredAsync();
    }

    public class DatabaseMigrationService : IDatabaseMigrationService
    {
        private readonly LicenseDbContext _context;
        private readonly ILogger<DatabaseMigrationService> _logger;
        private readonly string _targetSchemaVersion;
        private readonly string _dataPath = "/data";

        public DatabaseMigrationService(LicenseDbContext context, ILogger<DatabaseMigrationService> logger)
        {
            _context = context;
            _logger = logger;
            _targetSchemaVersion = GetEmbeddedSchemaVersion();
        }

        public async Task MigrateAsync()
        {
            _logger.LogInformation("Starting database migration process");

            try
            {
                // Ensure database exists
                await _context.Database.EnsureCreatedAsync();

                // Check if migration is needed
                var currentVersion = await GetCurrentSchemaVersionAsync();
                _logger.LogInformation("Current schema version: {CurrentVersion}, Target: {TargetVersion}",
                    currentVersion, _targetSchemaVersion);

                if (currentVersion == _targetSchemaVersion)
                {
                    _logger.LogInformation("Database schema is up to date");
                    return;
                }

                // Backup current database before migration
                await BackupDatabaseAsync();

                // Run Entity Framework migrations
                await RunEntityFrameworkMigrationsAsync();

                // Run custom schema migrations if needed
                await RunCustomMigrationsAsync(currentVersion, _targetSchemaVersion);

                // Update schema version
                await UpdateSchemaVersionAsync(_targetSchemaVersion);

                // Clean up old backups
                await CleanupOldBackupsAsync();

                _logger.LogInformation("Database migration completed successfully to version {Version}", _targetSchemaVersion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database migration failed");
                await RestoreDatabaseAsync();
                throw;
            }
        }

        public async Task<string> GetCurrentSchemaVersionAsync()
        {
            try
            {
                // Check if schema_version table exists
                var hasVersionTable = await _context.Database.ExecuteSqlRawAsync(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'") > 0;

                if (!hasVersionTable)
                {
                    // Create schema_version table
                    await _context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS schema_version (
                            id INTEGER PRIMARY KEY,
                            version TEXT NOT NULL,
                            applied_at DATETIME NOT NULL
                        )");

                    // Insert initial version
                    await _context.Database.ExecuteSqlRawAsync(
                        "INSERT INTO schema_version (version, applied_at) VALUES ('1.0.0', datetime('now'))");

                    return "1.0.0";
                }

                // Get current version
                using var command = _context.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT version FROM schema_version ORDER BY id DESC LIMIT 1";

                await _context.Database.OpenConnectionAsync();
                var result = await command.ExecuteScalarAsync();

                return result?.ToString() ?? "1.0.0";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get current schema version");
                return "1.0.0";
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        public async Task<bool> IsUpgradeRequiredAsync()
        {
            try
            {
                var currentVersion = await GetCurrentSchemaVersionAsync();
                return currentVersion != _targetSchemaVersion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if upgrade is required");
                return false;
            }
        }

        private async Task BackupDatabaseAsync()
        {
            try
            {
                var dbPath = Path.Combine(_dataPath, "license.db");
                if (!File.Exists(dbPath))
                {
                    _logger.LogDebug("No existing database to backup");
                    return;
                }

                var backupPath = Path.Combine(_dataPath, $"license_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");
                File.Copy(dbPath, backupPath);

                _logger.LogInformation("Database backed up to: {BackupPath}", backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backup database");
                throw;
            }
        }

        private async Task RunEntityFrameworkMigrationsAsync()
        {
            try
            {
                _logger.LogInformation("Running Entity Framework migrations");
                await _context.Database.MigrateAsync();
                _logger.LogInformation("Entity Framework migrations completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Entity Framework migrations failed");
                throw;
            }
        }

        private async Task RunCustomMigrationsAsync(string fromVersion, string toVersion)
        {
            try
            {
                _logger.LogInformation("Running custom migrations from {FromVersion} to {ToVersion}", fromVersion, toVersion);

                // Add custom migration logic here if needed
                // For example, data transformations that can't be handled by EF migrations

                if (fromVersion == "1.0.0" && toVersion == "1.1.0")
                {
                    await MigrateFrom1_0_0To1_1_0Async();
                }

                _logger.LogInformation("Custom migrations completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom migrations failed");
                throw;
            }
        }

        private async Task MigrateFrom1_0_0To1_1_0Async()
        {
            // Example custom migration
            _logger.LogDebug("Running migration from 1.0.0 to 1.1.0");

            // Add any custom SQL or data transformation logic here
            // For example:
            // await _context.Database.ExecuteSqlRawAsync("ALTER TABLE active_sessions ADD COLUMN new_field TEXT");
        }

        private async Task UpdateSchemaVersionAsync(string version)
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO schema_version (version, applied_at) VALUES ({0}, datetime('now'))",
                    version);

                _logger.LogInformation("Schema version updated to: {Version}", version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update schema version");
                throw;
            }
        }

        private async Task RestoreDatabaseAsync()
        {
            try
            {
                _logger.LogWarning("Attempting to restore database from backup");

                var backupFiles = Directory.GetFiles(_dataPath, "license_backup_*.db");
                if (backupFiles.Length == 0)
                {
                    _logger.LogError("No backup files found for restoration");
                    return;
                }

                // Use the most recent backup
                Array.Sort(backupFiles);
                var latestBackup = backupFiles[^1];

                var dbPath = Path.Combine(_dataPath, "license.db");
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }

                File.Copy(latestBackup, dbPath);
                _logger.LogInformation("Database restored from backup: {BackupPath}", latestBackup);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore database from backup");
            }
        }

        private async Task CleanupOldBackupsAsync()
        {
            try
            {
                var backupFiles = Directory.GetFiles(_dataPath, "license_backup_*.db");

                // Keep only the 5 most recent backups
                if (backupFiles.Length > 5)
                {
                    Array.Sort(backupFiles);
                    for (int i = 0; i < backupFiles.Length - 5; i++)
                    {
                        File.Delete(backupFiles[i]);
                        _logger.LogDebug("Deleted old backup: {BackupPath}", backupFiles[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup old backups");
            }
        }

        private string GetEmbeddedSchemaVersion()
        {
            // Get schema version from assembly version or environment variable
            var envVersion = Environment.GetEnvironmentVariable("LC_SCHEMA_VERSION");
            if (!string.IsNullOrEmpty(envVersion))
            {
                return envVersion;
            }

            // Fallback to assembly version
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString(3) ?? "1.0.0"; // Major.Minor.Patch
        }
    }
}