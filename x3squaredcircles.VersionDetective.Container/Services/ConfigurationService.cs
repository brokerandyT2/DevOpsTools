using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface IConfigurationService
    {
        VersionDetectiveConfiguration GetConfiguration();
        void ValidateConfiguration(VersionDetectiveConfiguration config);
        void LogConfiguration(VersionDetectiveConfiguration config);
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
        }

        public VersionDetectiveConfiguration GetConfiguration()
        {
            _logger.LogDebug("Parsing configuration from environment variables");

            var config = new VersionDetectiveConfiguration
            {
                Language = ParseLanguageConfiguration(),
                TrackAttribute = GetRequiredEnvironmentVariable("TRACK_ATTRIBUTE"),
                RepoUrl = GetRequiredEnvironmentVariable("REPO_URL"),
                Branch = GetRequiredEnvironmentVariable("BRANCH"),
                License = ParseLicenseConfiguration(),
                KeyVault = ParseKeyVaultConfiguration(),
                TagTemplate = ParseTagTemplateConfiguration(),
                Analysis = ParseAnalysisConfiguration(),
                Logging = ParseLoggingConfiguration(),
                Authentication = ParseAuthenticationConfiguration()
            };

            return config;
        }

        public void ValidateConfiguration(VersionDetectiveConfiguration config)
        {
            _logger.LogDebug("Validating configuration");

            var errors = new List<string>();

            // Validate language selection
            if (!config.Language.HasSingleLanguageSelected())
            {
                if (config.Language.GetSelectedLanguage() == string.Empty)
                {
                    errors.Add("No language specified. Set exactly one: LANGUAGE_CSHARP, LANGUAGE_JAVA, LANGUAGE_PYTHON, LANGUAGE_JAVASCRIPT, LANGUAGE_TYPESCRIPT, or LANGUAGE_GO to 'true'.");
                }
                else
                {
                    errors.Add("Multiple languages specified. Set exactly one language environment variable to 'true'.");
                }
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(config.TrackAttribute))
            {
                errors.Add("TRACK_ATTRIBUTE is required.");
            }

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

            // Validate tag templates
            try
            {
                ValidateTagTemplate(config.TagTemplate.Template, "TAG_TEMPLATE");
                ValidateTagTemplate(config.TagTemplate.MarketingTemplate, "MARKETING_TAG_TEMPLATE");
            }
            catch (Exception ex)
            {
                errors.Add($"Tag template validation failed: {ex.Message}");
            }

            if (errors.Any())
            {
                var errorMessage = "Configuration validation failed:\n" + string.Join("\n", errors.Select(e => $"  - {e}"));
                _logger.LogError(errorMessage);
                throw new VersionDetectiveException(VersionDetectiveExitCode.InvalidConfiguration, errorMessage);
            }

            _logger.LogInformation("✓ Configuration validation passed");
        }

        public void LogConfiguration(VersionDetectiveConfiguration config)
        {
            _logger.LogInformation("=== Version Detective Configuration ===");
            _logger.LogInformation("Language: {Language}", config.Language.GetSelectedLanguage().ToUpperInvariant());
            _logger.LogInformation("Track Attribute: {TrackAttribute}", config.TrackAttribute);
            _logger.LogInformation("Repository: {RepoUrl}", config.RepoUrl);
            _logger.LogInformation("Branch: {Branch}", config.Branch);
            _logger.LogInformation("License Server: {LicenseServer}", MaskSensitiveUrl(config.License.ServerUrl));
            _logger.LogInformation("Mode: {Mode}", config.Analysis.Mode.ToUpperInvariant());
            _logger.LogInformation("Tag Template: {TagTemplate}", config.TagTemplate.Template);
            _logger.LogInformation("Marketing Template: {MarketingTemplate}", config.TagTemplate.MarketingTemplate);

            if (config.Analysis.ValidateOnly)
            {
                _logger.LogInformation("VALIDATE ONLY mode enabled");
            }

            if (config.Analysis.NoOp)
            {
                _logger.LogInformation("NO-OP mode enabled");
            }

            if (config.KeyVault != null)
            {
                _logger.LogInformation("Key Vault: {VaultType} - {VaultUrl}", config.KeyVault.Type.ToUpperInvariant(), MaskSensitiveUrl(config.KeyVault.Url));
            }

            _logger.LogInformation("========================================");
        }

        private LanguageConfiguration ParseLanguageConfiguration()
        {
            return new LanguageConfiguration
            {
                CSharp = GetBooleanEnvironmentVariable("LANGUAGE_CSHARP"),
                Java = GetBooleanEnvironmentVariable("LANGUAGE_JAVA"),
                Python = GetBooleanEnvironmentVariable("LANGUAGE_PYTHON"),
                JavaScript = GetBooleanEnvironmentVariable("LANGUAGE_JAVASCRIPT"),
                TypeScript = GetBooleanEnvironmentVariable("LANGUAGE_TYPESCRIPT"),
                Go = GetBooleanEnvironmentVariable("LANGUAGE_GO")
            };
        }

        private LicenseConfiguration ParseLicenseConfiguration()
        {
            return new LicenseConfiguration
            {
                ServerUrl = GetRequiredEnvironmentVariable("LICENSE_SERVER"),
                ToolName = GetEnvironmentVariable("TOOL_NAME", Assembly.GetExecutingAssembly().GetName().Name?.ToString() ?? "version-detective"),
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
                Template = GetEnvironmentVariable("TAG_TEMPLATE", "{branch}/{repo}/semver/{version}"),
                MarketingTemplate = GetEnvironmentVariable("MARKETING_TAG_TEMPLATE", "{branch}/{repo}/marketing/{version}")
            };
        }

        private AnalysisConfiguration ParseAnalysisConfiguration()
        {
            var dllPathsStr = GetEnvironmentVariable("DLL_PATHS", string.Empty);
            var dllPaths = string.IsNullOrWhiteSpace(dllPathsStr)
                ? new List<string>()
                : dllPathsStr.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();

            return new AnalysisConfiguration
            {
                FromCommit = GetEnvironmentVariable("FROM"),
                Mode = GetEnvironmentVariable("MODE", "pr").ToLowerInvariant(),
                ValidateOnly = GetBooleanEnvironmentVariable("VALIDATE_ONLY"),
                NoOp = GetBooleanEnvironmentVariable("NO_OP"),
                DllPaths = dllPaths,
                BuildOutputPath = GetEnvironmentVariable("BUILD_OUTPUT_PATH")
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
                "build-number", "user"
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
                throw new VersionDetectiveException(VersionDetectiveExitCode.InvalidConfiguration,
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