using System;
using System.Collections.Generic;
using System.Linq;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IConfigurationService
    {
        TokensConfiguration GetConfiguration();
        void ValidateConfiguration(TokensConfiguration config);
        void LogConfiguration(TokensConfiguration config, IAppLogger logger);
    }

    public class ConfigurationService : IConfigurationService
    {
        public TokensConfiguration GetConfiguration()
        {
            var config = new TokensConfiguration
            {
                Command = GetEnvironmentVariable("TOKENS_COMMAND", "sync").ToLowerInvariant(),
                DesignPlatform = GetRequiredEnvironmentVariable("TOKENS_DESIGN_PLATFORM").ToLowerInvariant(),
                TargetPlatform = GetRequiredEnvironmentVariable("TOKENS_TARGET_PLATFORM").ToLowerInvariant(),
                RepoUrl = GetRequiredEnvironmentVariable("TOKENS_REPO_URL"),
                Branch = GetRequiredEnvironmentVariable("TOKENS_BRANCH"),
                ValidateOnly = GetBooleanEnvironmentVariable("TOKENS_VALIDATE_ONLY"),
                NoOp = GetBooleanEnvironmentVariable("TOKENS_NO_OP"),

                Figma = new FigmaConfig
                {
                    Url = GetEnvironmentVariable("TOKENS_FIGMA_URL"),
                    TokenSecretName = GetEnvironmentVariable("TOKENS_FIGMA_TOKEN_SECRET_NAME")
                },
                Sketch = new SketchConfig
                {
                    WorkspaceId = GetEnvironmentVariable("TOKENS_SKETCH_WORKSPACE_ID"),
                    DocumentId = GetEnvironmentVariable("TOKENS_SKETCH_DOCUMENT_ID"),
                    TokenSecretName = GetEnvironmentVariable("TOKENS_SKETCH_TOKEN_SECRET_NAME")
                },
                AdobeXd = new AdobeXdConfig
                {
                    ProjectUrl = GetEnvironmentVariable("TOKENS_ADOBEXD_PROJECT_URL"),
                    TokenSecretName = GetEnvironmentVariable("TOKENS_ADOBEXD_TOKEN_SECRET_NAME")
                },
                Zeplin = new ZeplinConfig
                {
                    ProjectId = GetEnvironmentVariable("TOKENS_ZEPLIN_PROJECT_ID"),
                    TokenSecretName = GetEnvironmentVariable("TOKENS_ZEPLIN_TOKEN_SECRET_NAME")
                },
                Abstract = new AbstractConfig
                {
                    ProjectId = GetEnvironmentVariable("TOKENS_ABSTRACT_PROJECT_ID"),
                    TokenSecretName = GetEnvironmentVariable("TOKENS_ABSTRACT_TOKEN_SECRET_NAME")
                },
                Penpot = new PenpotConfig
                {
                    FileId = GetEnvironmentVariable("TOKENS_PENPOT_FILE_ID"),
                    TokenSecretName = GetEnvironmentVariable("TOKENS_PENPOT_TOKEN_SECRET_NAME")
                },

                Android = new AndroidConfig { OutputDir = GetEnvironmentVariable("TOKENS_ANDROID_OUTPUT_DIR", "UI/Android/style/") },
                Ios = new IosConfig { OutputDir = GetEnvironmentVariable("TOKENS_IOS_OUTPUT_DIR", "UI/iOS/style/") },
                Web = new WebConfig { OutputDir = GetEnvironmentVariable("TOKENS_WEB_OUTPUT_DIR", "UI/Web/style/") },

                License = new LicenseConfig
                {
                    ServerUrl = GetRequiredEnvironmentVariable("TOKENS_LICENSE_SERVER"),
                    TimeoutSeconds = GetIntEnvironmentVariable("TOKENS_LICENSE_TIMEOUT", 300)
                },
                KeyVault = new KeyVaultConfig
                {
                    Type = GetEnvironmentVariable("TOKENS_VAULT_TYPE"),
                    Url = GetEnvironmentVariable("TOKENS_VAULT_URL"),
                    PatSecretName = GetEnvironmentVariable("TOKENS_PAT_SECRET_NAME"),
                    AzureClientId = GetEnvironmentVariable("TOKENS_AZURE_CLIENT_ID"),
                    AzureClientSecret = GetEnvironmentVariable("TOKENS_AZURE_CLIENT_SECRET"),
                    AzureTenantId = GetEnvironmentVariable("TOKENS_AZURE_TENANT_ID"),
                    AwsRegion = GetEnvironmentVariable("TOKENS_AWS_REGION"),
                    AwsAccessKeyId = GetEnvironmentVariable("TOKENS_AWS_ACCESS_KEY_ID"),
                    AwsSecretAccessKey = GetEnvironmentVariable("TOKENS_AWS_SECRET_ACCESS_KEY"),
                    HashiCorpToken = GetEnvironmentVariable("TOKENS_HASHICORP_TOKEN"),
                    GcpServiceAccountKeyJson = GetEnvironmentVariable("TOKENS_GCP_SERVICE_ACCOUNT_KEY_JSON")
                },
                Git = new GitConfig
                {
                    AutoCommit = GetBooleanEnvironmentVariable("TOKENS_GIT_AUTO_COMMIT"),
                    CommitMessage = GetEnvironmentVariable("TOKENS_GIT_COMMIT_MESSAGE", "feat(tokens): update design tokens")
                },
                Logging = new LoggingConfig
                {
                    Verbose = GetBooleanEnvironmentVariable("TOKENS_VERBOSE"),
                    LogLevel = GetEnvironmentVariable("TOKENS_LOG_LEVEL", "INFO").ToUpperInvariant(),
                    LogEndpointUrl = GetEnvironmentVariable("TOKENS_LOG_ENDPOINT_URL"),
                    LogEndpointToken = GetEnvironmentVariable("TOKENS_LOG_ENDPOINT_TOKEN")
                },
                ControlPoints = new ControlPointConfig
                {
                    OnRunStartUrl = GetEnvironmentVariable("TOKENS_CP_ON_RUN_START"),
                    OnExtractSuccessUrl = GetEnvironmentVariable("TOKENS_CP_ON_EXTRACT_SUCCESS"),
                    OnExtractFailureUrl = GetEnvironmentVariable("TOKENS_CP_ON_EXTRACT_FAILURE"),
                    OnGenerateSuccessUrl = GetEnvironmentVariable("TOKENS_CP_ON_GENERATE_SUCCESS"),
                    OnGenerateFailureUrl = GetEnvironmentVariable("TOKENS_CP_ON_GENERATE_FAILURE"),
                    BeforeCommitUrl = GetEnvironmentVariable("TOKENS_CP_BEFORE_COMMIT"),
                    OnCommitSuccessUrl = GetEnvironmentVariable("TOKENS_CP_ON_COMMIT_SUCCESS"),
                    OnRunSuccessUrl = GetEnvironmentVariable("TOKENS_CP_ON_RUN_SUCCESS"),
                    OnRunFailureUrl = GetEnvironmentVariable("TOKENS_CP_ON_RUN_FAILURE"),
                    TimeoutSeconds = GetIntEnvironmentVariable("TOKENS_CP_TIMEOUT_SECONDS", 60),
                    TimeoutAction = GetEnvironmentVariable("TOKENS_CP_TIMEOUT_ACTION", "fail").ToLowerInvariant()
                }
            };

            return config;
        }
        public void ValidateConfiguration(TokensConfiguration config)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(config.RepoUrl)) errors.Add("TOKENS_REPO_URL is required.");
            if (string.IsNullOrWhiteSpace(config.Branch)) errors.Add("TOKENS_BRANCH is required.");
            if (string.IsNullOrWhiteSpace(config.License.ServerUrl)) errors.Add("TOKENS_LICENSE_SERVER is required.");
            if (string.IsNullOrWhiteSpace(config.DesignPlatform)) errors.Add("TOKENS_DESIGN_PLATFORM is required.");
            if (string.IsNullOrWhiteSpace(config.TargetPlatform)) errors.Add("TOKENS_TARGET_PLATFORM is required.");

            // Validate design platform specific configuration
            switch (config.DesignPlatform)
            {
                case "figma":
                    if (string.IsNullOrWhiteSpace(config.Figma.Url)) errors.Add("TOKENS_FIGMA_URL is required.");
                    if (string.IsNullOrWhiteSpace(config.Figma.TokenSecretName)) errors.Add("TOKENS_FIGMA_TOKEN_SECRET_NAME is required.");
                    break;
                case "sketch":
                    if (string.IsNullOrWhiteSpace(config.Sketch.WorkspaceId)) errors.Add("TOKENS_SKETCH_WORKSPACE_ID is required.");
                    if (string.IsNullOrWhiteSpace(config.Sketch.DocumentId)) errors.Add("TOKENS_SKETCH_DOCUMENT_ID is required.");
                    if (string.IsNullOrWhiteSpace(config.Sketch.TokenSecretName)) errors.Add("TOKENS_SKETCH_TOKEN_SECRET_NAME is required.");
                    break;
                case "adobe-xd":
                    if (string.IsNullOrWhiteSpace(config.AdobeXd.ProjectUrl)) errors.Add("TOKENS_ADOBEXD_PROJECT_URL is required.");
                    if (string.IsNullOrWhiteSpace(config.AdobeXd.TokenSecretName)) errors.Add("TOKENS_ADOBEXD_TOKEN_SECRET_NAME is required.");
                    break;
                case "zeplin":
                    if (string.IsNullOrWhiteSpace(config.Zeplin.ProjectId)) errors.Add("TOKENS_ZEPLIN_PROJECT_ID is required.");
                    if (string.IsNullOrWhiteSpace(config.Zeplin.TokenSecretName)) errors.Add("TOKENS_ZEPLIN_TOKEN_SECRET_NAME is required.");
                    break;
                case "abstract":
                    if (string.IsNullOrWhiteSpace(config.Abstract.ProjectId)) errors.Add("TOKENS_ABSTRACT_PROJECT_ID is required.");
                    if (string.IsNullOrWhiteSpace(config.Abstract.TokenSecretName)) errors.Add("TOKENS_ABSTRACT_TOKEN_SECRET_NAME is required.");
                    break;
                case "penpot":
                    if (string.IsNullOrWhiteSpace(config.Penpot.FileId)) errors.Add("TOKENS_PENPOT_FILE_ID is required.");
                    if (string.IsNullOrWhiteSpace(config.Penpot.TokenSecretName)) errors.Add("TOKENS_PENPOT_TOKEN_SECRET_NAME is required.");
                    break;
            }

            if (errors.Any())
            {
                var errorMessage = "Configuration validation failed:\n" + string.Join("\n", errors.Select(e => $"  - {e}"));
                throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, errorMessage);
            }
        }

        public void LogConfiguration(TokensConfiguration config, IAppLogger logger)
        {
            logger.LogInfo("--- Design Token Generator Configuration ---");
            logger.LogInfo($"  Command: {config.Command.ToUpperInvariant()}");
            logger.LogInfo($"  Design Platform: {config.DesignPlatform.ToUpperInvariant()}");
            logger.LogInfo($"  Target Platform: {config.TargetPlatform.ToUpperInvariant()}");
            logger.LogInfo($"  Repository: {config.RepoUrl}");
            logger.LogInfo($"  Branch: {config.Branch}");
            logger.LogInfo($"  License Server: {MaskUrl(config.License.ServerUrl)}");

            if (config.ValidateOnly) logger.LogInfo("  VALIDATE ONLY mode enabled");
            if (config.NoOp) logger.LogInfo("  NO-OP mode enabled");

            if (!string.IsNullOrEmpty(config.KeyVault.Type))
            {
                logger.LogInfo($"  Key Vault: {config.KeyVault.Type.ToUpperInvariant()} - {MaskUrl(config.KeyVault.Url)}");
            }
            logger.LogInfo("------------------------------------------");
        }

        #region Private Helper Methods

        private string GetRequiredEnvironmentVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration,
                    $"Required environment variable '{name}' is not set or is empty.");
            }
            return value;
        }

        private string GetEnvironmentVariable(string name, string defaultValue = "")
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        private bool GetBooleanEnvironmentVariable(string name, bool defaultValue = false)
        {
            var value = GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) return defaultValue;
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }

        private int GetIntEnvironmentVariable(string name, int defaultValue)
        {
            var value = GetEnvironmentVariable(name);
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private string MaskUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "Not Configured";
            try
            {
                var uri = new Uri(url);
                return $"{uri.Scheme}://{uri.Host}{uri.PathAndQuery}";
            }
            catch
            {
                return "Invalid URL Format";
            }
        }
        #endregion
    }
}