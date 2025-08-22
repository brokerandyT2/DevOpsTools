using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using x3squaredcircles.PipelineGate.Container.Models;

namespace x3squaredcircles.PipelineGate.Container.Configuration
{
    public static class EnvironmentConfigurationLoader
    {
        private const string ToolPrefix = "GATE_";
        private const string UniversalPrefix = "3SC_";

        public static GateConfiguration LoadConfiguration()
        {
            var config = new GateConfiguration();

            // Universal Config
            config.Vault.Type = GetEnum<VaultType>(null, VaultType.None, universalName: "VAULT_TYPE");
            config.Vault.Url = GetString(null, universalName: "VAULT_URL");
            config.Logging.LogLevel = GetEnum<LogLevel>("LOG_LEVEL", LogLevel.Information, universalName: "LOG_LEVEL");
            config.Observability.FirehoseLogEndpointUrl = GetString(null, universalName: "LOG_ENDPOINT_URL");
            config.Observability.FirehoseLogEndpointToken = GetString(null, universalName: "LOG_ENDPOINT_TOKEN");

            // Core Operational Config
            config.Mode = GetEnum<GateMode>("MODE", GateMode.Basic);
            config.CiRunId = GetString("CI_RUN_ID");

            // Mode-Specific Config
            LoadBasicConfig(config);
            LoadAdvancedConfig(config);
            LoadCustomConfig(config);

            return config;
        }

        private static void LoadBasicConfig(GateConfiguration config)
        {
            config.Basic.Url = GetString("BASIC_URL");
            config.Basic.SecretName = GetString("BASIC_SECRET_NAME");
            config.Basic.SuccessEval = GetString("BASIC_SUCCESS_EVAL");
            config.Basic.DefaultAction = GetEnum<GateAction>("BASIC_DEFAULT_ACTION", GateAction.Break);
        }

        private static void LoadAdvancedConfig(GateConfiguration config)
        {
            config.Advanced.Action = GetEnum<AdvancedModeAction>("ADVANCED_ACTION", AdvancedModeAction.Notify);
            // Notify
            config.Advanced.NotifyUrl = GetString("ADVANCED_NOTIFY_URL");
            config.Advanced.NotifyPayload = GetString("ADVANCED_NOTIFY_PAYLOAD");
            // WaitFor
            config.Advanced.WaitUrl = GetString("ADVANCED_WAIT_URL");
            config.Advanced.WaitSecretName = GetString("ADVANCED_WAIT_SECRET_NAME");
            config.Advanced.WaitSuccessEval = GetString("ADVANCED_WAIT_SUCCESS_EVAL");
            config.Advanced.WaitFailureEval = GetString("ADVANCED_WAIT_FAILURE_EVAL");
            config.Advanced.WaitDefaultAction = GetEnum<GateAction>("ADVANCED_WAIT_DEFAULT_ACTION", GateAction.Pause);
            config.Advanced.WaitTimeoutMinutes = GetInt("ADVANCED_WAIT_TIMEOUT_MINUTES", 30);
            config.Advanced.WaitPollIntervalSeconds = GetInt("ADVANCED_WAIT_POLL_INTERVAL_SECONDS", 15);
        }

        private static void LoadCustomConfig(GateConfiguration config)
        {
            config.Custom.SwaggerPath = GetString("CUSTOM_SWAGGER_PATH");
            config.Custom.SecretName = GetString("CUSTOM_SECRET_NAME");
            config.Custom.OperationId = GetString("CUSTOM_OPERATION_ID");
            config.Custom.SuccessEval = GetString("CUSTOM_SUCCESS_EVAL");
            config.Custom.FailureEval = GetString("CUSTOM_FAILURE_EVAL");
            config.Custom.DefaultAction = GetEnum<GateAction>("CUSTOM_DEFAULT_ACTION", GateAction.Break);

            // Load all custom parameters using the structured naming convention
            var paramPrefix = $"{ToolPrefix}CUSTOM_PARAM_";
            foreach (System.Collections.DictionaryEntry envVar in Environment.GetEnvironmentVariables())
            {
                var key = envVar.Key.ToString();
                if (key.StartsWith(paramPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var paramName = key.Substring(paramPrefix.Length);
                    config.Custom.Parameters[paramName] = envVar.Value.ToString();
                }
            }
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

        private static string GetString(string name, string defaultValue = null, string universalName = null) => GetVariable(name, universalName) ?? defaultValue;

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