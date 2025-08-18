using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Models;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    /// <summary>
    /// Represents a specific table and column to be scanned for data governance violations.
    /// </summary>
    /// <param name="Schema">The database schema of the target.</param>
    /// <param name="Table">The table name of the target.</param>
    /// <param name="Column">The column name of the target.</param>
    public record ScanTarget(string Schema, string Table, string Column);

    /// <summary>
    /// Defines the contract for a service that parses a consolidated SQL script
    /// to identify new or added schema elements.
    /// </summary>
    public interface ISqlDeltaParserService
    {
        /// <summary>
        /// Parses the raw content of a SQL script to extract a list of new tables and columns.
        /// </summary>
        /// <param name="sqlContent">The string content of the consolidated SQL file.</param>
        /// <returns>A list of ScanTarget objects representing the schema delta.</returns>
        Task<List<ScanTarget>> ParseDeltaAsync(string sqlContent);
    }

    /// <summary>
    /// Implements SQL delta parsing using regular expressions to quickly identify
    /// new tables and columns from DDL statements.
    /// </summary>
    public class SqlDeltaParserService : ISqlDeltaParserService
    {
        private readonly ILogger<SqlDeltaParserService> _logger;

        // Regex to find CREATE TABLE statements and capture schema (optional), table name, and the entire column definition block.
        // Handles delimiters like [], "", and ``.
        private static readonly Regex CreateTableRegex = new(
            @"CREATE\s+TABLE\s+(?:\[(?<schema>\w+)\]\.?|`(?<schema>\w+)`\.?|""(?<schema>\w+)""\.?|(?<schema>\w+)\.)?\[(?<table>\w+)\]|`(?<table>\w+)`|""(?<table>\w+)""|(?<table>\w+)\s*\((?<columns>[\s\S]*?)\);?",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        // Regex to find column definitions within a CREATE TABLE block. Captures the column name.
        private static readonly Regex ColumnDefinitionRegex = new(
            @"^\s*(?:\[(?<column>\w+)\]|`(?<column>\w+)`|""(?<column>\w+)""|(?<column>\w+))\s+.*",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        // Regex to find ALTER TABLE ... ADD statements and capture schema (optional), table name, and the new column name.
        private static readonly Regex AlterTableAddRegex = new(
            @"ALTER\s+TABLE\s+(?:\[(?<schema>\w+)\]\.?|`(?<schema>\w+)`\.?|""(?<schema>\w+)""\.?|(?<schema>\w+)\.)?\[(?<table>\w+)\]|`(?<table>\w+)`|""(?<table>\w+)""|(?<table>\w+)\s+ADD\s+(?:COLUMN\s+)?\[(?<column>\w+)\]|`(?<column>\w+)`|""(?<column>\w+)""|(?<column>\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        public SqlDeltaParserService(ILogger<SqlDeltaParserService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public Task<List<ScanTarget>> ParseDeltaAsync(string sqlContent)
        {
            _logger.LogInformation("Parsing SQL file to identify schema delta...");

            var targets = new HashSet<ScanTarget>();

            try
            {
                // Process CREATE TABLE statements
                var createTableMatches = CreateTableRegex.Matches(sqlContent);
                foreach (Match match in createTableMatches)
                {
                    var schema = match.Groups["schema"].Success ? match.Groups["schema"].Value : "dbo";
                    var table = match.Groups["table"].Value;
                    var columnsBlock = match.Groups["columns"].Value;

                    var columnMatches = ColumnDefinitionRegex.Matches(columnsBlock);
                    foreach (Match colMatch in columnMatches)
                    {
                        var column = colMatch.Groups["column"].Value;
                        targets.Add(new ScanTarget(schema, table, column));
                    }
                }

                // Process ALTER TABLE ADD statements
                var alterTableMatches = AlterTableAddRegex.Matches(sqlContent);
                foreach (Match match in alterTableMatches)
                {
                    var schema = match.Groups["schema"].Success ? match.Groups["schema"].Value : "dbo";
                    var table = match.Groups["table"].Value;
                    var column = match.Groups["column"].Value;
                    targets.Add(new ScanTarget(schema, table, column));
                }

                _logger.LogInformation("✓ SQL delta parsed. Found {Count} new or added columns to scan.", targets.Count);
                return Task.FromResult(targets.ToList());
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogError(ex, "A regular expression timed out while parsing the SQL file.");
                throw new GuardianException(ExitCode.SqlAnalysisFailed, "SQL_PARSE_TIMEOUT", "Parsing the SQL file failed due to a timeout. The file may be unusually large or complex.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while parsing the SQL delta.");
                throw new GuardianException(ExitCode.SqlAnalysisFailed, "SQL_PARSE_ERROR", "An unexpected error occurred during SQL file analysis.", ex);
            }
        }
    }
}