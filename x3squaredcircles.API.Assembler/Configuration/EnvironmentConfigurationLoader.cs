using Microsoft.Extensions.Logging;
using System;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Configuration
{
    /// <summary>
    /// A static class responsible for loading all application configuration from environment variables
    /// into a strongly-typed AssemblerConfiguration object.
    /// </summary>
    public static class EnvironmentConfigurationLoader
    {
        /// <summary>
        /// Loads and parses all known environment variables.
        /// </summary>
        /// <returns>A populated AssemblerConfiguration instance.</returns>
        public static AssemblerConfiguration LoadConfiguration()
        {
            var config = new AssemblerConfiguration
            {
                // Core Configuration
                RepoUrl = GetString("REPO_URL"),
                Branch = GetString("BRANCH"),
                AssemblerEnv = GetString("ASSEMBLER_ENV", "dev"),
                Language = GetSelectedLanguage(),
                Cloud = GetSelectedCloud(),

                // Paths
                Libs = GetString("ASSEMBLER_LIBS"),
                Sources = GetString("ASSEMBLER_SOURCES"),
                OutputPath = GetString("ASSEMBLER_OUTPUT", "./output"),

                // Licensing
                License = new LicenseConfiguration
                {
                    ServerUrl = GetString("LICENSE_SERVER"),
                    TimeoutSeconds = GetInt("LICENSE_TIMEOUT", 300)
                },

                // Key Vault
                Vault = new VaultConfiguration
                {
                    Type = GetString("VAULT_TYPE"),
                    Url = GetString("VAULT_URL")
                },

                // Logging
                Logging = new LoggingConfiguration
                {
                    LogLevel = GetEnum<LogLevel>("LOG_LEVEL", LogLevel.Information),
                    ExternalLogEndpoint = GetString("THREE_SC_LOG_ENDPOINT"),
                    ExternalLogToken = GetString("THREE_SC_LOG_TOKEN"),
                    ExternalLogTokenVaultKey = GetString("THREE_SC_LOG_TOKEN_VAULT_KEY")
                },

                // Tag Template
                TagTemplate = new TagTemplateConfiguration
                {
                    Template = GetString("TAG_TEMPLATE", "{repo}/{group}/{version}")
                },

                // Operation Modes
                ValidateOnly = GetBool("VALIDATE_ONLY"),
                NoOp = GetBool("NO_OP", true)
            };

            return config;
        }

        #region Private Getters and Parsers

        private static string GetSelectedLanguage()
        {
            if (GetBool("LANGUAGE_CSHARP")) return "csharp";
            if (GetBool("LANGUAGE_JAVA")) return "java";
            if (GetBool("LANGUAGE_PYTHON")) return "python";
            if (GetBool("LANGUAGE_JAVASCRIPT")) return "javascript";
            if (GetBool("LANGUAGE_TYPESCRIPT")) return "typescript";
            if (GetBool("LANGUAGE_GO")) return "go";
            return string.Empty;
        }

        private static string GetSelectedCloud()
        {
            if (GetBool("CLOUD_AZURE")) return "azure";
            if (GetBool("CLOUD_AWS")) return "aws";
            if (GetBool("CLOUD_GCP")) return "gcp";
            if (GetBool("CLOUD_ORACLE")) return "oracle";
            return string.Empty;
        }

        private static string GetString(string name, string defaultValue = "")
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        private static bool GetBool(string name, bool defaultValue = false)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) return defaultValue;
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(string name, int defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private static T GetEnum<T>(string name, T defaultValue) where T : struct, Enum
        {
            var value = Environment.GetEnvironmentVariable(name);
            return Enum.TryParse<T>(value, true, out var result) ? result : defaultValue;
        }

        #endregion
    }
}