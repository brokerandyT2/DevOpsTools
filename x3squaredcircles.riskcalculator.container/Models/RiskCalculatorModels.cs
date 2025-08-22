using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace x3squaredcircles.RiskCalculator.Container.Models
{
    #region Core Enums and Exceptions

    /// <summary>
    /// Defines the exit codes for the Risk Calculator application.
    /// </summary>
    public enum RiskCalculatorExitCode
    {
        /// <summary>
        /// Success. No significant risk pattern changes were detected.
        /// </summary>
        Pass = 0,
        /// <summary>
        /// An unhandled exception occurred, terminating the application.
        /// </summary>
        UnhandledException = 1,
        /// <summary>
        /// The provided configuration was invalid or incomplete.
        /// </summary>
        InvalidConfiguration = 2,
        /// <summary>
        /// A failure occurred during Git operations.
        /// </summary>
        GitOperationFailure = 3,
        /// <summary>
        /// A failure occurred reading or writing the analysis state file.
        /// </summary>
        FileIOFailure = 4,
        /// <summary>
        /// License validation failed or a license was unavailable.
        /// </summary>
        LicenseFailure = 5,
        /// <summary>
        /// A Control Point webhook returned a failure or aborted the run.
        /// </summary>
        ControlPointAborted = 6,
        /// <summary>
        /// An alert was triggered based on risk pattern changes.
        /// </summary>
        Alert = 70,
        /// <summary>
        /// A critical failure threshold was crossed based on risk pattern changes.
        /// </summary>
        Fail = 71,
    }

    /// <summary>
    /// Represents the final decision of the risk analysis.
    /// </summary>
    public enum AnalysisDecision
    {
        Pass,
        Alert,
        Fail
    }

    /// <summary>
    /// Custom exception class for the Risk Calculator.
    /// </summary>
    public class RiskCalculatorException : Exception
    {
        public RiskCalculatorExitCode ExitCode { get; }
        public RiskCalculatorException(RiskCalculatorExitCode exitCode, string message) : base(message) { ExitCode = exitCode; }
        public RiskCalculatorException(RiskCalculatorExitCode exitCode, string message, Exception innerException) : base(message, innerException) { ExitCode = exitCode; }
    }

    #endregion

    #region Configuration Models

    /// <summary>
    /// Root model for all application configuration, populated from environment variables.
    /// </summary>
    public class RiskCalculatorConfiguration
    {
        public LicenseConfiguration License { get; set; } = new();
        public VaultConfiguration Vault { get; set; } = new();
        public LoggingConfiguration Logging { get; set; } = new();
        public ObservabilityConfiguration Observability { get; set; } = new();
        public GitConfiguration Git { get; set; } = new();
        public AnalysisConfiguration Analysis { get; set; } = new();
    }

    public class LicenseConfiguration
    {
        public string ServerUrl { get; set; }
    }

    public class VaultConfiguration
    {
        public VaultType Type { get; set; }
        public string Url { get; set; }
    }

    public enum VaultType { None, Azure, Aws, HashiCorp }

    public class LoggingConfiguration
    {
        public bool Verbose { get; set; }
        public LogLevel LogLevel { get; set; }
    }

    public class ObservabilityConfiguration
    {
        public string FirehoseLogEndpointUrl { get; set; }
        public string FirehoseLogEndpointToken { get; set; }
    }

    public class GitConfiguration
    {
        public string RepoUrl { get; set; }
        public string Branch { get; set; }
        public string PatSecretName { get; set; }
    }

    public class AnalysisConfiguration
    {
        public int AlertThreshold { get; set; }
        public int FailThreshold { get; set; }
        public bool AlertOnNewEntries { get; set; }
        public int MinimumPercentile { get; set; }
        public List<string> ExcludedAreas { get; set; } = new();
    }

    #endregion
    #region Analysis State & Result Models

    /// <summary>
    /// Represents the complete state of the risk analysis, which is persisted in change-analysis.json.
    /// </summary>
    public class AnalysisState
    {
        [JsonPropertyName("toolVersion")]
        public string ToolVersion { get; set; }

        [JsonPropertyName("analysisDateUtc")]
        public DateTime AnalysisDateUtc { get; set; }

        [JsonPropertyName("lastCommitHash")]
        public string LastCommitHash { get; set; }

        [JsonPropertyName("trackedAreas")]
        public List<TrackedArea> TrackedAreas { get; set; } = new();

        [JsonPropertyName("blastRadius")]
        public List<BlastRadiusAnalysis> BlastRadius { get; set; } = new();
    }

    /// <summary>
    /// Represents a single leaf directory being tracked and analyzed.
    /// </summary>
    public class TrackedArea
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("metrics")]
        public AreaMetrics Metrics { get; set; } = new();

        [JsonPropertyName("riskScore")]
        public double RiskScore { get; set; }

        [JsonPropertyName("percentile")]
        public double Percentile { get; set; }

        [JsonPropertyName("currentRanking")]
        public int? CurrentRanking { get; set; }
    }

    /// <summary>
    /// Contains the raw cumulative statistics for a tracked area.
    /// </summary>
    public class AreaMetrics
    {
        [JsonPropertyName("totalCommits")]
        public int TotalCommits { get; set; }

        [JsonPropertyName("totalLinesAdded")]
        public int TotalLinesAdded { get; set; }

        [JsonPropertyName("totalLinesDeleted")]
        public int TotalLinesDeleted { get; set; }

        [JsonPropertyName("totalFilesChanged")]
        public int TotalFilesChanged { get; set; }

        [JsonPropertyName("firstCommitDateUtc")]
        public DateTime FirstCommitDateUtc { get; set; }

        [JsonPropertyName("lastCommitDateUtc")]
        public DateTime LastCommitDateUtc { get; set; }
    }

    /// <summary>
    /// Represents the co-change correlation for a single source path.
    /// </summary>
    public class BlastRadiusAnalysis
    {
        [JsonPropertyName("sourcePath")]
        public string SourcePath { get; set; }

        [JsonPropertyName("correlatedPaths")]
        public List<CorrelatedPath> CorrelatedPaths { get; set; } = new();
    }

    /// <summary>
    /// Represents a path that frequently changes along with a source path.
    /// </summary>
    public class CorrelatedPath
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("correlationScore")]
        public double CorrelationScore { get; set; }

        [JsonPropertyName("cooccurrenceCount")]
        public int CooccurrenceCount { get; set; }
    }

    /// <summary>
    /// Represents the final output of the entire risk calculation process.
    /// </summary>
    public class RiskAnalysisResult
    {
        public AnalysisDecision Decision { get; set; }
        public List<string> Reasons { get; set; } = new();
        public AnalysisState PreviousState { get; set; }
        public AnalysisState CurrentState { get; set; }
        public List<RiskRankingChange> RankingChanges { get; set; } = new();
    }

    /// <summary>
    /// Details a specific change in ranking for a tracked area that triggered an alert or failure.
    /// </summary>
    public class RiskRankingChange
    {
        public string Path { get; set; }
        public int? PreviousRanking { get; set; }
        public int? CurrentRanking { get; set; }
        public ChangeType Type { get; set; }
    }

    public enum ChangeType
    {
        MovedUp,
        MovedDown,
        NewEntry,
        Exited
    }

    #endregion

    #region Git Analysis Models

    /// <summary>
    /// Represents the changes in a single file from a git delta.
    /// </summary>
    public class GitFileChange
    {
        public string Path { get; set; }
        public int LinesAdded { get; set; }
        public int LinesDeleted { get; set; }
        public string Status { get; set; } // e.g., 'A' for Added, 'M' for Modified
    }

    /// <summary>
    /// Contains all the changes discovered between two commits.
    /// </summary>
    public class GitDelta
    {
        public string FromCommit { get; set; }
        public string ToCommit { get; set; }
        public List<GitFileChange> Changes { get; set; } = new();
        public List<string> CochangedPaths { get; set; } = new();
    }

    #endregion
}