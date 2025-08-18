using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Models;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    #region Report Data Models
    // These models define the structure of the final JSON report artifact.

    public class GuardianReport
    {
        [JsonPropertyName("summary")]
        public ReportSummary Summary { get; set; } = new();

        [JsonPropertyName("forcedContinuation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ForcedContinuation? ForcedContinuation { get; set; }

        [JsonPropertyName("activeViolations")]
        public List<ActiveViolationDetail> ActiveViolations { get; set; } = new();

        [JsonPropertyName("suppressedViolations")]
        public List<SuppressedViolationDetail> SuppressedViolations { get; set; } = new();
    }

    public class ReportSummary
    {
        [JsonPropertyName("violationsFound")]
        public int ViolationsFound { get; set; }

        [JsonPropertyName("violationsSuppressed")]
        public int ViolationsSuppressed { get; set; }

        [JsonPropertyName("activeViolations")]
        public int ActiveViolations { get; set; }

        [JsonPropertyName("highestActiveSeverity")]
        public string HighestActiveSeverity { get; set; } = "none";

        [JsonPropertyName("scanDurationMs")]
        public long ScanDurationMs { get; set; }

        [JsonPropertyName("result")]
        public string Result { get; set; } = "SUCCESS";
    }

    public class ForcedContinuation
    {
        [JsonPropertyName("activated")]
        public bool Activated { get; set; } = true;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "Emergency override flag 'GUARDIAN_CONTINUE_ON_FAILURE' was set, forcing a successful exit code despite active violations.";
    }

    public class ActiveViolationDetail
    {
        [JsonPropertyName("violationCode")]
        public string ViolationCode { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;

        [JsonPropertyName("location")]
        public string Location { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("sample")]
        public string Sample { get; set; } = string.Empty;
    }

    public class SuppressedViolationDetail
    {
        [JsonPropertyName("violationCode")]
        public string ViolationCode { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;

        [JsonPropertyName("location")]
        public string Location { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("suppression")]
        public SuppressionInfo Suppression { get; set; } = new();
    }

    public class SuppressionInfo
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Defines the contract for a service that generates the final JSON report and determines the exit code.
    /// </summary>
    public interface IReportGeneratorService
    {
        /// <summary>
        /// Generates the final Guardian report artifact and calculates the appropriate exit code.
        /// </summary>
        /// <param name="activeViolations">List of violations that were not suppressed.</param>
        /// <param name="suppressedViolations">List of violations that were suppressed by policy.</param>
        /// <param name="config">The application configuration.</param>
        /// <param name="scanDuration">The total duration of the scan.</param>
        /// <returns>The calculated exit code for the application.</returns>
        Task<ExitCode> GenerateReportAsync(
            List<Violation> activeViolations,
            List<Violation> suppressedViolations,
            GuardianConfiguration config,
            TimeSpan scanDuration);
    }

    /// <summary>
    /// Implements the logic for creating the final JSON report artifact.
    /// </summary>
    public class ReportGeneratorService : IReportGeneratorService
    {
        private readonly ILogger<ReportGeneratorService> _logger;
        private readonly string _reportPath = "/src/guardian-report.json";

        public ReportGeneratorService(ILogger<ReportGeneratorService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<ExitCode> GenerateReportAsync(
            List<Violation> activeViolations,
            List<Violation> suppressedViolations,
            GuardianConfiguration config,
            TimeSpan scanDuration)
        {
            _logger.LogInformation("Generating final governance report...");

            var report = new GuardianReport();
            var highestSeverity = "none";

            // Populate active violations
            foreach (var violation in activeViolations)
            {
                report.ActiveViolations.Add(new ActiveViolationDetail
                {
                    ViolationCode = violation.Rule.Code,
                    Severity = violation.Rule.Severity,
                    Location = $"{violation.Target.Schema}.{violation.Target.Table}.{violation.Target.Column}",
                    Message = $"{violation.Rule.Description} detected.",
                    Sample = SanitizeSample(violation.ViolatingValue)
                });
            }

            // Populate suppressed violations
            foreach (var violation in suppressedViolations)
            {
                report.SuppressedViolations.Add(new SuppressedViolationDetail
                {
                    ViolationCode = violation.Rule.Code,
                    Severity = violation.Rule.Severity,
                    Location = $"{violation.Target.Schema}.{violation.Target.Table}.{violation.Target.Column}",
                    Message = $"{violation.Rule.Description} detected.",
                    Suppression = new SuppressionInfo { Source = $"file:{config.ExceptionsFilePath}" }
                });
            }

            // Calculate summary and result
            report.Summary.ActiveViolations = activeViolations.Count;
            report.Summary.ViolationsSuppressed = suppressedViolations.Count;
            report.Summary.ViolationsFound = activeViolations.Count + suppressedViolations.Count;
            report.Summary.ScanDurationMs = (long)scanDuration.TotalMilliseconds;

            if (activeViolations.Any())
            {
                highestSeverity = GetHighestSeverity(activeViolations);
                report.Summary.HighestActiveSeverity = highestSeverity;
                report.Summary.Result = "BUILD_FAILURE";
            }

            // Handle the "break glass" override
            if (config.ContinueOnFailure && activeViolations.Any())
            {
                report.ForcedContinuation = new ForcedContinuation();
                report.Summary.Result = "FORCED_SUCCESS";
            }

            // Serialize and write the report file
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            var jsonReport = JsonSerializer.Serialize(report, jsonOptions);
            await File.WriteAllTextAsync(_reportPath, jsonReport);

            _logger.LogInformation("✓ Governance report generated at: {ReportPath}", _reportPath);

            return DetermineExitCode(report.Summary.Result);
        }

        private string SanitizeSample(string value, int maxLength = 100)
        {
            if (value.Length <= maxLength)
            {
                return value;
            }
            // Truncate and indicate that the sample has been shortened.
            return value.Substring(0, maxLength) + "...";
        }

        private string GetHighestSeverity(List<Violation> violations)
        {
            var severityOrder = new Dictionary<string, int>
            {
                ["info"] = 1,
                ["warning"] = 2,
                ["error"] = 3,
                ["critical"] = 4
            };

            return violations
                .OrderByDescending(v => severityOrder.GetValueOrDefault(v.Rule.Severity.ToLowerInvariant(), 0))
                .FirstOrDefault()?.Rule.Severity ?? "none";
        }

        private ExitCode DetermineExitCode(string result)
        {
            return result switch
            {
                "SUCCESS" => ExitCode.Success,
                "FORCED_SUCCESS" => ExitCode.Success,
                "BUILD_FAILURE" => ExitCode.ViolationsFound,
                _ => ExitCode.UnhandledException
            };
        }
    }
}