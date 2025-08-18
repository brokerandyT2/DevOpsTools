using System;
using System.Collections.Generic;

namespace x3squaredcircles.DesignToken.Generator.Models
{
    // Configuration models
    public class DesignTokenConfiguration
    {
        public DesignPlatformConfiguration DesignPlatform { get; set; } = new();
        public TargetPlatformConfiguration TargetPlatform { get; set; } = new();
        public string RepoUrl { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string? TargetRepo { get; set; }
        public LicenseConfiguration License { get; set; } = new();
        public KeyVaultConfiguration? KeyVault { get; set; }
        public TagTemplateConfiguration TagTemplate { get; set; } = new();
        public OperationConfiguration Operation { get; set; } = new();
        public FileManagementConfiguration FileManagement { get; set; } = new();
        public GitConfiguration Git { get; set; } = new();
        public LoggingConfiguration Logging { get; set; } = new();
        public AuthenticationConfiguration Authentication { get; set; } = new();
    }

    public class DesignPlatformConfiguration
    {
        public bool Figma { get; set; }
        public bool Sketch { get; set; }
        public bool AdobeXd { get; set; }
        public bool Zeplin { get; set; }
        public bool Abstract { get; set; }
        public bool Penpot { get; set; }

        // Figma specific
        public string FigmaUrl { get; set; } = string.Empty;
        public string FigmaTokenVaultKey { get; set; } = string.Empty;
        public string? FigmaVersionId { get; set; }
        public string? FigmaNodeId { get; set; }

        // Sketch specific
        public string SketchWorkspaceId { get; set; } = string.Empty;
        public string SketchDocumentId { get; set; } = string.Empty;
        public string SketchTokenVaultKey { get; set; } = string.Empty;
        public string? SketchPageName { get; set; }

        // Adobe XD specific
        public string XdProjectUrl { get; set; } = string.Empty;
        public string XdTokenVaultKey { get; set; } = string.Empty;
        public string? XdArtboardName { get; set; }

        // Zeplin specific
        public string ZeplinProjectId { get; set; } = string.Empty;
        public string ZeplinTokenVaultKey { get; set; } = string.Empty;
        public string? ZeplinStyleguideId { get; set; }

        // Abstract specific
        public string AbstractProjectId { get; set; } = string.Empty;
        public string AbstractTokenVaultKey { get; set; } = string.Empty;
        public string? AbstractBranchId { get; set; }
        public string? AbstractCommitSha { get; set; }

        // Penpot specific
        public string PenpotFileId { get; set; } = string.Empty;
        public string PenpotTokenVaultKey { get; set; } = string.Empty;
        public string? PenpotServerUrl { get; set; }
        public string? PenpotPageId { get; set; }

        public string GetSelectedPlatform()
        {
            if (Figma) return "figma";
            if (Sketch) return "sketch";
            if (AdobeXd) return "adobe-xd";
            if (Zeplin) return "zeplin";
            if (Abstract) return "abstract";
            if (Penpot) return "penpot";
            return string.Empty;
        }

        public bool HasSinglePlatformSelected()
        {
            var count = 0;
            if (Figma) count++;
            if (Sketch) count++;
            if (AdobeXd) count++;
            if (Zeplin) count++;
            if (Abstract) count++;
            if (Penpot) count++;
            return count == 1;
        }
    }

    public class TargetPlatformConfiguration
    {
        public bool Android { get; set; }
        public bool Ios { get; set; }
        public bool Web { get; set; }

        // Android specific
        public string? AndroidPackageName { get; set; }
        public string AndroidOutputDir { get; set; } = "UI/Android/style/";
        public string? AndroidThemeName { get; set; }
        public string AndroidComposeVersion { get; set; } = "latest";

        // iOS specific
        public string? IosModuleName { get; set; }
        public string IosOutputDir { get; set; } = "UI/iOS/style/";
        public string? IosThemeName { get; set; }
        public string IosSwiftVersion { get; set; } = "5.9";

        // Web specific
        public string WebTemplate { get; set; } = "vanilla";
        public string WebOutputDir { get; set; } = "UI/Web/style/";
        public string? WebCssPrefix { get; set; }
        public bool WebSupportDarkMode { get; set; } = true;

        // Web template-specific
        public string? TailwindConfigPath { get; set; }
        public bool TailwindExtendTheme { get; set; } = true;
        public string BootstrapVersion { get; set; } = "5.3";
        public string? BootstrapScssPath { get; set; }
        public string MaterialVersion { get; set; } = "3";
        public string? MaterialComponents { get; set; }

        public string GetSelectedPlatform()
        {
            if (Android) return "android";
            if (Ios) return "ios";
            if (Web) return "web";
            return string.Empty;
        }

        public bool HasSinglePlatformSelected()
        {
            var count = 0;
            if (Android) count++;
            if (Ios) count++;
            if (Web) count++;
            return count == 1;
        }
    }

    public class LicenseConfiguration
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string ToolName { get; set; } = "design-token-generator";
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
        public string Template { get; set; } = "{branch}/{repo}/tokens/{version}";
    }

    public class OperationConfiguration
    {
        public string Mode { get; set; } = "sync"; // extract, generate, sync
        public bool ValidateOnly { get; set; }
        public bool NoOp { get; set; }
        public bool PreserveCustom { get; set; } = true;
        public string MergeStrategy { get; set; } = "preserve-custom";
        public string TokenFormat { get; set; } = "json";
    }

    public class FileManagementConfiguration
    {
        public string OutputDir { get; set; } = "design/tokens";
        public string BaseTokensFile { get; set; } = "base.json";
        public string VerticalTokensFile { get; set; } = "{vertical}.json";
        public string GeneratedDir { get; set; } = "generated";
    }

    public class GitConfiguration
    {
        public bool AutoCommit { get; set; }
        public string? CommitMessage { get; set; }
        public string? CommitAuthorName { get; set; }
        public string? CommitAuthorEmail { get; set; }
        public bool CreateBranch { get; set; }
        public string? BranchNameTemplate { get; set; }
        public bool CreatePullRequest { get; set; }
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

    // Design token models
    public class DesignToken
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // color, typography, spacing, etc.
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
        public string Source { get; set; } = string.Empty; // figma, sketch, etc.
        public List<DesignToken> Tokens { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    // Platform generation models
    public class GenerationRequest
    {
        public TokenCollection Tokens { get; set; } = new();
        public TargetPlatformConfiguration Platform { get; set; } = new();
        public string OutputDirectory { get; set; } = string.Empty;
        public Dictionary<string, string> CustomSections { get; set; } = new();
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
        public bool HasCustomSections { get; set; }
        public List<CustomSection> PreservedSections { get; set; } = new();
    }

    public class CustomSection
    {
        public string Name { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }

    // License client models (same as Version Detective)
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

    public class LicenseSession
    {
        public string SessionId { get; set; } = string.Empty;
        public bool BurstMode { get; set; }
        public int BurstCountRemaining { get; set; }
        public DateTime ExpiresAt { get; set; }
        public Timer? HeartbeatTimer { get; set; }
    }

    // Tag template models
    public class TagTemplateResult
    {
        public string GeneratedTag { get; set; } = string.Empty;
        public Dictionary<string, string> TokenValues { get; set; } = new();
    }

    // Output models
    public class DesignTokenMetadata
    {
        public string ToolName { get; set; } = "design-token-generator";
        public string ToolVersion { get; set; } = "1.0.0";
        public DateTime ExecutionTime { get; set; }
        public string DesignPlatform { get; set; } = string.Empty;
        public string TargetPlatform { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public TokenCollection ExtractedTokens { get; set; } = new();
        public GenerationResult GenerationResult { get; set; } = new();
        public TagTemplateResult TagTemplate { get; set; } = new();
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
    public enum DesignTokenExitCode
    {
        Success = 0,
        InvalidConfiguration = 1,
        LicenseUnavailable = 2,
        AuthenticationFailure = 3,
        RepositoryAccessFailure = 4,
        DesignPlatformApiFailure = 5,
        TokenExtractionFailure = 6,
        PlatformGenerationFailure = 7,
        GitOperationFailure = 8,
        KeyVaultAccessFailure = 9,
        CustomSectionConflict = 10,
        FileSystemError = 11,
        NoDesignChangesDetected = 12
    }

    public class DesignTokenException : Exception
    {
        public DesignTokenExitCode ExitCode { get; }

        public DesignTokenException(DesignTokenExitCode exitCode, string message) : base(message)
        {
            ExitCode = exitCode;
        }

        public DesignTokenException(DesignTokenExitCode exitCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ExitCode = exitCode;
        }
    }
}