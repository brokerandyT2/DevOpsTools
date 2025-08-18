using Microsoft.Extensions.Logging;

namespace x3squaredcircles.API.Assembler.Models
{
    /// <summary>
    /// Root model for all application configuration, populated from environment variables.
    /// This class acts as a pure data container for settings.
    /// </summary>
    public class AssemblerConfiguration
    {
        // Language & Cloud Selection
        public string Language { get; set; } = string.Empty;
        public string Cloud { get; set; } = string.Empty;

        // Core Configuration
        public string RepoUrl { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string AssemblerEnv { get; set; } = "dev";

        // Paths
        public string Libs { get; set; } = string.Empty;
        public string Sources { get; set; } = string.Empty;
        public string OutputPath { get; set; } = "./output";

        // Operation Modes
        public bool ValidateOnly { get; set; }
        public bool NoOp { get; set; }

        // Nested Configuration Objects
        public LicenseConfiguration License { get; set; } = new LicenseConfiguration();
        public VaultConfiguration Vault { get; set; } = new VaultConfiguration();
        public LoggingConfiguration Logging { get; set; } = new LoggingConfiguration();
        public TagTemplateConfiguration TagTemplate { get; set; } = new TagTemplateConfiguration();
    }

    #region Nested Configuration Models

    public class LicenseConfiguration
    {
        public string ServerUrl { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 300;
    }

    public class VaultConfiguration
    {
        public string Type { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public class LoggingConfiguration
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Information;
        public string ExternalLogEndpoint { get; set; } = string.Empty;
        public string ExternalLogToken { get; set; } = string.Empty;
        public string ExternalLogTokenVaultKey { get; set; } = string.Empty;
    }

    public class TagTemplateConfiguration
    {
        public string Template { get; set; } = "{repo}/{group}/{version}";
    }

    #endregion
}