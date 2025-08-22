using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Configuration
{
    /// <summary>
    /// A static class responsible for loading all application configuration from environment variables.
    /// This loader adheres to the "Environment-Driven Supremacy" principle, using namespaced prefixes
    /// and a defined order of precedence (Tool-Specific > Universal > Default).
    /// </summary>
    public static class EnvironmentConfigurationLoader
    {
        private const string ToolPrefix = "SQLSYNC_";
        private const string UniversalPrefix = "3SC_";

        /// <summary>
        /// Loads and parses all known environment variables into a strongly-typed SqlSchemaConfiguration object.
        /// </summary>
        /// <returns>A populated SqlSchemaConfiguration instance.</returns>
        public static SqlSchemaConfiguration LoadConfiguration()
        {
            var config = new SqlSchemaConfiguration();

            // Operation Mode
            config.Operation.Mode = GetEnum<OperationMode>("MODE", OperationMode.Generate);
            config.Operation.NoOp = GetBool("NO_OP", universalName: "NO_OP");

            // Language Selection
            config.Language.CSharp = GetBool("LANGUAGE_CSHARP");
            config.Language.Java = GetBool("LANGUAGE_JAVA");
            config.Language.Python = GetBool("LANGUAGE_PYTHON");
            config.Language.TypeScript = GetBool("LANGUAGE_TYPESCRIPT");
            config.Language.Go = GetBool("LANGUAGE_GO");

            // Database Provider and Connection
            LoadDatabaseConfiguration(config);
            LoadAuthenticationConfiguration(config);

            // Core and Context
            config.TrackAttribute = GetString("TRACK_ATTRIBUTE");
            config.RepoUrl = GetString("REPO_URL");
            config.Branch = GetString("BRANCH");
            config.Environment.Environment = GetString("ENVIRONMENT", "dev");
            config.Environment.Vertical = GetString("VERTICAL");

            // Licensing (with Universal Fallbacks)
            config.License.ServerUrl = GetString("LICENSE_SERVER", universalName: "LICENSE_SERVER");
            config.License.ToolName = GetString("TOOL_NAME", "sql-schema-generator");
            config.License.TimeoutSeconds = GetInt("LICENSE_TIMEOUT", 300, universalName: "LICENSE_TIMEOUT");
            config.License.RetryIntervalSeconds = GetInt("LICENSE_RETRY_INTERVAL", 30, universalName: "LICENSE_RETRY_INTERVAL");

            // Vault, Analysis, and Tagging
            LoadVaultConfiguration(config);
            LoadSchemaAnalysisConfiguration(config);
            config.TagTemplate.Template = GetString("TAG_TEMPLATE", "{branch}/{repo}/schema/{version}");

            // Deployment and Backup
            config.Deployment.Enable29PhaseDeployment = GetBool("ENABLE_29_PHASE_DEPLOYMENT", true);
            config.Backup.SkipBackup = GetBool("SKIP_BACKUP");
            config.Backup.RetentionDays = GetInt("BACKUP_RETENTION_DAYS", 7);

            // Standardized Observability and Logging
            LoadObservabilityConfiguration(config);
            LoadLoggingConfiguration(config);

            return config;
        }

        #region Private Loader Methods

        private static void LoadDatabaseConfiguration(SqlSchemaConfiguration config)
        {
            if (GetBool("DATABASE_SQLSERVER")) config.Database.Provider = DbProvider.SqlServer;
            else if (GetBool("DATABASE_POSTGRESQL")) config.Database.Provider = DbProvider.PostgreSql;
            else if (GetBool("DATABASE_MYSQL")) config.Database.Provider = DbProvider.MySql;
            else if (GetBool("DATABASE_ORACLE")) config.Database.Provider = DbProvider.Oracle;
            else if (GetBool("DATABASE_SQLITE")) config.Database.Provider = DbProvider.SQLite;

            config.Database.Server = GetString("DB_SERVER");
            config.Database.DatabaseName = GetString("DB_NAME");
            config.Database.Schema = GetString("DB_SCHEMA", "dbo");
            config.Database.Port = GetInt("DB_PORT", 0);
            config.Database.ConnectionTimeoutSeconds = GetInt("DB_CONNECT_TIMEOUT_SECONDS", 30);
            config.Database.CommandTimeoutSeconds = GetInt("DB_COMMAND_TIMEOUT_SECONDS", 300);
        }

        private static void LoadAuthenticationConfiguration(SqlSchemaConfiguration config)
        {
            config.Authentication.AuthMode = GetEnum<AuthMode>("AUTH_MODE", AuthMode.Password);
            config.Authentication.Username = GetString("DB_USERNAME");
            config.Authentication.PasswordSecretName = GetString("DB_PASSWORD_SECRET");
            config.Authentication.PatToken = GetString("PAT_TOKEN");
            config.Authentication.PatSecretName = GetString("PAT_SECRET_NAME");
            config.Authentication.AzureTenantId = GetString("AZURE_TENANT_ID", universalName: "AZURE_TENANT_ID");
            config.Authentication.AwsRegion = GetString("AWS_REGION", universalName: "AWS_REGION");
        }

        private static void LoadVaultConfiguration(SqlSchemaConfiguration config)
        {
            config.Vault.Type = GetEnum<VaultType>("VAULT_TYPE", VaultType.None, universalName: "VAULT_TYPE");
            config.Vault.Url = GetString("VAULT_URL", universalName: "VAULT_URL");
            config.Vault.AzureClientId = GetString("AZURE_CLIENT_ID", universalName: "AZURE_CLIENT_ID");
            config.Vault.AzureClientSecret = GetString("AZURE_CLIENT_SECRET", universalName: "AZURE_CLIENT_SECRET");
            config.Vault.AzureTenantId = GetString("AZURE_TENANT_ID", universalName: "AZURE_TENANT_ID");
            config.Vault.AwsRegion = GetString("AWS_REGION", universalName: "AWS_REGION");
            config.Vault.AwsAccessKeyId = GetString("AWS_ACCESS_KEY_ID", universalName: "AWS_ACCESS_KEY_ID");
            config.Vault.AwsSecretAccessKey = GetString("AWS_SECRET_ACCESS_KEY", universalName: "AWS_SECRET_ACCESS_KEY");
            config.Vault.HashiCorpToken = GetString("VAULT_TOKEN");
        }

        private static void LoadSchemaAnalysisConfiguration(SqlSchemaConfiguration config)
        {
            config.SchemaAnalysis.AssemblyPath = GetString("ASSEMBLY_PATH");
            config.SchemaAnalysis.SourcePaths = GetString("SOURCE_PATHS");
            config.SchemaAnalysis.ScriptsPath = GetString("SCRIPTS_PATH");
        }

        private static void LoadLoggingConfiguration(SqlSchemaConfiguration config)
        {
            config.Logging.Verbose = GetBool("VERBOSE", universalName: "VERBOSE");
            config.Logging.LogLevel = GetEnum<LogLevel>("LOG_LEVEL", LogLevel.Information, universalName: "LOG_LEVEL");
        }

        private static void LoadObservabilityConfiguration(SqlSchemaConfiguration config)
        {
            config.Observability.FirehoseLogEndpointUrl = GetString(null, universalName: "LOG_ENDPOINT_URL");
            config.Observability.FirehoseLogEndpointToken = GetString(null, universalName: "LOG_ENDPOINT_TOKEN");
        }

        #endregion

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