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
                GenerateTestHarness = GetBooleanEnvironmentVariable("DATALINK_GENERATE_TEST_HARNESS", true)
            };

            ValidateConfiguration(config);

            return config;
        }

        private void ValidateConfiguration(DataLinkConfiguration config)
        {
            var errors = new List<string>();

            // The GetRequiredEnvironmentVariable method already handles presence checks.
            // This method is for more complex, cross-variable validation like URL formatting.

            if (!IsWellFormedGitUrl(config.BusinessLogicRepo))
            {
                errors.Add($"'DATALINK_BUSINESS_LOGIC_REPO' is not a valid Git repository URL: {config.BusinessLogicRepo}");
            }

            if (!string.IsNullOrEmpty(config.TestHarnessRepo) && !IsWellFormedGitUrl(config.TestHarnessRepo))
            {
                errors.Add($"'DATALINK_TEST_HARNESS_REPO' must be a valid Git repository URL if provided: {config.TestHarnessRepo}");
            }

            if (!IsWellFormedGitUrl(config.DestinationRepo))
            {
                errors.Add($"'DATALINK_DESTINATION_REPO' is not a valid Git repository URL: {config.DestinationRepo}");
            }

            if (errors.Any())
            {
                var errorMessage = "Configuration validation failed with the following errors:\n" +
                                   string.Join("\n", errors.Select(e => $"  - {e}"));

                throw new DataLinkException(ExitCode.InvalidConfiguration, "CONFIG_VALIDATION_FAILED", errorMessage);
            }
        }

        private bool IsWellFormedGitUrl(string url)
        {
            // A simple check for common Git URL formats.
            return Uri.IsWellFormedUriString(url, UriKind.Absolute) && (url.StartsWith("https://") || url.StartsWith("git@"));
        }

        private string GetRequiredEnvironmentVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new DataLinkException(
                    ExitCode.InvalidConfiguration,
                    "MISSING_REQUIRED_ENV_VAR",
                    $"Required environment variable '{name}' is not set or is empty.");
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
            // Treat "1" as true for CI/CD system compatibility
            return bool.TryParse(value, out var result) ? result : (value == "1" || defaultValue);
        }
    }
}