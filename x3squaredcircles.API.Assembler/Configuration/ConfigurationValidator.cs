using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Configuration
{
    /// <summary>
    /// Validates the AssemblerConfiguration to ensure all required settings are present and consistent before execution.
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
        /// <param name="config">The AssemblerConfiguration object to validate.</param>
        /// <exception cref="AssemblerException">Thrown if validation fails, with a specific exit code.</exception>
        public void Validate(AssemblerConfiguration config)
        {
            var errors = new List<string>();
            _logger.LogDebug("Starting configuration validation process.");

            // Validate language and cloud selections
            if (string.IsNullOrWhiteSpace(config.Language))
            {
                errors.Add("No source language specified. Set exactly one language flag to true (e.g., LANGUAGE_CSHARP=true).");
            }
            if (string.IsNullOrWhiteSpace(config.Cloud))
            {
                errors.Add("No target cloud specified. Set exactly one cloud flag to true (e.g., CLOUD_AZURE=true).");
            }

            // Validate required core settings
            if (string.IsNullOrWhiteSpace(config.RepoUrl)) errors.Add("Required environment variable REPO_URL is not set.");
            if (string.IsNullOrWhiteSpace(config.Branch)) errors.Add("Required environment variable BRANCH is not set.");
            if (string.IsNullOrWhiteSpace(config.AssemblerEnv)) errors.Add("Required environment variable ASSEMBLER_ENV is not set.");
            if (string.IsNullOrWhiteSpace(config.License.ServerUrl)) errors.Add("Required environment variable LICENSE_SERVER is not set.");

            // Validate required paths
            if (string.IsNullOrWhiteSpace(config.Libs)) errors.Add("Required environment variable ASSEMBLER_LIBS is not set.");
            if (string.IsNullOrWhiteSpace(config.Sources)) errors.Add("Required environment variable ASSEMBLER_SOURCES is not set.");

            if (errors.Count > 0)
            {
                var errorMessage = $"Configuration validation failed with {errors.Count} error(s):\n- {string.Join("\n- ", errors)}";
                _logger.LogError(errorMessage);
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, errorMessage);
            }

            _logger.LogInformation("✓ Configuration validation passed.");
        }
    }
}