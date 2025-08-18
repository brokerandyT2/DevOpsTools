using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using x3squaredcircles.SQLSentry.Container.Models;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    /// <summary>
    /// Defines the contract for the configuration service.
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>
        /// Reads, validates, and returns the application configuration from environment variables.
        ///</summary>
        GuardianConfiguration GetConfiguration();

        /// <summary>
        /// Logs the loaded configuration to the console for auditability, masking any secrets.
        ///</summary>
        void LogConfiguration(GuardianConfiguration config);
    }

    /// <summary>
    /// Manages the loading and validation of application configuration from environment variables.
    /// </summary>
    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public GuardianConfiguration GetConfiguration()
        {
            _logger.LogDebug("Loading configuration from environment variables.");

            var config = new GuardianConfiguration
            {
                // Database Connection
                DbConnectionString = GetEnvironmentVariable("DB_CONNECTION_STRING"),
                DbVaultKey = GetEnvironmentVariable("DB_VAULT_KEY"),
                VaultProvider = GetEnvironmentVariable("THREE_SC_VAULT_PROVIDER"),
                VaultUrl = GetEnvironmentVariable("THREE_SC_VAULT_URL"),

                // Repository Connection
                GitRepoUrl = GetRequiredEnvironmentVariable("GIT_REPO_URL"),
                GitPat = GetRequiredEnvironmentVariable("GIT_PAT"),
                GitSqlFilePath = GetRequiredEnvironmentVariable("GIT_SQL_FILE_PATH"),

                // Optional File Paths
                ExceptionsFilePath = GetEnvironmentVariable("GUARDIAN_EXCEPTIONS_FILE_PATH", "./guardian.exceptions.json"),
                PatternsFilePath = GetEnvironmentVariable("GUARDIAN_PATTERNS_FILE_PATH", "./guardian.patterns.json"),

                // Operational Overrides
                ContinueOnFailure = GetBooleanEnvironmentVariable("GUARDIAN_CONTINUE_ON_FAILURE")
            };

            ValidateConfiguration(config);

            _logger.LogInformation("✓ Configuration loaded and validated successfully.");
            return config;
        }

        /// <inheritdoc />
        public void LogConfiguration(GuardianConfiguration config)
        {
            _logger.LogInformation("--- Guardian Configuration ---");
            _logger.LogInformation("Git Repository: {RepoUrl}", config.GitRepoUrl);
            _logger.LogInformation("SQL File Path: {SqlFilePath}", config.GitSqlFilePath);
            _logger.LogInformation("Exceptions File: {ExceptionsFile}", config.ExceptionsFilePath);
            _logger.LogInformation("Custom Patterns File: {PatternsFile}", config.PatternsFilePath);

            if (!string.IsNullOrEmpty(config.DbConnectionString))
            {
                _logger.LogInformation("Database Connection: Direct Connection String (secret masked)");
            }
            else
            {
                _logger.LogInformation("Database Connection: Vault Key ({VaultKey})", config.DbVaultKey);
                _logger.LogInformation("Vault Provider: {VaultProvider}", config.VaultProvider);
                _logger.LogInformation("Vault URL: {VaultUrl}", config.VaultUrl);
            }

            if (config.ContinueOnFailure)
            {
                _logger.LogWarning(">> EMERGENCY OVERRIDE: Continue on Failure is ENABLED.");
            }
            _logger.LogInformation("----------------------------");
        }

        private void ValidateConfiguration(GuardianConfiguration config)
        {
            var errors = new List<string>();

            // Validate Database Connection
            bool hasDirectConnectionString = !string.IsNullOrWhiteSpace(config.DbConnectionString);
            bool hasVaultKey = !string.IsNullOrWhiteSpace(config.DbVaultKey);
            bool hasVaultConfig = !string.IsNullOrWhiteSpace(config.VaultProvider) && !string.IsNullOrWhiteSpace(config.VaultUrl);

            if (!hasDirectConnectionString && !hasVaultKey)
            {
                errors.Add("Database connection is not configured. You must provide either 'DB_CONNECTION_STRING' or 'DB_VAULT_KEY'.");
            }
            if (hasVaultKey && !hasVaultConfig)
            {
                errors.Add("When using 'DB_VAULT_KEY', you must also provide 'THREE_SC_VAULT_PROVIDER' and 'THREE_SC_VAULT_URL'.");
            }

            // The required Git variables are already validated by GetRequiredEnvironmentVariable.
            // No further validation is needed here for them.

            if (errors.Count > 0)
            {
                var errorMessage = "Configuration validation failed with the following errors:\n" +
                                   string.Join("\n", errors.Select(e => $"  - {e}"));

                throw new GuardianException(ExitCode.InvalidConfiguration, "CONFIG_VALIDATION_FAILED", errorMessage);
            }
        }

        private string GetRequiredEnvironmentVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new GuardianException(
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
            return bool.TryParse(value, out var result) ? result : defaultValue;
        }
    }
}