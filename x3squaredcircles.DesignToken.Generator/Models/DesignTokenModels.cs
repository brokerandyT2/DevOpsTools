using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

namespace x3squaredcircles.DesignToken.Generator.Models
{
    //
    // --- NEW: Refactored and Standardized Configuration Model ---
    //
    public class TokensConfiguration
    {
        public string Command { get; set; } = "sync";
        public string DesignPlatform { get; set; } = string.Empty;
        public string TargetPlatform { get; set; } = string.Empty;
        public string RepoUrl { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public bool ValidateOnly { get; set; }
        public bool NoOp { get; set; }

        public FigmaConfig Figma { get; set; } = new();

        public SketchConfig Sketch { get; set; } = new();
        public AdobeXdConfig AdobeXd { get; set; } = new();
        public ZeplinConfig Zeplin { get; set; } = new();
        public AbstractConfig Abstract { get; set; } = new();
        public PenpotConfig Penpot { get; set; } = new();

        public AndroidConfig Android { get; set; } = new();
        public IosConfig Ios { get; set; } = new();
        public WebConfig Web { get; set; } = new();

        public LicenseConfig License { get; set; } = new();
        public KeyVaultConfig KeyVault { get; set; } = new();
        public GitConfig Git { get; set; } = new();
        public LoggingConfig Logging { get; set; } = new();
        public ControlPointConfig ControlPoints { get; set; } = new();
        public FileManagementConfig FileManagement { get; set; } = new();
    }
    public class TagTemplateResult
    {
        public string GeneratedTag { get; set; } = string.Empty;
        public Dictionary<string, string> TokenValues { get; set; } = new();
    }
    public class FileManagementConfig
    {
        public string OutputDir { get; set; } = "design/tokens";
        public string GeneratedDir { get; set; } = "generated";
    }
    public class FigmaConfig { public string Url { get; set; } = string.Empty; public string TokenSecretName { get; set; } = string.Empty; }
    public class SketchConfig { public string WorkspaceId { get; set; } = string.Empty; public string DocumentId { get; set; } = string.Empty; public string TokenSecretName { get; set; } = string.Empty; }
    public class AdobeXdConfig { public string ProjectUrl { get; set; } = string.Empty; public string TokenSecretName { get; set; } = string.Empty; }
    public class ZeplinConfig { public string ProjectId { get; set; } = string.Empty; public string TokenSecretName { get; set; } = string.Empty; }
    public class AbstractConfig { public string ProjectId { get; set; } = string.Empty; public string TokenSecretName { get; set; } = string.Empty; }
    public class PenpotConfig { public string FileId { get; set; } = string.Empty; public string TokenSecretName { get; set; } = string.Empty; }
    public class AndroidConfig { public string OutputDir { get; set; } = "UI/Android/style/"; }
    public class IosConfig { public string OutputDir { get; set; } = "UI/iOS/style/"; }
    public class WebConfig { public string OutputDir { get; set; } = "UI/Web/style/"; public string Template { get; set; } = "vanilla"; }
    public class LicenseConfig { public string ServerUrl { get; set; } = string.Empty; public int TimeoutSeconds { get; set; } = 300; }
    public class GitConfig { public bool AutoCommit { get; set; } public string CommitMessage { get; set; } = "feat(tokens): update design tokens"; }
    // In Models.cs

    public class KeyVaultConfig
    {
        public string Type { get; set; } = string.Empty; // azure, aws, hashicorp, gcp
        public string Url { get; set; } = string.Empty;
        public string PatSecretName { get; set; } = string.Empty;

        // Azure
        public string AzureClientId { get; set; } = string.Empty;
        public string AzureClientSecret { get; set; } = string.Empty;
        public string AzureTenantId { get; set; } = string.Empty;

        // AWS
        public string AwsRegion { get; set; } = string.Empty;
        public string AwsAccessKeyId { get; set; } = string.Empty;
        public string AwsSecretAccessKey { get; set; } = string.Empty;

        // HashiCorp
        public string HashiCorpToken { get; set; } = string.Empty;

        // --- NEW: Added properties for GCP ---
        public string GcpServiceAccountKeyJson { get; set; } = string.Empty;
    }


    public class LoggingConfig
    {
        public bool Verbose { get; set; }
        public string LogLevel { get; set; } = "INFO";
        public string? LogEndpointUrl { get; set; }
        public string? LogEndpointToken { get; set; }
    }

    public class ControlPointConfig
    {
        public string? OnRunStartUrl { get; set; }
        public string? OnExtractSuccessUrl { get; set; }
        public string? OnExtractFailureUrl { get; set; }
        public string? OnGenerateSuccessUrl { get; set; }
        public string? OnGenerateFailureUrl { get; set; }
        public string? BeforeCommitUrl { get; set; }
        public string? OnCommitSuccessUrl { get; set; }
        public string? OnRunSuccessUrl { get; set; }
        public string? OnRunFailureUrl { get; set; }
        public int TimeoutSeconds { get; set; } = 60;
        public string TimeoutAction { get; set; } = "fail";
    }

    //
    // --- NEW: Control Point Payload Models ---
    //
    public class ControlPointPayload
    {
        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = string.Empty;
        [JsonPropertyName("toolVersion")]
        public string ToolVersion { get; set; } = string.Empty;
        [JsonPropertyName("timestampUtc")]
        public string TimestampUtc { get; set; } = string.Empty;
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        [JsonPropertyName("errorMessage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorMessage { get; set; }
        [JsonPropertyName("metadata")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? Metadata { get; set; }
    }

    // --- Original, non-configuration models remain largely unchanged ---

    public class DesignToken
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public object Value { get; set; } = new();
        public Dictionary<string, object> Attributes { get; set; } = new();
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class TokenCollection
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0.0";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = string.Empty;
        public List<DesignToken> Tokens { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class GenerationRequest
    {
        public TokenCollection Tokens { get; set; } = new();
        public string OutputDirectory { get; set; } = string.Empty;
        // Simplified for new config model
    }

    public class GenerationResult
    {
        public string Platform { get; set; } = string.Empty;
        public List<GeneratedFile> Files { get; set; } = new();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class GeneratedFile
    {
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class LicenseSession
    {
        public string SessionId { get; set; } = string.Empty;
        public bool BurstMode { get; set; }
        public Timer? HeartbeatTimer { get; set; }
    }

    // NEW: Simplified Exit Codes
    public enum DesignTokenExitCode
    {
        RepositoryAccessFailure = 4,
        Success = 0,
        InvalidConfiguration = 1,
        LicenseUnavailable = 2,
        TokenExtractionFailure = 6,
        PlatformGenerationFailure = 7,
        GitOperationFailure = 8,
        UnhandledException = 99,
        DesignPlatformApiFailure = 100
    }

    public class DesignTokenException : Exception
    {
        public DesignTokenExitCode ExitCode { get; }
        public DesignTokenException(DesignTokenExitCode exitCode, string message) : base(message) { ExitCode = exitCode; }
        public DesignTokenException(DesignTokenExitCode exitCode, string message, Exception innerException) : base(message, innerException) { ExitCode = exitCode; }
    }
}