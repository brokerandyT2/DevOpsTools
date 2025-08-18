using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IConfigurationService
    {
        DesignTokenConfiguration GetConfiguration();
        void ValidateConfiguration(DesignTokenConfiguration config);
        void LogConfiguration(DesignTokenConfiguration config);
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
        }

        public DesignTokenConfiguration GetConfiguration()
        {
            _logger.LogDebug("Parsing configuration from environment variables");

            var config = new DesignTokenConfiguration
            {
                DesignPlatform = ParseDesignPlatformConfiguration(),
                TargetPlatform = ParseTargetPlatformConfiguration(),
                RepoUrl = GetRequiredEnvironmentVariable("REPO_URL"),
                Branch = GetRequiredEnvironmentVariable("BRANCH"),
                TargetRepo = GetEnvironmentVariable("TARGET_REPO"),
                License = ParseLicenseConfiguration(),
                KeyVault = ParseKeyVaultConfiguration(),
                TagTemplate = ParseTagTemplateConfiguration(),
                Operation = ParseOperationConfiguration(),
                FileManagement = ParseFileManagementConfiguration(),
                Git = ParseGitConfiguration(),
                Logging = ParseLoggingConfiguration(),
                Authentication = ParseAuthenticationConfiguration()
            };

            return config;
        }

        public void ValidateConfiguration(DesignTokenConfiguration config)
        {
            _logger.LogDebug("Validating configuration");

            var errors = new List<string>();

            // Validate design platform selection
            if (!config.DesignPlatform.HasSinglePlatformSelected())
            {
                if (config.DesignPlatform.GetSelectedPlatform() == string.Empty)
                {
                    errors.Add("No design platform specified. Set exactly one: DESIGN_FIGMA, DESIGN_SKETCH, DESIGN_ADOBE_XD, DESIGN_ZEPLIN, DESIGN_ABSTRACT, or DESIGN_PENPOT to 'true'.");
                }
                else
                {
                    errors.Add("Multiple design platforms specified. Set exactly one design platform environment variable to 'true'.");
                }
            }

            // Validate target platform selection
            if (!config.TargetPlatform.HasSinglePlatformSelected())
            {
                if (config.TargetPlatform.GetSelectedPlatform() == string.Empty)
                {
                    errors.Add("No target platform specified. Set exactly one: PLATFORM_ANDROID, PLATFORM_IOS, or PLATFORM_WEB to 'true'.");
                }
                else
                {
                    errors.Add("Multiple target platforms specified. Set exactly one target platform environment variable to 'true'.");
                }
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(config.RepoUrl))
            {
                errors.Add("REPO_URL is required.");
            }

            if (string.IsNullOrWhiteSpace(config.Branch))
            {
                errors.Add("BRANCH is required.");
            }

            if (string.IsNullOrWhiteSpace(config.License.ServerUrl))
            {
                errors.Add("LICENSE_SERVER is required.");
            }

            // Validate design platform specific configuration
            ValidateDesignPlatformSpecificConfig(config.DesignPlatform, errors);

            // Validate tag template
            try
            {
                ValidateTagTemplate(config.TagTemplate.Template, "TAG_TEMPLATE");
            }
            catch (Exception ex)
            {
                errors.Add($"Tag template validation failed: {ex.Message}");
            }

            if (errors.Any())
            {
                var errorMessage = "Configuration validation failed:\n" + string.Join("\n", errors.Select(e => $"  - {e}"));
                _logger.LogError(errorMessage);
                throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, errorMessage);
            }

            _logger.LogInformation("✓ Configuration validation passed");
        }

        public void LogConfiguration(DesignTokenConfiguration config)
        {
            _logger.LogInformation("=== Design Token Generator Configuration ===");
            _logger.LogInformation("Design Platform: {DesignPlatform}", config.DesignPlatform.GetSelectedPlatform().ToUpperInvariant());
            _logger.LogInformation("Target Platform: {TargetPlatform}", config.TargetPlatform.GetSelectedPlatform().ToUpperInvariant());
            _logger.LogInformation("Repository: {RepoUrl}", config.RepoUrl);
            _logger.LogInformation("Branch: {Branch}", config.Branch);
            _logger.LogInformation("License Server: {LicenseServer}", MaskSensitiveUrl(config.License.ServerUrl));
            _logger.LogInformation("Mode: {Mode}", config.Operation.Mode.ToUpperInvariant());
            _logger.LogInformation("Tag Template: {TagTemplate}", config.TagTemplate.Template);

            if (config.Operation.ValidateOnly)
            {
                _logger.LogInformation("VALIDATE ONLY mode enabled");
            }

            if (config.Operation.NoOp)
            {
                _logger.LogInformation("NO-OP mode enabled");
            }

            if (config.KeyVault != null)
            {
                _logger.LogInformation("Key Vault: {VaultType} - {VaultUrl}", config.KeyVault.Type.ToUpperInvariant(), MaskSensitiveUrl(config.KeyVault.Url));
            }

            _logger.LogInformation("==========================================");
        }

        private DesignPlatformConfiguration ParseDesignPlatformConfiguration()
        {
            var config = new DesignPlatformConfiguration
            {
                Figma = GetBooleanEnvironmentVariable("DESIGN_FIGMA"),
                Sketch = GetBooleanEnvironmentVariable("DESIGN_SKETCH"),
                AdobeXd = GetBooleanEnvironmentVariable("DESIGN_ADOBE_XD"),
                Zeplin = GetBooleanEnvironmentVariable("DESIGN_ZEPLIN"),
                Abstract = GetBooleanEnvironmentVariable("DESIGN_ABSTRACT"),
                Penpot = GetBooleanEnvironmentVariable("DESIGN_PENPOT"),

                // Figma
                FigmaUrl = GetEnvironmentVariable("FIGMA_URL"),
                FigmaTokenVaultKey = GetEnvironmentVariable("FIGMA_TOKEN_VAULT_KEY"),
                FigmaVersionId = GetEnvironmentVariable("FIGMA_VERSION_ID"),
                FigmaNodeId = GetEnvironmentVariable("FIGMA_NODE_ID"),

                // Sketch
                SketchWorkspaceId = GetEnvironmentVariable("SKETCH_WORKSPACE_ID"),
                SketchDocumentId = GetEnvironmentVariable("SKETCH_DOCUMENT_ID"),
                SketchTokenVaultKey = GetEnvironmentVariable("SKETCH_TOKEN_VAULT_KEY"),
                SketchPageName = GetEnvironmentVariable("SKETCH_PAGE_NAME"),

                // Adobe XD
                XdProjectUrl = GetEnvironmentVariable("XD_PROJECT_URL"),
                XdTokenVaultKey = GetEnvironmentVariable("XD_TOKEN_VAULT_KEY"),
                XdArtboardName = GetEnvironmentVariable("XD_ARTBOARD_NAME"),

                // Zeplin
                ZeplinProjectId = GetEnvironmentVariable("ZEPLIN_PROJECT_ID"),
                ZeplinTokenVaultKey = GetEnvironmentVariable("ZEPLIN_TOKEN_VAULT_KEY"),
                ZeplinStyleguideId = GetEnvironmentVariable("ZEPLIN_STYLEGUIDE_ID"),

                // Abstract
                AbstractProjectId = GetEnvironmentVariable("ABSTRACT_PROJECT_ID"),
                AbstractTokenVaultKey = GetEnvironmentVariable("ABSTRACT_TOKEN_VAULT_KEY"),
                AbstractBranchId = GetEnvironmentVariable("ABSTRACT_BRANCH_ID"),
                AbstractCommitSha = GetEnvironmentVariable("ABSTRACT_COMMIT_SHA"),

                // Penpot
                PenpotFileId = GetEnvironmentVariable("PENPOT_FILE_ID"),
                PenpotTokenVaultKey = GetEnvironmentVariable("PENPOT_TOKEN_VAULT_KEY"),
                PenpotServerUrl = GetEnvironmentVariable("PENPOT_SERVER_URL"),
                PenpotPageId = GetEnvironmentVariable("PENPOT_PAGE_ID")
            };

            return config;
        }

        private TargetPlatformConfiguration ParseTargetPlatformConfiguration()
        {
            var config = new TargetPlatformConfiguration
            {
                Android = GetBooleanEnvironmentVariable("PLATFORM_ANDROID"),
                Ios = GetBooleanEnvironmentVariable("PLATFORM_IOS"),
                Web = GetBooleanEnvironmentVariable("PLATFORM_WEB"),

                // Android
                AndroidPackageName = GetEnvironmentVariable("ANDROID_PACKAGE_NAME"),
                AndroidOutputDir = GetEnvironmentVariable("ANDROID_OUTPUT_DIR", "UI/Android/style/"),
                AndroidThemeName = GetEnvironmentVariable("ANDROID_THEME_NAME"),
                AndroidComposeVersion = GetEnvironmentVariable("ANDROID_COMPOSE_VERSION", "latest"),

                // iOS
                IosModuleName = GetEnvironmentVariable("IOS_MODULE_NAME"),
                IosOutputDir = GetEnvironmentVariable("IOS_OUTPUT_DIR", "UI/iOS/style/"),
                IosThemeName = GetEnvironmentVariable("IOS_THEME_NAME"),
                IosSwiftVersion = GetEnvironmentVariable("IOS_SWIFT_VERSION", "5.9"),

                // Web
                WebTemplate = GetEnvironmentVariable("WEB_TEMPLATE", "vanilla"),
                WebOutputDir = GetEnvironmentVariable("WEB_OUTPUT_DIR", "UI/Web/style/"),
                WebCssPrefix = GetEnvironmentVariable("WEB_CSS_PREFIX"),
                WebSupportDarkMode = GetBooleanEnvironmentVariable("WEB_SUPPORT_DARK_MODE", true),

                // Web template-specific
                TailwindConfigPath = GetEnvironmentVariable("TAILWIND_CONFIG_PATH"),
                TailwindExtendTheme = GetBooleanEnvironmentVariable("TAILWIND_EXTEND_THEME", true),
                BootstrapVersion = GetEnvironmentVariable("BOOTSTRAP_VERSION", "5.3"),
                BootstrapScssPath = GetEnvironmentVariable("BOOTSTRAP_SCSS_PATH"),
                MaterialVersion = GetEnvironmentVariable("MATERIAL_VERSION", "3"),
                MaterialComponents = GetEnvironmentVariable("MATERIAL_COMPONENTS")
            };

            return config;
        }

        private LicenseConfiguration ParseLicenseConfiguration()
        {
            return new LicenseConfiguration
            {
                ServerUrl = GetRequiredEnvironmentVariable("LICENSE_SERVER"),
                ToolName = GetEnvironmentVariable("TOOL_NAME", "design-token-generator"),
                TimeoutSeconds = GetIntegerEnvironmentVariable("LICENSE_TIMEOUT", 300),
                RetryIntervalSeconds = GetIntegerEnvironmentVariable("LICENSE_RETRY_INTERVAL", 30)
            };
        }

        private KeyVaultConfiguration? ParseKeyVaultConfiguration()
        {
            var vaultType = GetEnvironmentVariable("VAULT_TYPE");
            if (string.IsNullOrWhiteSpace(vaultType))
            {
                return null;
            }

            var config = new KeyVaultConfiguration
            {
                Type = vaultType.ToLowerInvariant(),
                Url = GetEnvironmentVariable("VAULT_URL", string.Empty)
            };

            // Parse vault-specific parameters
            switch (config.Type)
            {
                case "azure":
                    config.Parameters["ClientId"] = GetEnvironmentVariable("AZURE_CLIENT_ID", string.Empty);
                    config.Parameters["ClientSecret"] = GetEnvironmentVariable("AZURE_CLIENT_SECRET", string.Empty);
                    config.Parameters["TenantId"] = GetEnvironmentVariable("AZURE_TENANT_ID", string.Empty);
                    break;

                case "aws":
                    config.Parameters["Region"] = GetEnvironmentVariable("AWS_REGION", "us-east-1");
                    config.Parameters["AccessKeyId"] = GetEnvironmentVariable("AWS_ACCESS_KEY_ID", string.Empty);
                    config.Parameters["SecretAccessKey"] = GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", string.Empty);
                    break;

                case "hashicorp":
                    config.Parameters["Token"] = GetEnvironmentVariable("VAULT_TOKEN", string.Empty);
                    break;
            }

            return config;
        }

        private TagTemplateConfiguration ParseTagTemplateConfiguration()
        {
            return new TagTemplateConfiguration
            {
                Template = GetEnvironmentVariable("TAG_TEMPLATE", "{branch}/{repo}/tokens/{version}")
            };
        }

        private OperationConfiguration ParseOperationConfiguration()
        {
            return new OperationConfiguration
            {
                Mode = GetEnvironmentVariable("MODE", "sync").ToLowerInvariant(),
                ValidateOnly = GetBooleanEnvironmentVariable("VALIDATE_ONLY"),
                NoOp = GetBooleanEnvironmentVariable("NO_OP"),
                PreserveCustom = GetBooleanEnvironmentVariable("PRESERVE_CUSTOM", true),
                MergeStrategy = GetEnvironmentVariable("MERGE_STRATEGY", "preserve-custom"),
                TokenFormat = GetEnvironmentVariable("TOKEN_FORMAT", "json")
            };
        }

        private FileManagementConfiguration ParseFileManagementConfiguration()
        {
            return new FileManagementConfiguration
            {
                OutputDir = GetEnvironmentVariable("OUTPUT_DIR", "design/tokens"),
                BaseTokensFile = GetEnvironmentVariable("BASE_TOKENS_FILE", "base.json"),
                VerticalTokensFile = GetEnvironmentVariable("VERTICAL_TOKENS_FILE", "{vertical}.json"),
                GeneratedDir = GetEnvironmentVariable("GENERATED_DIR", "generated")
            };
        }

        private GitConfiguration ParseGitConfiguration()
        {
            return new GitConfiguration
            {
                AutoCommit = GetBooleanEnvironmentVariable("AUTO_COMMIT"),
                CommitMessage = GetEnvironmentVariable("COMMIT_MESSAGE"),
                CommitAuthorName = GetEnvironmentVariable("COMMIT_AUTHOR_NAME"),
                CommitAuthorEmail = GetEnvironmentVariable("COMMIT_AUTHOR_EMAIL"),
                CreateBranch = GetBooleanEnvironmentVariable("CREATE_BRANCH"),
                BranchNameTemplate = GetEnvironmentVariable("BRANCH_NAME_TEMPLATE"),
                CreatePullRequest = GetBooleanEnvironmentVariable("CREATE_PULL_REQUEST")
            };
        }

        private LoggingConfiguration ParseLoggingConfiguration()
        {
            return new LoggingConfiguration
            {
                Verbose = GetBooleanEnvironmentVariable("VERBOSE"),
                LogLevel = GetEnvironmentVariable("LOG_LEVEL", "INFO").ToUpperInvariant()
            };
        }

        private AuthenticationConfiguration ParseAuthenticationConfiguration()
        {
            return new AuthenticationConfiguration
            {
                PatToken = GetEnvironmentVariable("PAT_TOKEN"),
                PatSecretName = GetEnvironmentVariable("PAT_SECRET_NAME"),
                PipelineToken = GetPipelineToken()
            };
        }

        private string? GetPipelineToken()
        {
            // Try Azure DevOps first
            var adoToken = GetEnvironmentVariable("System.AccessToken");
            if (!string.IsNullOrWhiteSpace(adoToken))
            {
                return adoToken;
            }

            // Try GitHub Actions
            var githubToken = GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(githubToken))
            {
                return githubToken;
            }

            // Try Jenkins (common variable names)
            var jenkinsToken = GetEnvironmentVariable("GIT_TOKEN") ?? GetEnvironmentVariable("SCM_TOKEN");
            return jenkinsToken;
        }

        private void ValidateDesignPlatformSpecificConfig(DesignPlatformConfiguration config, List<string> errors)
        {
            var platform = config.GetSelectedPlatform();

            switch (platform)
            {
                case "figma":
                    if (string.IsNullOrWhiteSpace(config.FigmaUrl))
                        errors.Add("FIGMA_URL is required when DESIGN_FIGMA=true");
                    if (string.IsNullOrWhiteSpace(config.FigmaTokenVaultKey))
                        errors.Add("FIGMA_TOKEN_VAULT_KEY is required when DESIGN_FIGMA=true");
                    break;

                case "sketch":
                    if (string.IsNullOrWhiteSpace(config.SketchWorkspaceId))
                        errors.Add("SKETCH_WORKSPACE_ID is required when DESIGN_SKETCH=true");
                    if (string.IsNullOrWhiteSpace(config.SketchDocumentId))
                        errors.Add("SKETCH_DOCUMENT_ID is required when DESIGN_SKETCH=true");
                    if (string.IsNullOrWhiteSpace(config.SketchTokenVaultKey))
                        errors.Add("SKETCH_TOKEN_VAULT_KEY is required when DESIGN_SKETCH=true");
                    break;

                case "adobe-xd":
                    if (string.IsNullOrWhiteSpace(config.XdProjectUrl))
                        errors.Add("XD_PROJECT_URL is required when DESIGN_ADOBE_XD=true");
                    if (string.IsNullOrWhiteSpace(config.XdTokenVaultKey))
                        errors.Add("XD_TOKEN_VAULT_KEY is required when DESIGN_ADOBE_XD=true");
                    break;

                case "zeplin":
                    if (string.IsNullOrWhiteSpace(config.ZeplinProjectId))
                        errors.Add("ZEPLIN_PROJECT_ID is required when DESIGN_ZEPLIN=true");
                    if (string.IsNullOrWhiteSpace(config.ZeplinTokenVaultKey))
                        errors.Add("ZEPLIN_TOKEN_VAULT_KEY is required when DESIGN_ZEPLIN=true");
                    break;

                case "abstract":
                    if (string.IsNullOrWhiteSpace(config.AbstractProjectId))
                        errors.Add("ABSTRACT_PROJECT_ID is required when DESIGN_ABSTRACT=true");
                    if (string.IsNullOrWhiteSpace(config.AbstractTokenVaultKey))
                        errors.Add("ABSTRACT_TOKEN_VAULT_KEY is required when DESIGN_ABSTRACT=true");
                    break;

                case "penpot":
                    if (string.IsNullOrWhiteSpace(config.PenpotFileId))
                        errors.Add("PENPOT_FILE_ID is required when DESIGN_PENPOT=true");
                    if (string.IsNullOrWhiteSpace(config.PenpotTokenVaultKey))
                        errors.Add("PENPOT_TOKEN_VAULT_KEY is required when DESIGN_PENPOT=true");
                    break;
            }
        }

        private void ValidateTagTemplate(string template, string templateName)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new ArgumentException($"{templateName} cannot be empty");
            }

            var supportedTokens = new[]
            {
                "branch", "repo", "version", "major", "minor", "patch",
                "date", "datetime", "commit-hash", "commit-hash-full",
                "build-number", "user", "design-platform", "platform", "vertical"
            };

            // Find all tokens in the template
            var tokens = new List<string>();
            var start = 0;
            while (true)
            {
                var openBrace = template.IndexOf('{', start);
                if (openBrace == -1) break;

                var closeBrace = template.IndexOf('}', openBrace);
                if (closeBrace == -1)
                {
                    throw new ArgumentException($"Invalid {templateName}: Unclosed token at position {openBrace}");
                }

                var token = template.Substring(openBrace + 1, closeBrace - openBrace - 1);
                tokens.Add(token);
                start = closeBrace + 1;
            }

            // Validate all tokens are supported
            foreach (var token in tokens)
            {
                if (!supportedTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Invalid {templateName}: Unknown token '{{{token}}}'. Supported tokens: {string.Join(", ", supportedTokens.Select(t => $"{{{t}}}"))}");
                }
            }
        }

        private string GetRequiredEnvironmentVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration,
                    $"Required environment variable {name} is not set or is empty.");
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
            return bool.TryParse(value, out var result) ? result : defaultValue;
        }

        private int GetIntegerEnvironmentVariable(string name, int defaultValue)
        {
            var value = GetEnvironmentVariable(name);
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private string MaskSensitiveUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            try
            {
                var uri = new Uri(url);
                return $"{uri.Scheme}://{uri.Host}{uri.PathAndQuery}";
            }
            catch
            {
                return url;
            }
        }
    }
}