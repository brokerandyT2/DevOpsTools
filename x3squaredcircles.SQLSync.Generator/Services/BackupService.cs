using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IBackupService
    {
        Task CreateBackupAsync(SqlSchemaConfiguration config);
        Task<bool> VerifyBackupAsync(SqlSchemaConfiguration config, string backupPath);
        Task<List<string>> ListBackupsAsync(SqlSchemaConfiguration config);
        Task RestoreBackupAsync(SqlSchemaConfiguration config, string backupPath);
        Task CleanupOldBackupsAsync(SqlSchemaConfiguration config);
    }

    public class BackupService : IBackupService
    {
        private readonly IDatabaseProviderFactory _databaseProviderFactory;
        private readonly ILogger<BackupService> _logger;

        public BackupService(IDatabaseProviderFactory databaseProviderFactory, ILogger<BackupService> logger)
        {
            _databaseProviderFactory = databaseProviderFactory;
            _logger = logger;
        }

        public async Task CreateBackupAsync(SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Creating backup for database: {DatabaseName}", config.Database.DatabaseName);

                var provider = config.Database.GetSelectedProvider();
                var databaseProvider = await _databaseProviderFactory.GetProviderAsync(provider);

                // Generate backup path and filename
                var backupInfo = GenerateBackupInfo(config);

                // Create backup directory if it doesn't exist
                EnsureBackupDirectoryExists(backupInfo.BackupDirectory);

                // Execute provider-specific backup
                await ExecuteProviderBackupAsync(provider, config, backupInfo, databaseProvider);

                // Verify backup was created successfully
                if (await VerifyBackupAsync(config, backupInfo.BackupPath))
                {
                    _logger.LogInformation("✓ Backup created successfully: {BackupPath}", backupInfo.BackupPath);

                    // Log backup metadata
                    await LogBackupMetadataAsync(config, backupInfo);
                }
                else
                {
                    throw new InvalidOperationException($"Backup verification failed for: {backupInfo.BackupPath}");
                }

                // Clean up old backups if retention policy is configured
                if (config.Backup.RetentionDays > 0)
                {
                    await CleanupOldBackupsAsync(config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create backup for database: {DatabaseName}", config.Database.DatabaseName);
                throw new SqlSchemaException(SqlSchemaExitCode.DatabaseConnectionFailure,
                    $"Failed to create database backup: {ex.Message}", ex);
            }
        }

        public async Task<bool> VerifyBackupAsync(SqlSchemaConfiguration config, string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    _logger.LogWarning("Backup file does not exist: {BackupPath}", backupPath);
                    return false;
                }

                var fileInfo = new FileInfo(backupPath);
                if (fileInfo.Length == 0)
                {
                    _logger.LogWarning("Backup file is empty: {BackupPath}", backupPath);
                    return false;
                }

                var provider = config.Database.GetSelectedProvider();

                // Provider-specific verification
                return await VerifyProviderBackupAsync(provider, config, backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify backup: {BackupPath}", backupPath);
                return false;
            }
        }

        public async Task<List<string>> ListBackupsAsync(SqlSchemaConfiguration config)
        {
            try
            {
                var backupDirectory = GetBackupDirectory(config);
                var backups = new List<string>();

                if (!Directory.Exists(backupDirectory))
                {
                    _logger.LogInformation("Backup directory does not exist: {BackupDirectory}", backupDirectory);
                    return backups;
                }

                var provider = config.Database.GetSelectedProvider();
                var backupExtension = GetBackupExtension(provider);
                var pattern = $"*{config.Database.DatabaseName}*{backupExtension}";

                var backupFiles = Directory.GetFiles(backupDirectory, pattern, SearchOption.TopDirectoryOnly);

                foreach (var file in backupFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > 0) // Only include non-empty files
                    {
                        backups.Add(file);
                    }
                }

                // Sort by creation time, newest first
                backups.Sort((a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));

                _logger.LogInformation("Found {BackupCount} backups for database: {DatabaseName}",
                    backups.Count, config.Database.DatabaseName);

                return backups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list backups for database: {DatabaseName}", config.Database.DatabaseName);
                throw;
            }
        }

        public async Task RestoreBackupAsync(SqlSchemaConfiguration config, string backupPath)
        {
            try
            {
                _logger.LogInformation("Restoring backup: {BackupPath}", backupPath);

                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException($"Backup file not found: {backupPath}");
                }

                var provider = config.Database.GetSelectedProvider();
                var databaseProvider = await _databaseProviderFactory.GetProviderAsync(provider);

                // Execute provider-specific restore
                await ExecuteProviderRestoreAsync(provider, config, backupPath, databaseProvider);

                _logger.LogInformation("✓ Backup restored successfully: {BackupPath}", backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore backup: {BackupPath}", backupPath);
                throw new SqlSchemaException(SqlSchemaExitCode.DatabaseConnectionFailure,
                    $"Failed to restore database backup: {ex.Message}", ex);
            }
        }

        public async Task CleanupOldBackupsAsync(SqlSchemaConfiguration config)
        {
            try
            {
                if (config.Backup.RetentionDays <= 0)
                {
                    _logger.LogDebug("Backup retention not configured, skipping cleanup");
                    return;
                }

                _logger.LogInformation("Cleaning up backups older than {RetentionDays} days", config.Backup.RetentionDays);

                var backups = await ListBackupsAsync(config);
                var cutoffDate = DateTime.UtcNow.AddDays(-config.Backup.RetentionDays);
                var deletedCount = 0;

                foreach (var backup in backups)
                {
                    var fileInfo = new FileInfo(backup);
                    if (fileInfo.CreationTimeUtc < cutoffDate)
                    {
                        try
                        {
                            File.Delete(backup);
                            deletedCount++;
                            _logger.LogDebug("Deleted old backup: {BackupPath}", backup);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete old backup: {BackupPath}", backup);
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    _logger.LogInformation("✓ Cleaned up {DeletedCount} old backups", deletedCount);
                }
                else
                {
                    _logger.LogInformation("No old backups to clean up");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup old backups");
                // Don't throw - cleanup failure shouldn't stop deployment
            }
        }

        private BackupInfo GenerateBackupInfo(SqlSchemaConfiguration config)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var provider = config.Database.GetSelectedProvider();
            var extension = GetBackupExtension(provider);

            var backupDirectory = GetBackupDirectory(config);
            var restorePointLabel = config.Backup.RestorePointLabel ?? "schema_deployment";

            var fileName = $"{config.Database.DatabaseName}_{restorePointLabel}_{timestamp}{extension}";
            var backupPath = Path.Combine(backupDirectory, fileName);

            return new BackupInfo
            {
                BackupDirectory = backupDirectory,
                BackupPath = backupPath,
                FileName = fileName,
                Timestamp = timestamp,
                RestorePointLabel = restorePointLabel
            };
        }

        private string GetBackupDirectory(SqlSchemaConfiguration config)
        {
            // Use environment-specific backup directory
            var baseDir = Environment.GetEnvironmentVariable("BACKUP_DIRECTORY") ?? "/backups";
            var environmentDir = Path.Combine(baseDir, config.Environment.Environment);

            if (!string.IsNullOrEmpty(config.Environment.Vertical))
            {
                environmentDir = Path.Combine(environmentDir, config.Environment.Vertical);
            }

            return environmentDir;
        }

        private string GetBackupExtension(string provider)
        {
            return provider switch
            {
                "sqlserver" => ".bak",
                "postgresql" => ".sql",
                "mysql" => ".sql",
                "oracle" => ".dmp",
                "sqlite" => ".db",
                _ => ".backup"
            };
        }

        private void EnsureBackupDirectoryExists(string backupDirectory)
        {
            if (!Directory.Exists(backupDirectory))
            {
                Directory.CreateDirectory(backupDirectory);
                _logger.LogDebug("Created backup directory: {BackupDirectory}", backupDirectory);
            }
        }

        private async Task ExecuteProviderBackupAsync(string provider, SqlSchemaConfiguration config, BackupInfo backupInfo, IDatabaseProvider databaseProvider)
        {
            switch (provider)
            {
                case "sqlserver":
                    await ExecuteSqlServerBackupAsync(config, backupInfo, databaseProvider);
                    break;
                case "postgresql":
                    await ExecutePostgreSqlBackupAsync(config, backupInfo);
                    break;
                case "mysql":
                    await ExecuteMySqlBackupAsync(config, backupInfo);
                    break;
                case "oracle":
                    await ExecuteOracleBackupAsync(config, backupInfo);
                    break;
                case "sqlite":
                    await ExecuteSqliteBackupAsync(config, backupInfo);
                    break;
                default:
                    throw new NotSupportedException($"Backup not supported for provider: {provider}");
            }
        }

        private async Task ExecuteSqlServerBackupAsync(SqlSchemaConfiguration config, BackupInfo backupInfo, IDatabaseProvider databaseProvider)
        {
            var backupType = config.Database.SqlServerBackupType.ToUpperInvariant();
            var sql = $@"
                BACKUP DATABASE [{config.Database.DatabaseName}] 
                TO DISK = N'{backupInfo.BackupPath}'
                WITH {backupType}, 
                INIT, 
                NAME = N'{config.Database.DatabaseName}-{backupInfo.RestorePointLabel}',
                DESCRIPTION = N'Schema deployment backup created by SQL Schema Generator',
                COMPRESSION,
                CHECKSUM,
                STATS = 10;";

            await databaseProvider.ExecuteSqlAsync(sql, config);
        }

        private async Task ExecutePostgreSqlBackupAsync(SqlSchemaConfiguration config, BackupInfo backupInfo)
        {
            var connectionString = config.Database.BuildConnectionString();
            var uri = new Uri($"postgresql://{connectionString}");

            var arguments = $"--host={uri.Host} --port={uri.Port} --username={config.Database.Username} " +
                          $"--dbname={config.Database.DatabaseName} --file={backupInfo.BackupPath} " +
                          $"--verbose --format=custom --compress=9";

            await ExecuteExternalCommandAsync("pg_dump", arguments, config.Database.Password);
        }

        private async Task ExecuteMySqlBackupAsync(SqlSchemaConfiguration config, BackupInfo backupInfo)
        {
            var arguments = $"--host={config.Database.Server} --user={config.Database.Username} " +
                          $"--password={config.Database.Password} --single-transaction " +
                          $"--routines --triggers --databases {config.Database.DatabaseName} " +
                          $"--result-file={backupInfo.BackupPath}";

            await ExecuteExternalCommandAsync("mysqldump", arguments);
        }

        private async Task ExecuteOracleBackupAsync(SqlSchemaConfiguration config, BackupInfo backupInfo)
        {
            var arguments = $"userid={config.Database.Username}/{config.Database.Password}@{config.Database.Server}:{config.Database.Port}/{config.Database.OracleServiceName} " +
                          $"schemas={config.Database.Username} directory=BACKUP_DIR dumpfile={Path.GetFileName(backupInfo.BackupPath)} " +
                          $"logfile=backup_{backupInfo.Timestamp}.log";

            await ExecuteExternalCommandAsync("expdp", arguments);
        }

        private async Task ExecuteSqliteBackupAsync(SqlSchemaConfiguration config, BackupInfo backupInfo)
        {
            var sourceFile = config.Database.SqliteFilePath;
            if (string.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile))
            {
                throw new FileNotFoundException($"SQLite database file not found: {sourceFile}");
            }

            // For SQLite, backup is a simple file copy
            File.Copy(sourceFile, backupInfo.BackupPath, overwrite: true);

            // Verify the copied file
            if (!File.Exists(backupInfo.BackupPath))
            {
                throw new InvalidOperationException($"Failed to create SQLite backup: {backupInfo.BackupPath}");
            }
        }

        private async Task<bool> VerifyProviderBackupAsync(string provider, SqlSchemaConfiguration config, string backupPath)
        {
            switch (provider)
            {
                case "sqlserver":
                    return await VerifySqlServerBackupAsync(config, backupPath);
                case "postgresql":
                    return await VerifyPostgreSqlBackupAsync(config, backupPath);
                case "mysql":
                    return await VerifyMySqlBackupAsync(config, backupPath);
                case "oracle":
                    return await VerifyOracleBackupAsync(config, backupPath);
                case "sqlite":
                    return await VerifySqliteBackupAsync(config, backupPath);
                default:
                    // For unknown providers, just check if file exists and is not empty
                    return File.Exists(backupPath) && new FileInfo(backupPath).Length > 0;
            }
        }

        private async Task<bool> VerifySqlServerBackupAsync(SqlSchemaConfiguration config, string backupPath)
        {
            try
            {
                var databaseProvider = await _databaseProviderFactory.GetProviderAsync("sqlserver");
                var sql = $"RESTORE VERIFYONLY FROM DISK = N'{backupPath}';";
                await databaseProvider.ExecuteSqlAsync(sql, config);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQL Server backup verification failed: {BackupPath}", backupPath);
                return false;
            }
        }

        private async Task<bool> VerifyPostgreSqlBackupAsync(SqlSchemaConfiguration config, string backupPath)
        {
            try
            {
                // For PostgreSQL custom format, we can use pg_restore to verify
                var arguments = $"--list {backupPath}";
                await ExecuteExternalCommandAsync("pg_restore", arguments);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PostgreSQL backup verification failed: {BackupPath}", backupPath);
                return false;
            }
        }

        private async Task<bool> VerifyMySqlBackupAsync(SqlSchemaConfiguration config, string backupPath)
        {
            try
            {
                // For MySQL, check if the file contains expected SQL content
                var content = await File.ReadAllTextAsync(backupPath);
                return content.Contains("CREATE TABLE") || content.Contains("INSERT INTO") || content.Contains("-- MySQL dump");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MySQL backup verification failed: {BackupPath}", backupPath);
                return false;
            }
        }

        private async Task<bool> VerifyOracleBackupAsync(SqlSchemaConfiguration config, string backupPath)
        {
            try
            {
                // For Oracle dump files, check file header
                var buffer = new byte[4];
                using var fileStream = File.OpenRead(backupPath);
                await fileStream.ReadAsync(buffer, 0, 4);

                // Oracle dump files start with specific magic bytes
                return buffer[0] == 0x01 && buffer[1] == 0x00;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Oracle backup verification failed: {BackupPath}", backupPath);
                return false;
            }
        }

        private async Task<bool> VerifySqliteBackupAsync(SqlSchemaConfiguration config, string backupPath)
        {
            try
            {
                // Verify SQLite file format by checking header
                var buffer = new byte[16];
                using var fileStream = File.OpenRead(backupPath);
                await fileStream.ReadAsync(buffer, 0, 16);

                // SQLite files start with "SQLite format 3\0"
                var header = System.Text.Encoding.ASCII.GetString(buffer);
                return header.StartsWith("SQLite format 3");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQLite backup verification failed: {BackupPath}", backupPath);
                return false;
            }
        }

        private async Task ExecuteProviderRestoreAsync(string provider, SqlSchemaConfiguration config, string backupPath, IDatabaseProvider databaseProvider)
        {
            switch (provider)
            {
                case "sqlserver":
                    await ExecuteSqlServerRestoreAsync(config, backupPath, databaseProvider);
                    break;
                case "postgresql":
                    await ExecutePostgreSqlRestoreAsync(config, backupPath);
                    break;
                case "mysql":
                    await ExecuteMySqlRestoreAsync(config, backupPath);
                    break;
                case "oracle":
                    await ExecuteOracleRestoreAsync(config, backupPath);
                    break;
                case "sqlite":
                    await ExecuteSqliteRestoreAsync(config, backupPath);
                    break;
                default:
                    throw new NotSupportedException($"Restore not supported for provider: {provider}");
            }
        }

        private async Task ExecuteSqlServerRestoreAsync(SqlSchemaConfiguration config, string backupPath, IDatabaseProvider databaseProvider)
        {
            var sql = $@"
                RESTORE DATABASE [{config.Database.DatabaseName}] 
                FROM DISK = N'{backupPath}'
                WITH REPLACE, STATS = 10;";

            await databaseProvider.ExecuteSqlAsync(sql, config);
        }

        private async Task ExecutePostgreSqlRestoreAsync(SqlSchemaConfiguration config, string backupPath)
        {
            var arguments = $"--host={config.Database.Server} --port={config.Database.Port} " +
                          $"--username={config.Database.Username} --dbname={config.Database.DatabaseName} " +
                          $"--clean --create {backupPath}";

            await ExecuteExternalCommandAsync("pg_restore", arguments, config.Database.Password);
        }

        private async Task ExecuteMySqlRestoreAsync(SqlSchemaConfiguration config, string backupPath)
        {
            var arguments = $"--host={config.Database.Server} --user={config.Database.Username} " +
                          $"--password={config.Database.Password} {config.Database.DatabaseName}";

            await ExecuteExternalCommandAsync("mysql", arguments, null, backupPath);
        }

        private async Task ExecuteOracleRestoreAsync(SqlSchemaConfiguration config, string backupPath)
        {
            var arguments = $"userid={config.Database.Username}/{config.Database.Password}@{config.Database.Server}:{config.Database.Port}/{config.Database.OracleServiceName} " +
                          $"schemas={config.Database.Username} directory=BACKUP_DIR dumpfile={Path.GetFileName(backupPath)} " +
                          $"table_exists_action=replace";

            await ExecuteExternalCommandAsync("impdp", arguments);
        }

        private async Task ExecuteSqliteRestoreAsync(SqlSchemaConfiguration config, string backupPath)
        {
            var targetFile = config.Database.SqliteFilePath;
            if (string.IsNullOrEmpty(targetFile))
            {
                throw new InvalidOperationException("SQLite file path not configured for restore");
            }

            // For SQLite, restore is a simple file copy
            File.Copy(backupPath, targetFile, overwrite: true);
        }

        private async Task ExecuteExternalCommandAsync(string command, string arguments, string password = null, string inputFile = null)
        {
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = inputFile != null,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(password))
            {
                processStartInfo.Environment["PGPASSWORD"] = password;
            }

            using var process = new System.Diagnostics.Process { StartInfo = processStartInfo };

            process.Start();

            if (!string.IsNullOrEmpty(inputFile))
            {
                var fileContent = await File.ReadAllTextAsync(inputFile);
                await process.StandardInput.WriteAsync(fileContent);
                process.StandardInput.Close();
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Command '{command}' failed with exit code {process.ExitCode}. Error: {error}");
            }

            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Command '{Command}' completed with warnings: {Error}", command, error);
            }
        }

        private async Task LogBackupMetadataAsync(SqlSchemaConfiguration config, BackupInfo backupInfo)
        {
            var fileInfo = new FileInfo(backupInfo.BackupPath);
            var metadata = new Dictionary<string, object>
            {
                ["backup_path"] = backupInfo.BackupPath,
                ["file_size_bytes"] = fileInfo.Length,
                ["file_size_mb"] = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2),
                ["creation_time_utc"] = fileInfo.CreationTimeUtc,
                ["database_name"] = config.Database.DatabaseName,
                ["database_provider"] = config.Database.GetSelectedProvider(),
                ["environment"] = config.Environment.Environment,
                ["restore_point_label"] = backupInfo.RestorePointLabel,
                ["backup_type"] = config.Database.SqlServerBackupType
            };

            _logger.LogInformation("Backup metadata: {@BackupMetadata}", metadata);
        }

        private class BackupInfo
        {
            public string BackupDirectory { get; set; } = string.Empty;
            public string BackupPath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string Timestamp { get; set; } = string.Empty;
            public string RestorePointLabel { get; set; } = string.Empty;
        }
    }
}