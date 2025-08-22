using Microsoft.Extensions.Logging;

namespace x3squaredcircles.API.Assembler.Models
{
    /// <summary>
    /// Base class for shared configuration settings across 3SC tools.
    /// </summary>
    public abstract class BaseConfiguration
    {
        // Common Platform Settings
        public string RepoUrl { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public LicenseConfiguration License { get; set; } = new LicenseConfiguration();
        public VaultConfiguration Vault { get; set; } = new VaultConfiguration();
        public LoggingConfiguration Logging { get; set; } = new LoggingConfiguration();
        public ControlPointsConfiguration ControlPoints { get; set; } = new ControlPointsConfiguration();

        // Universal Operation Modes
        public bool ValidateOnly { get; set; }
        public bool NoOp { get; set; }
    }

    /// <summary>
    /// Root model for all application configuration, populated from environment variables.
    /// </summary>
    public class AssemblerConfiguration : BaseConfiguration
    {
        // Tool-Specific Configuration
        public string Language { get; set; } = string.Empty;
        public string Cloud { get; set; } = string.Empty;
        public string AssemblerEnv { get; set; } = "dev";
        public string Libs { get; set; } = string.Empty;
        public string Sources { get; set; } = string.Empty;
        public string OutputPath { get; set; } = "./output";
        public TagTemplateConfiguration TagTemplate { get; set; } = new TagTemplateConfiguration();
    }

    #region Nested Configuration Models

    public class LicenseConfiguration
    {
        public string ServerUrl { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 300;
        public int RetryIntervalSeconds { get; set; } = 30; // Added for consistency
    }

    public class VaultConfiguration
    {
        public string Type { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        // Specific provider credentials (e.g., Azure, AWS) would be loaded here as needed.
    }

    public class LoggingConfiguration
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Information;
        public string ExternalLogEndpoint { get; set; } = string.Empty;
        public string ExternalLogToken { get; set; } = string.Empty;
    }

    public class TagTemplateConfiguration
    {
        public string Template { get; set; } = "{repo}/{group}/{version}";
    }

    /// <summary>
    /// Holds the URLs for the universal Control Points.
    /// </summary>
    public class ControlPointsConfiguration
    {
        public string Logging { get; set; } = string.Empty;
        public string OnStartup { get; set; } = string.Empty;
        public string OnSuccess { get; set; } = string.Empty;
        public string OnFailure { get; set; } = string.Empty;

        // Tool-specific Control Points would be added here, e.g.:
        // public string DataApiDiscovery { get; set; } = string.Empty;
    }

    #endregion
}