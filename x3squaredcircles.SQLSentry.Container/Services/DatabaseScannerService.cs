using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Models;
using Microsoft.Data.SqlClient;
using Npgsql;
using MySql.Data.MySqlClient;
using Oracle.ManagedDataAccess.Client;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    /// <summary>
    /// Defines the contract for a service that scans a live database for data governance violations.
    /// </summary>
    public interface IDatabaseScannerService
    {
        /// <summary>
        /// Scans a list of target tables and columns for violations.
        /// </summary>
        /// <param name="config">The application configuration containing connection details.</param>
        /// <param name="targets">The list of specific schema elements to scan.</param>
        /// <param name="rulesEngine">The initialized rules engine to use for validation.</param>
        /// <returns>A list of all violations found during the scan.</returns>
        Task<List<Violation>> ScanTargetsAsync(GuardianConfiguration config, List<ScanTarget> targets, IRulesEngineService rulesEngine);
    }

    /// <summary>
    /// Implements the database scanning logic, including connection management,
    /// data sampling, and violation detection orchestration for multiple database dialects.
    /// </summary>
    public class DatabaseScannerService : IDatabaseScannerService
    {
        private readonly ILogger<DatabaseScannerService> _logger;
        private readonly IKeyVaultService _keyVaultService;
        private const int MaxSampleSize = 1000; // Limit the number of rows to sample per column.

        public DatabaseScannerService(ILogger<DatabaseScannerService> logger, IKeyVaultService keyVaultService)
        {
            _logger = logger;
            _keyVaultService = keyVaultService;
        }

        /// <inheritdoc />
        public async Task<List<Violation>> ScanTargetsAsync(GuardianConfiguration config, List<ScanTarget> targets, IRulesEngineService rulesEngine)
        {
            if (!targets.Any())
            {
                _logger.LogInformation("No new or modified columns to scan. Scan complete.");
                return new List<Violation>();
            }

            _logger.LogInformation("Beginning database scan of {Count} target columns...", targets.Count);
            var allViolations = new List<Violation>();
            var connectionString = await GetConnectionStringAsync(config);
            var provider = FindProviderFromConnectionString(connectionString, config);

            await using var connection = CreateDbConnection(provider, connectionString);

            try
            {
                await connection.OpenAsync();
                _logger.LogDebug("Database connection opened successfully for provider: {Provider}", provider);

                var targetsByTable = targets.GroupBy(t => new { t.Schema, t.Table });

                foreach (var tableGroup in targetsByTable)
                {
                    var schema = tableGroup.Key.Schema;
                    var table = tableGroup.Key.Table;
                    var columns = tableGroup.Select(tg => tg.Column).ToList();

                    allViolations.AddRange(await ScanTableAsync(connection, provider, schema, table, columns, rulesEngine));
                }

                _logger.LogInformation("✓ Database scan complete. Found {Count} raw violations.", allViolations.Count);
                return allViolations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database scan failed due to a connection or query error.");
                throw new GuardianException(ExitCode.DatabaseConnectionFailed, "DB_SCAN_ERROR", "An error occurred while connecting to or querying the database.", ex);
            }
        }

        private async Task<string> GetConnectionStringAsync(GuardianConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config.DbConnectionString))
            {
                return config.DbConnectionString;
            }
            return await _keyVaultService.ResolveDbConnectionStringAsync(config);
        }

        private async Task<List<Violation>> ScanTableAsync(DbConnection connection, string provider, string schema, string table, List<string> columns, IRulesEngineService rulesEngine)
        {
            var tableViolations = new List<Violation>();
            var query = GenerateSamplingQuery(provider, schema, table, columns);

            _logger.LogDebug("Executing sampling query for table [{Schema}].[{Table}] using {Provider} dialect.", schema, table, provider);
            _logger.LogTrace("Query: {Query}", query);

            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = 120; // Set a reasonable timeout for scan queries.

            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        if (await reader.IsDBNullAsync(i))
                        {
                            continue;
                        }

                        var value = reader.GetValue(i)?.ToString();
                        if (string.IsNullOrEmpty(value))
                        {
                            continue;
                        }

                        var matchedRule = rulesEngine.FindFirstViolation(value);
                        if (matchedRule != null)
                        {
                            var target = new ScanTarget(schema, table, columnName);
                            var violation = new Violation(matchedRule, target, value);
                            tableViolations.Add(violation);
                        }
                    }
                }
            }
            catch (DbException ex)
            {
                _logger.LogWarning(ex, "Could not execute scan query for table [{Schema}].[{Table}]. It may not exist or permissions may be denied. Skipping.", schema, table);
            }

            return tableViolations;
        }

        private string FindProviderFromConnectionString(string connectionString, GuardianConfiguration config)
        {
            // Infer provider from connection string keywords if not explicitly set by vault provider name
            var csLower = connectionString.ToLowerInvariant();
            if (!string.IsNullOrEmpty(config.VaultProvider)) return config.VaultProvider;
            if (csLower.Contains("server=") || csLower.Contains("data source=")) return "sqlserver";
            if (csLower.Contains("host=")) return "postgresql";
            if (csLower.Contains("server=")) return "mysql"; // Could be ambiguous, but common for mysql

            throw new GuardianException(ExitCode.InvalidConfiguration, "DB_PROVIDER_INDETERMINATE", "Could not determine the database provider from the connection string.");
        }

        private DbConnection CreateDbConnection(string provider, string connectionString)
        {
            return provider.ToLowerInvariant() switch
            {
                "sqlserver" or "azure" => new SqlConnection(connectionString),
                "postgresql" => new NpgsqlConnection(connectionString),
                "mysql" => new MySqlConnection(connectionString),
                "oracle" => new OracleConnection(connectionString),
                _ => throw new GuardianException(ExitCode.InvalidConfiguration, "UNSUPPORTED_DB_PROVIDER", $"The database provider '{provider}' is not supported for scanning.")
            };
        }

        private string GenerateSamplingQuery(string provider, string schema, string table, List<string> columns)
        {
            var quotedColumns = string.Join(", ", columns.Select(c => QuoteIdentifier(provider, c)));
            var quotedTable = $"{QuoteIdentifier(provider, schema)}.{QuoteIdentifier(provider, table)}";

            return provider.ToLowerInvariant() switch
            {
                // TABLESAMPLE is efficient and non-blocking for large tables.
                "sqlserver" or "azure" => $"SELECT TOP ({MaxSampleSize}) {quotedColumns} FROM {quotedTable} TABLESAMPLE ({MaxSampleSize} ROWS);",

                // Uses the BERNOULLI method for row-level sampling. Fast for small percentages.
                "postgresql" => $"SELECT {quotedColumns} FROM {quotedTable} TABLESAMPLE BERNOULLI (1) REPEATABLE(123) LIMIT {MaxSampleSize};",

                // No efficient native TABLESAMPLE. ORDER BY RAND() is very slow on large tables.
                // A better approach for huge tables involves joins with random numbers, but this is a safe default.
                "mysql" => $"SELECT {quotedColumns} FROM {quotedTable} ORDER BY RAND() LIMIT {MaxSampleSize};",

                // SAMPLE clause is the standard Oracle way.
                "oracle" => $"SELECT {quotedColumns} FROM {quotedTable} SAMPLE(1) WHERE ROWNUM <= {MaxSampleSize};",

                _ => throw new GuardianException(ExitCode.InvalidConfiguration, "UNSUPPORTED_SAMPLING_DIALECT", $"Sampling query generation is not supported for '{provider}'.")
            };
        }

        private string QuoteIdentifier(string provider, string identifier)
        {
            return provider.ToLowerInvariant() switch
            {
                "sqlserver" or "azure" => $"[{identifier}]",
                "postgresql" or "oracle" => $"\"{identifier}\"",
                "mysql" => $"`{identifier}`",
                _ => identifier
            };
        }
    }
}