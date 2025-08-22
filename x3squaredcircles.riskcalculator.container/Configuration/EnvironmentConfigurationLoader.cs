using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Configuration
{
    /// <summary>
    /// A static class responsible for loading all application configuration from environment variables.
    /// This loader adheres to the "Environment-Driven Supremacy" principle, using namespaced prefixes
    /// and a defined order of precedence (Tool-Specific > Universal > Default).
    /// </summary>
    public static class EnvironmentConfigurationLoader
    {
        private const string ToolPrefix = "RISKCALC_";
        private const string UniversalPrefix = "3SC_";

        public static RiskCalculatorConfiguration LoadConfiguration()
        {
            var config = new RiskCalculatorConfiguration();

            // Licensing (Universal Only)
            config.License.ServerUrl = GetString(null, universalName: "LICENSE_SERVER");

            // Vault (Universal Only)
            config.Vault.Type = GetEnum<VaultType>(null, VaultType.None, universalName: "VAULT_TYPE");
            config.Vault.Url = GetString(null, universalName: "VAULT_URL");

            // Logging and Observability
            config.Logging.Verbose = GetBool("VERBOSE", universalName: "VERBOSE");
            config.Logging.LogLevel = GetEnum<LogLevel>("LOG_LEVEL", LogLevel.Information, universalName: "LOG_LEVEL");
            config.Observability.FirehoseLogEndpointUrl = GetString(null, universalName: "LOG_ENDPOINT_URL");
            config.Observability.FirehoseLogEndpointToken = GetString(null, universalName: "LOG_ENDPOINT_TOKEN");

            // Git Configuration
            config.Git.RepoUrl = GetString("REPO_URL");
            config.Git.Branch = GetString("BRANCH");
            config.Git.PatSecretName = GetString("PAT_SECRET_NAME");

            // Analysis Configuration
            config.Analysis.AlertThreshold = GetInt("ALERT_THRESHOLD", 2);
            config.Analysis.FailThreshold = GetInt("FAIL_THRESHOLD", 5);
            config.Analysis.AlertOnNewEntries = GetBool("ALERT_ON_NEW_ENTRIES", true);
            config.Analysis.MinimumPercentile = GetInt("MINIMUM_PERCENTILE", 70);

            var excluded = GetString("EXCLUDED_AREAS");
            if (!string.IsNullOrWhiteSpace(excluded))
            {
                config.Analysis.ExcludedAreas = excluded.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            return config;
        }

        #region Private Getters

        private static string GetVariable(string name, string universalName = null)
        {
            if (name != null)
            {
                var toolSpecificValue = Environment.GetEnvironmentVariable(ToolPrefix + name);
                if (!string.IsNullOrEmpty(toolSpecificValue)) return toolSpecificValue;
            }

            if (universalName != null)
            {
                var universalValue = Environment.GetEnvironmentVariable(UniversalPrefix + universalName);
                if (!string.IsNullOrEmpty(universalValue)) return universalValue;
            }

            return null;
        }

        private static string GetString(string name, string defaultValue = null, string universalName = null)
        {
            return GetVariable(name, universalName) ?? defaultValue;
        }

        private static bool GetBool(string name, bool defaultValue = false, string universalName = null)
        {
            var value = GetVariable(name, universalName);
            if (string.IsNullOrEmpty(value)) return defaultValue;
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(string name, int defaultValue, string universalName = null)
        {
            var value = GetVariable(name, universalName);
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private static T GetEnum<T>(string name, T defaultValue, string universalName = null) where T : struct, Enum
        {
            var value = GetVariable(name, universalName);
            if (string.IsNullOrEmpty(value)) return defaultValue;
            return Enum.TryParse<T>(value, true, out var result) ? result : defaultValue;
        }

        #endregion
    }
}