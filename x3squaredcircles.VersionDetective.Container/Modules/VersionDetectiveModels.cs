using System;
using System.Collections.Generic;

namespace x3squaredcircles.VersionDetective.Container.Models
{
    // Configuration models
    public class VersionDetectiveConfiguration
    {
        public LanguageConfiguration Language { get; set; } = new();
        public string TrackAttribute { get; set; } = string.Empty;
        public string RepoUrl { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public LicenseConfiguration License { get; set; } = new();
        public KeyVaultConfiguration? KeyVault { get; set; }
        public TagTemplateConfiguration TagTemplate { get; set; } = new();
        public AnalysisConfiguration Analysis { get; set; } = new();
        public LoggingConfiguration Logging { get; set; } = new();
        public AuthenticationConfiguration Authentication { get; set; } = new();
    }

    public class LanguageConfiguration
    {
        public bool CSharp { get; set; }
        public bool Java { get; set; }
        public bool Python { get; set; }
        public bool JavaScript { get; set; }
        public bool TypeScript { get; set; }
        public bool Go { get; set; }

        public string GetSelectedLanguage()
        {
            if (CSharp) return "csharp";
            if (Java) return "java";
            if (Python) return "python";
            if (JavaScript) return "javascript";
            if (TypeScript) return "typescript";
            if (Go) return "go";
            return string.Empty;
        }

        public bool HasSingleLanguageSelected()
        {
            var count = 0;
            if (CSharp) count++;
            if (Java) count++;
            if (Python) count++;
            if (JavaScript) count++;
            if (TypeScript) count++;
            if (Go) count++;
            return count == 1;
        }
    }

    public class LicenseConfiguration
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string ToolName { get; set; } = "version-calculator";
        public int TimeoutSeconds { get; set; } = 300;
        public int RetryIntervalSeconds { get; set; } = 30;
    }

    public class KeyVaultConfiguration
    {
        public string Type { get; set; } = string.Empty; // azure, aws, hashicorp
        public string Url { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    public class TagTemplateConfiguration
    {
        public string Template { get; set; } = "{branch}/{repo}/semver/{version}";
        public string MarketingTemplate { get; set; } = "{branch}/{repo}/marketing/{version}";
    }

    public class AnalysisConfiguration
    {
        public string? FromCommit { get; set; }
        public string Mode { get; set; } = "pr"; // pr, deploy
        public bool ValidateOnly { get; set; }
        public bool NoOp { get; set; }
        public List<string> DllPaths { get; set; } = new();
        public string? BuildOutputPath { get; set; }
    }

    public class LoggingConfiguration
    {
        public bool Verbose { get; set; }
        public string LogLevel { get; set; } = "INFO";
    }

    public class AuthenticationConfiguration
    {
        public string? PatToken { get; set; }
        public string? PatSecretName { get; set; }
        public string? PipelineToken { get; set; }
    }

    // License client models
    public class LicenseAcquireRequest
    {
        public string ToolName { get; set; } = string.Empty;
        public string ToolVersion { get; set; } = "1.0.0";
        public string IpAddress { get; set; } = string.Empty;
        public string BuildId { get; set; } = string.Empty;
    }

    public class LicenseAcquireResponse
    {
        public bool LicenseGranted { get; set; }
        public string? SessionId { get; set; }
        public bool BurstMode { get; set; }
        public int BurstCountRemaining { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Reason { get; set; }
        public bool BurstEventsExhausted { get; set; }
        public int RetryAfterSeconds { get; set; }
    }

    public class LicenseHeartbeatRequest
    {
        public string SessionId { get; set; } = string.Empty;
    }

    public class LicenseReleaseRequest
    {
        public string SessionId { get; set; } = string.Empty;
    }

    // Analysis models
    public class GitAnalysisResult
    {
        public string CurrentCommit { get; set; } = string.Empty;
        public string? BaselineCommit { get; set; }
        public List<GitFileChange> Changes { get; set; } = new();
        public List<string> CommitMessages { get; set; } = new();
    }

    public class GitFileChange
    {
        public string FilePath { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty; // Added, Modified, Deleted
        public string Content { get; set; } = string.Empty;
    }

    public class LanguageAnalysisResult
    {
        public List<TrackedEntity> Entities { get; set; } = new();
        public List<TrackedEntity> BaselineEntities { get; set; } = new();
        public List<EntityChange> EntityChanges { get; set; } = new();
        public QuantitativeChanges QuantitativeChanges { get; set; } = new();
    }

    public class TrackedEntity
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public List<EntityProperty> Properties { get; set; } = new();
        public List<string> Methods { get; set; } = new();
    }

    public class EntityProperty
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
    }

    public class EntityChange
    {
        public string Type { get; set; } = string.Empty; // NewEntity, RemovedEntity, NewProperty, etc.
        public string Description { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public bool IsMajorChange { get; set; }
    }

    public class QuantitativeChanges
    {
        public int NewClasses { get; set; }
        public int NewMethods { get; set; }
        public int NewProperties { get; set; }
        public int BugFixes { get; set; }
        public int PerformanceImprovements { get; set; }
        public int DocumentationUpdates { get; set; }
    }

    // Version calculation models
    public class VersionCalculationResult
    {
        public string CurrentVersion { get; set; } = "1.0.0";
        public string NewSemanticVersion { get; set; } = "1.0.0";
        public string NewMarketingVersion { get; set; } = "1.0.0";
        public bool HasMajorChanges { get; set; }
        public int MinorChanges { get; set; }
        public int PatchChanges { get; set; }
        public string Reasoning { get; set; } = string.Empty;
        public List<EntityChange> MajorChanges { get; set; } = new();
    }

    // Tag template models
    public class TagTemplateResult
    {
        public string SemanticTag { get; set; } = string.Empty;
        public string MarketingTag { get; set; } = string.Empty;
        public Dictionary<string, string> TokenValues { get; set; } = new();
    }

    // Output models
    public class VersionMetadata
    {
        public string ToolName { get; set; } = "version-calculator";
        public string ToolVersion { get; set; } = "1.0.0";
        public DateTime ExecutionTime { get; set; }
        public string Language { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string CurrentCommit { get; set; } = string.Empty;
        public string? BaselineCommit { get; set; }
        public VersionCalculationResult VersionCalculation { get; set; } = new();
        public TagTemplateResult TagTemplates { get; set; } = new();
        public bool LicenseUsed { get; set; }
        public bool BurstModeUsed { get; set; }
        public string Mode { get; set; } = string.Empty;
    }

    public class PipelineToolLogEntry
    {
        public string ToolName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool BurstMode { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Error models
    public enum VersionDetectiveExitCode
    {
        Success = 0,
        InvalidConfiguration = 1,
        LicenseUnavailable = 2,
        AuthenticationFailure = 3,
        RepositoryAccessFailure = 4,
        BuildArtifactsNotFound = 5,
        GitOperationFailure = 6,
        TagTemplateValidationFailure = 7,
        KeyVaultAccessFailure = 8
    }

    public class VersionDetectiveException : Exception
    {
        public VersionDetectiveExitCode ExitCode { get; }

        public VersionDetectiveException(VersionDetectiveExitCode exitCode, string message) : base(message)
        {
            ExitCode = exitCode;
        }

        public VersionDetectiveException(VersionDetectiveExitCode exitCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ExitCode = exitCode;
        }
    }
}