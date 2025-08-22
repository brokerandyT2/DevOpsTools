using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
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

            // The 'isRequired' flag in the loader handles most presence checks.
            // This method is for validating the *content* of the variables.

            if (string.IsNullOrWhiteSpace(config.Language))
            {
                errors.Add("No source language specified. Use ASSEMBLER_LANGUAGE or 3SC_LANGUAGE to set a supported language (e.g., 'csharp').");
            }

            if (string.IsNullOrWhiteSpace(config.Cloud))
            {
                errors.Add("No target cloud specified. Use ASSEMBLER_CLOUD_PROVIDER or 3SC_CLOUD_PROVIDER to set a supported cloud (e.g., 'azure').");
            }

            // Example of a more complex, cross-variable validation
            if (!string.IsNullOrWhiteSpace(config.Vault.Url) && string.IsNullOrWhiteSpace(config.Vault.Type))
            {
                errors.Add("If ASSEMBLER_VAULT_URL or 3SC_VAULT_URL is set, you must also specify a vault type using ASSEMBLER_VAULT_TYPE or 3SC_VAULT_TYPE.");
            }

            if (errors.Any())
            {
                var errorMessage = $"Configuration validation failed with {errors.Count} error(s):\n- {string.Join("\n- ", errors)}";
                _logger.LogError(errorMessage);
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, errorMessage);
            }

            _logger.LogInformation("✓ Configuration validation passed.");
        }
    }
}