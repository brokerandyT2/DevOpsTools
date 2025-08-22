using System;
using System.Collections.Generic;
using System.Linq;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines the contract for the configuration service.
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>
        /// Reads all environment variables, validates them, and returns a strongly-typed configuration object.
        /// </summary>
        /// <returns>A validated DataLinkConfiguration object.</returns>
        DataLinkConfiguration GetConfiguration();
    }

    /// <summary>
    /// Manages the loading and validation of application configuration from environment variables.
    /// This service acts as the initial gatekeeper, ensuring the tool has all necessary inputs to run.
    /// </summary>
    public class ConfigurationService : IConfigurationService
    {
        /// <inheritdoc />
        public DataLinkConfiguration GetConfiguration()
        {
            var config = new DataLinkConfiguration
            {
                // Repository Configuration
                BusinessLogicRepo = GetRequiredEnvironmentVariable("DATALINK_BUSINESS_LOGIC_REPO"),
                TestHarnessRepo = GetEnvironmentVariable("DATALINK_TEST_HARNESS_REPO"),
                DestinationRepo = GetRequiredEnvironmentVariable("DATALINK_DESTINATION_REPO"),
                DestinationRepoPat = GetRequiredEnvironmentVariable("DATALINK_DESTINATION_REPO_PAT"),

                // Versioning and Tagging
                VersionTagPattern = GetEnvironmentVariable("DATALINK_VERSION_TAG_PATTERN", "v*"),

                // Logging
                Verbose = GetBooleanEnvironmentVariable("DATALINK_VERBOSE"),
                LogLevel = GetEnvironmentVariable("DATALINK_LOG_LEVEL", "INFO"),

                // Operational Overrides & Flags
                ContinueOnTestFailure = GetBooleanEnvironmentVariable("DATALINK_CONTINUE_ON_TEST_FAILURE"),
                GenerateTestHarness = GetBooleanEnvironmentVariable("DATALINK_GENERATE_TEST_HARNESS", true),

                // NEW: Business rule to enable variable discovery and exit mode.
                // Renamed from ASSEMBLER_LIST_VARIABLES to match the project's current naming.
                ListVariablesAndExit = GetBooleanEnvironmentVariable("DATALINK_LIST_VARIABLES"),

                // Target platform configuration (to be used by weavers)
                TargetLanguage = GetRequiredEnvironmentVariable("DATALINK_TARGET_LANGUAGE"),
                CloudProvider = GetRequiredEnvironmentVariable("DATALINK_CLOUD_PROVIDER"),
                DeploymentPattern = GetRequiredEnvironmentVariable("DATALINK_DEPLOYMENT_PATTERN"),
                OutputPath = GetEnvironmentVariable("DATALINK_OUTPUT_PATH", "./output"),
                ControlPointDeploymentOverrideUrl = GetEnvironmentVariable("DATALINK_CP_DEPLOYMENT_TOOL"),
                // NEW: Read Firehose Logging environment variables
                LogEndpointUrl = GetEnvironmentVariable("DATALINK_LOG_ENDPOINT_URL"),
                LogEndpointToken = GetEnvironmentVariable("DATALINK_LOG_ENDPOINT_TOKEN")
            };

            ValidateConfiguration(config);

            return config;
        }

        private void ValidateConfiguration(DataLinkConfiguration config)
        {
            var errors = new List<string>();

            if (!IsWellFormedGitUrl(config.BusinessLogicRepo))
            {
                errors.Add($"'DATALINK_BUSINESS_LOGIC_REPO' is not a valid Git repository URL: {config.BusinessLogicRepo}");
            }
            // ... other validations
            if (errors.Any())
            {
                var errorMessage = "Configuration validation failed:\n" + string.Join("\n", errors.Select(e => $"  - {e}"));
                throw new DataLinkException(ExitCode.InvalidConfiguration, "CONFIG_VALIDATION_FAILED", errorMessage);
            }
        }

        private bool IsWellFormedGitUrl(string url)
        {
            return Uri.IsWellFormedUriString(url, UriKind.Absolute) && (url.StartsWith("https://") || url.StartsWith("git@"));
        }

        private string GetRequiredEnvironmentVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new DataLinkException(ExitCode.InvalidConfiguration, "MISSING_REQUIRED_ENV_VAR", $"Required environment variable '{name}' is not set.");
            }
            return value;
        }

        private string? GetEnvironmentVariable(string name, string? defaultValue = null)
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        private bool GetBooleanEnvironmentVariable(string name, bool defaultValue = false)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) return defaultValue;
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }
    }
}