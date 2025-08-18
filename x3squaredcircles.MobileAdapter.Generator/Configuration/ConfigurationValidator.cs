using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.Configuration
{
    /// <summary>
    /// Validates the GeneratorConfiguration to ensure all required settings are present and consistent.
    /// </summary>
    public class ConfigurationValidator
    {
        private readonly ILogger<ConfigurationValidator> _logger;

        public ConfigurationValidator(ILogger<ConfigurationValidator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Performs a comprehensive validation of the provided application configuration.
        /// </summary>
        /// <param name="config">The GeneratorConfiguration object to validate.</param>
        /// <returns>A ValidationResult containing the outcome and a list of any errors found.</returns>
        public ValidationResult Validate(GeneratorConfiguration config)
        {
            var errors = new List<string>();
            _logger.LogDebug("Starting configuration validation process.");

            // Validate language and platform selections
            ValidateLanguageSelection(config, errors);
            ValidatePlatformSelection(config, errors);

            // Validate core operational settings
            ValidateCoreConfiguration(config, errors);
            ValidateLicensingConfiguration(config, errors);
            ValidateDiscoveryConfiguration(config, errors);

            // Validate language-specific path and source requirements
            ValidateLanguageSpecificConfiguration(config, errors);

            // Validate output and vault settings
            ValidateOutputConfiguration(config, errors);
            ValidateVaultConfiguration(config, errors);

            if (errors.Any())
            {
                _logger.LogError("Configuration validation failed with {ErrorCount} errors.", errors.Count);
            }

            return new ValidationResult
            {
                IsValid = !errors.Any(),
                Errors = errors
            };
        }

        private void ValidateLanguageSelection(GeneratorConfiguration config, List<string> errors)
        {
            if (config.GetSelectedLanguage() == SourceLanguage.None)
            {
                errors.Add("No source language specified. Set exactly one language flag to true (e.g., LANGUAGE_CSHARP=true).");
            }
        }

        private void ValidatePlatformSelection(GeneratorConfiguration config, List<string> errors)
        {
            if (config.GetSelectedPlatform() == TargetPlatform.None)
            {
                errors.Add("No target platform specified. Set exactly one platform flag to true (e.g., PLATFORM_ANDROID=true).");
            }
        }

        private void ValidateCoreConfiguration(GeneratorConfiguration config, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(config.RepoUrl)) errors.Add("Required environment variable REPO_URL is not set.");
            if (string.IsNullOrWhiteSpace(config.Branch)) errors.Add("Required environment variable BRANCH is not set.");
        }

        private void ValidateLicensingConfiguration(GeneratorConfiguration config, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(config.LicenseServer)) errors.Add("Required environment variable LICENSE_SERVER is not set.");
            if (config.LicenseTimeout <= 0) errors.Add("LICENSE_TIMEOUT must be a positive integer.");
            if (config.LicenseRetryInterval <= 0) errors.Add("LICENSE_RETRY_INTERVAL must be a positive integer.");
        }

        private void ValidateDiscoveryConfiguration(GeneratorConfiguration config, List<string> errors)
        {
            var discoveryMethods = new[]
            {
                !string.IsNullOrWhiteSpace(config.TrackAttribute),
                !string.IsNullOrWhiteSpace(config.TrackPattern),
                !string.IsNullOrWhiteSpace(config.TrackNamespace),
                !string.IsNullOrWhiteSpace(config.TrackFilePattern)
            };

            if (discoveryMethods.Count(isSet => isSet) == 0)
            {
                errors.Add("No discovery method specified. Set one of: TRACK_ATTRIBUTE, TRACK_PATTERN, TRACK_NAMESPACE, or TRACK_FILE_PATTERN.");
            }
            else if (discoveryMethods.Count(isSet => isSet) > 1)
            {
                errors.Add("Multiple discovery methods specified. Only one TRACK_* variable can be used per execution.");
            }
        }

        private void ValidateLanguageSpecificConfiguration(GeneratorConfiguration config, List<string> errors)
        {
            switch (config.GetSelectedLanguage())
            {
                case SourceLanguage.CSharp:
                    if (string.IsNullOrWhiteSpace(config.Assembly.CoreAssemblyPath) && string.IsNullOrWhiteSpace(config.Assembly.TargetAssemblyPath))
                        errors.Add("For C# analysis, either CORE_ASSEMBLY_PATH or TARGET_ASSEMBLY_PATH must be specified.");
                    break;
                case SourceLanguage.Java:
                case SourceLanguage.Kotlin:
                case SourceLanguage.JavaScript:
                case SourceLanguage.TypeScript:
                    if (string.IsNullOrWhiteSpace(config.Source.SourcePaths))
                        errors.Add($"For {config.GetSelectedLanguage()} analysis, SOURCE_PATHS must be specified.");
                    break;
                case SourceLanguage.Python:
                    if (string.IsNullOrWhiteSpace(config.Source.PythonPaths))
                        errors.Add("For Python analysis, PYTHON_PATHS must be specified.");
                    break;
            }
        }

        private void ValidateOutputConfiguration(GeneratorConfiguration config, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(config.Output.OutputDir)) errors.Add("OUTPUT_DIR cannot be empty.");

            var selectedPlatform = config.GetSelectedPlatform();
            if (selectedPlatform == TargetPlatform.Android && string.IsNullOrWhiteSpace(config.Output.AndroidOutputDir))
            {
                errors.Add("ANDROID_OUTPUT_DIR cannot be empty when targeting Android.");
            }
            else if (selectedPlatform == TargetPlatform.iOS && string.IsNullOrWhiteSpace(config.Output.IosOutputDir))
            {
                errors.Add("IOS_OUTPUT_DIR cannot be empty when targeting iOS.");
            }
        }

        private void ValidateVaultConfiguration(GeneratorConfiguration config, List<string> errors)
        {
            if (config.Vault.Type == VaultType.None) return;

            if (string.IsNullOrWhiteSpace(config.Vault.Url)) errors.Add("VAULT_URL is required when VAULT_TYPE is specified.");

            switch (config.Vault.Type)
            {
                case VaultType.Azure:
                    if (string.IsNullOrWhiteSpace(config.Vault.AzureClientId)) errors.Add("AZURE_CLIENT_ID is required for Azure Key Vault.");
                    if (string.IsNullOrWhiteSpace(config.Vault.AzureClientSecret)) errors.Add("AZURE_CLIENT_SECRET is required for Azure Key Vault.");
                    if (string.IsNullOrWhiteSpace(config.Vault.AzureTenantId)) errors.Add("AZURE_TENANT_ID is required for Azure Key Vault.");
                    break;
                case VaultType.Aws:
                    if (string.IsNullOrWhiteSpace(config.Vault.AwsRegion)) errors.Add("AWS_REGION is required for AWS Secrets Manager.");
                    if (string.IsNullOrWhiteSpace(config.Vault.AwsAccessKeyId)) errors.Add("AWS_ACCESS_KEY_ID is required for AWS Secrets Manager.");
                    if (string.IsNullOrWhiteSpace(config.Vault.AwsSecretAccessKey)) errors.Add("AWS_SECRET_ACCESS_KEY is required for AWS Secrets Manager.");
                    break;
                case VaultType.HashiCorp:
                    if (string.IsNullOrWhiteSpace(config.Vault.HashiCorpToken)) errors.Add("VAULT_TOKEN is required for HashiCorp Vault.");
                    break;
            }
        }
    }

    /// <summary>
    /// Represents the result of a configuration validation check.
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}