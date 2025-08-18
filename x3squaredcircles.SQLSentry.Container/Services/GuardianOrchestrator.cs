using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Models;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    /// <summary>
    /// Defines the contract for the main orchestrator that runs the end-to-end Guardian workflow.
    /// </summary>
    public interface IGuardianOrchestrator
    {
        /// <summary>
        /// Executes the complete data governance scan and reporting workflow.
        /// </summary>
        /// <returns>The final exit code for the application.</returns>
        Task<int> ExecuteAsync();
    }

    /// <summary>
    /// Orchestrates the entire Guardian workflow, coordinating all other services to execute the scan.
    /// </summary>
    public class GuardianOrchestrator : IGuardianOrchestrator
    {
        private readonly ILogger<GuardianOrchestrator> _logger;
        private readonly IConfigurationService _configService;
        private readonly IFileProviderService _fileProvider;
        private readonly ISqlDeltaParserService _sqlParser;
        private readonly IDatabaseScannerService _dbScanner;
        private readonly IRulesEngineService _rulesEngine;
        private readonly IReportGeneratorService _reportGenerator;

        public GuardianOrchestrator(
            ILogger<GuardianOrchestrator> logger,
            IConfigurationService configService,
            IFileProviderService fileProvider,
            ISqlDeltaParserService sqlParser,
            IDatabaseScannerService dbScanner,
            IRulesEngineService rulesEngine,
            IReportGeneratorService reportGenerator)
        {
            _logger = logger;
            _configService = configService;
            _fileProvider = fileProvider;
            _sqlParser = sqlParser;
            _dbScanner = dbScanner;
            _rulesEngine = rulesEngine;
            _reportGenerator = reportGenerator;
        }

        /// <inheritdoc />
        public async Task<int> ExecuteAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            // Step 1: Load and validate all configuration
            var config = _configService.GetConfiguration();
            _configService.LogConfiguration(config);

            // Step 2: Load all required and optional files from the local filesystem
            var sqlContent = await _fileProvider.GetSqlFileContentAsync(config.SqlFilePath);
            var exceptionsFile = await _fileProvider.GetExceptionsAsync(config.ExceptionsFilePath);
            var patternsFile = await _fileProvider.GetPatternsAsync(config.PatternsFilePath);

            // Step 3: Parse the SQL file to determine the scope of the scan
            var scanTargets = await _sqlParser.ParseDeltaAsync(sqlContent);
            if (!scanTargets.Any())
            {
                _logger.LogInformation("No new tables or columns found in the SQL file. No data scan required.");
                // Generate an empty success report to produce the artifact and maintain CI/CD consistency.
                await _reportGenerator.GenerateReportAsync(new List<Violation>(), new List<Violation>(), config, stopwatch.Elapsed);
                return (int)ExitCode.Success;
            }

            // Step 4: Initialize the rules engine with built-in and custom patterns
            _rulesEngine.Initialize(patternsFile);

            // Step 5: Execute the database scan against the identified targets
            var allViolations = await _dbScanner.ScanTargetsAsync(config, scanTargets, _rulesEngine);

            // Step 6: Process the raw findings against the exception file to separate active from suppressed violations
            var (activeViolations, suppressedViolations) = ProcessSuppressions(allViolations, exceptionsFile);

            // Step 7: Generate the final report and determine the application's exit code
            var exitCode = await _reportGenerator.GenerateReportAsync(activeViolations, suppressedViolations, config, stopwatch.Elapsed);

            stopwatch.Stop();
            return (int)exitCode;
        }

        /// <summary>
        /// Filters a list of raw violations against a set of suppression rules.
        /// </summary>
        /// <param name="allViolations">The complete list of violations found by the scanner.</param>
        /// <param name="exceptions">The loaded exceptions file, which may be null.</param>
        /// <returns>A tuple containing the list of active violations and the list of suppressed violations.</returns>
        private (List<Violation> Active, List<Violation> Suppressed) ProcessSuppressions(List<Violation> allViolations, ExceptionFile? exceptions)
        {
            if (exceptions?.Suppressions == null || !exceptions.Suppressions.Any())
            {
                // No suppression rules exist, so all found violations are active.
                return (allViolations, new List<Violation>());
            }

            _logger.LogInformation("Processing {Count} found violations against suppression rules...", allViolations.Count);

            var active = new List<Violation>();
            var suppressed = new List<Violation>();

            foreach (var violation in allViolations)
            {
                bool isSuppressed = false;

                // Check for a suppression rule matching the specific violation code.
                if (exceptions.Suppressions.TryGetValue(violation.Rule.Code, out var locations))
                {
                    if (IsLocationSuppressed(violation.Target, locations))
                    {
                        isSuppressed = true;
                    }
                }

                // If not suppressed yet, check for a wildcard ("*") suppression rule.
                if (!isSuppressed && exceptions.Suppressions.TryGetValue("*", out var wildcardLocations))
                {
                    if (IsLocationSuppressed(violation.Target, wildcardLocations))
                    {
                        isSuppressed = true;
                    }
                }

                if (isSuppressed)
                {
                    suppressed.Add(violation);
                }
                else
                {
                    active.Add(violation);
                }
            }

            _logger.LogInformation("Suppression processing complete. Active Violations: {ActiveCount}, Suppressed Violations: {SuppressedCount}", active.Count, suppressed.Count);
            return (active, suppressed);
        }

        /// <summary>
        /// Checks if a specific scan target matches any of the suppression locations.
        /// </summary>
        private bool IsLocationSuppressed(ScanTarget target, List<SuppressionLocation> locations)
        {
            foreach (var loc in locations)
            {
                // Check if the table name matches the suppression rule.
                // This supports exact matches and trailing wildcards (e.g., "Staging_*").
                bool tableMatch = loc.Table == "*" ||
                                  (loc.Table.EndsWith("*") && target.Table.StartsWith(loc.Table.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)) ||
                                  target.Table.Equals(loc.Table, StringComparison.OrdinalIgnoreCase);

                if (!tableMatch)
                {
                    continue;
                }

                // If the table matches, check if the column name matches.
                // This supports exact matches or a full column wildcard ("*").
                bool columnMatch = loc.Column == "*" ||
                                   target.Column.Equals(loc.Column, StringComparison.OrdinalIgnoreCase);

                if (columnMatch)
                {
                    return true; // The violation is in a suppressed location.
                }
            }
            return false; // No matching suppression rule was found for this location.
        }
    }
}