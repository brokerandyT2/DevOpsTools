using Microsoft.Extensions.Logging;
using System;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Configuration
{
    /// <summary>
    /// A static class responsible for loading all application configuration from environment variables
    /// into a strongly-typed AssemblerConfiguration object, adhering to the 3SC Universal Override Protocol.
    /// </summary>
    public static class EnvironmentConfigurationLoader
    {
        private const string ToolPrefix = "ASSEMBLER_";
        private const string CommonPrefix = "3SC_";

        /// <summary>
        /// Loads and parses all known environment variables.
        /// </summary>
        /// <returns>A populated AssemblerConfiguration instance.</returns>
        public static AssemblerConfiguration LoadConfiguration()
        {
            var config = new AssemblerConfiguration
            {
                // Common Platform Settings (resolved using override protocol)
                RepoUrl = GetString("REPO_URL", isRequired: true),
                Branch = GetString("BRANCH", isRequired: true),
                NoOp = GetBool("NO_OP", true), // Default to true for safety
                ValidateOnly = GetBool("VALIDATE_ONLY", false),

                // Tool-Specific Settings
                Language = GetString("LANGUAGE", isRequired: true),
                Cloud = GetString("CLOUD", isRequired: true),
                AssemblerEnv = GetString("ENV", "dev"),
                Libs = GetString("LIBS", isRequired: true),
                Sources = GetString("SOURCES", isRequired: true),
                OutputPath = GetString("OUTPUT_PATH", "./output"),
                TagTemplate = new TagTemplateConfiguration
                {
                    Template = GetString("TAG_TEMPLATE", "{repo}/{group}/{version}")
                },

                // Nested Configuration Objects
                License = new LicenseConfiguration
                {
                    ServerUrl = GetString("LICENSE_SERVER", isRequired: true),
                    TimeoutSeconds = GetInt("LICENSE_TIMEOUT", 300),
                    RetryIntervalSeconds = GetInt("LICENSE_RETRY_INTERVAL", 30)
                },
                Vault = new VaultConfiguration
                {
                    Type = GetString("VAULT_TYPE"),
                    Url = GetString("VAULT_URL")
                },
                Logging = new LoggingConfiguration
                {
                    LogLevel = GetEnum<LogLevel>("LOG_LEVEL", LogLevel.Information),
                    ExternalLogEndpoint = GetString("LOG_ENDPOINT_URL"),
                    ExternalLogToken = GetString("LOG_ENDPOINT_TOKEN")
                },
                ControlPoints = new ControlPointsConfiguration
                {
                    Logging = GetString("CP_LOGGING"),
                    OnStartup = GetString("CP_ON_STARTUP"),
                    OnSuccess = GetString("CP_ON_SUCCESS"),
                    OnFailure = GetString("CP_ON_FAILURE")
                }
            };

            return config;
        }

        #region Standardized Getters (Implements Universal Override Protocol)

        private static string GetString(string suffix, string defaultValue = "", bool isRequired = false)
        {
            var toolVar = $"{ToolPrefix}{suffix}";
            var commonVar = $"{CommonPrefix}{suffix}";

            var value = Environment.GetEnvironmentVariable(toolVar)
                     ?? Environment.GetEnvironmentVariable(commonVar)
                     ?? defaultValue;

            if (isRequired && string.IsNullOrWhiteSpace(value))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Required configuration not found. Set either '{toolVar}' or '{commonVar}'.");
            }
            return value;
        }

        private static bool GetBool(string suffix, bool defaultValue = false)
        {
            var toolVar = $"{ToolPrefix}{suffix}";
            var commonVar = $"{CommonPrefix}{suffix}";

            var valueStr = Environment.GetEnvironmentVariable(toolVar)
                        ?? Environment.GetEnvironmentVariable(commonVar);

            if (string.IsNullOrEmpty(valueStr)) return defaultValue;

            return string.Equals(valueStr, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(valueStr, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(string suffix, int defaultValue)
        {
            var toolVar = $"{ToolPrefix}{suffix}";
            var commonVar = $"{CommonPrefix}{suffix}";

            var valueStr = Environment.GetEnvironmentVariable(toolVar)
                        ?? Environment.GetEnvironmentVariable(commonVar);

            return int.TryParse(valueStr, out var result) ? result : defaultValue;
        }

        private static T GetEnum<T>(string suffix, T defaultValue) where T : struct, Enum
        {
            var toolVar = $"{ToolPrefix}{suffix}";
            var commonVar = $"{CommonPrefix}{suffix}";

            var valueStr = Environment.GetEnvironmentVariable(toolVar)
                        ?? Environment.GetEnvironmentVariable(commonVar);

            return Enum.TryParse<T>(valueStr, true, out var result) ? result : defaultValue;
        }

        #endregion
    }
}