using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace x3squaredcircles.SQLSentry.Container.Models
{
    /// <summary>
    /// Defines the standard exit codes for the Guardian application.
    /// </summary>
    public enum ExitCode
    {
        Success = 0,
        ViolationsFound = 1,
        InvalidConfiguration = 10,
        GitConnectionFailed = 11,
        FileReadFailed = 12,
        DatabaseConnectionFailed = 20,
        SqlAnalysisFailed = 21,
        ScanFailed = 22,
        UnhandledException = 99
    }

    /// <summary>
    /// Custom exception for handling controlled, expected errors within the application.
    /// </summary>
    public class GuardianException : Exception
    {
        public ExitCode ExitCode { get; }
        public string ErrorCode { get; }

        public GuardianException(ExitCode exitCode, string errorCode, string message) : base(message)
        {
            ExitCode = exitCode;
            ErrorCode = errorCode;
        }

        public GuardianException(ExitCode exitCode, string errorCode, string message, Exception innerException) : base(message, innerException)
        {
            ExitCode = exitCode;
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// A strongly-typed representation of all configuration parameters provided via environment variables.
    /// </summary>
    public class GuardianConfiguration
    {
        // Database Connection
        public string? DbConnectionString { get; set; }
        public string? DbVaultKey { get; set; }
        public string? VaultProvider { get; set; }
        public string? VaultUrl { get; set; }

        // Repository Connection
        public string GitRepoUrl { get; set; } = string.Empty;
        public string GitPat { get; set; } = string.Empty;
        public string GitSqlFilePath { get; set; } = string.Empty;

        // Optional File Paths
        public string ExceptionsFilePath { get; set; } = "./guardian.exceptions.json";
        public string PatternsFilePath { get; set; } = "./guardian.patterns.json";

        // Operational Overrides
        public bool ContinueOnFailure { get; set; }
        public string SqlFilePath { get; internal set; }
    }

    /// <summary>
    /// Represents the structure of the guardian.exceptions.json file.
    /// </summary>
    public class ExceptionFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("suppressions")]
        public Dictionary<string, List<SuppressionLocation>> Suppressions { get; set; } = new();
    }

    /// <summary>
    /// Defines a location (table and column) for a suppression rule.
    /// </summary>
    public class SuppressionLocation
    {
        [JsonPropertyName("table")]
        public string Table { get; set; } = string.Empty;

        [JsonPropertyName("column")]
        public string Column { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents the structure of the guardian.patterns.json file.
    /// </summary>
    public class PatternFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("patterns")]
        public List<CustomPattern> Patterns { get; set; } = new();
    }

    /// <summary>
    /// Defines a custom, user-provided data pattern to scan for.
    /// </summary>
    public class CustomPattern
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "error";

        [JsonPropertyName("regex")]
        public string Regex { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }
}