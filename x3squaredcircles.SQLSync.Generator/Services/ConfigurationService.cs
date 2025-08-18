using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Schema;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IConfigurationService
    {
        SqlSchemaConfiguration GetConfiguration();
        void ValidateConfiguration(SqlSchemaConfiguration config);
        void LogConfiguration(SqlSchemaConfiguration config);
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
        }

        public SqlSchemaConfiguration GetConfiguration()
        {
            _logger.LogDebug("Parsing configuration from environment variables");

            var config = new SqlSchemaConfiguration
            {
                Language = ParseLanguageConfiguration(),
                Database = ParseDatabaseConfiguration(),
                TrackAttribute = GetRequiredEnvironmentVariable("TRACK_ATTRIBUTE"),
                RepoUrl = GetRequiredEnvironmentVariable("REPO_URL"),
                Branch = GetRequiredEnvironmentVariable("BRANCH"),
                License = ParseLicenseConfiguration(),
                KeyVault = ParseKeyVaultConfiguration(),
                TagTemplate = ParseTagTemplateConfiguration(),
                Operation = ParseOperationConfiguration(),
                SchemaAnalysis = ParseSchemaAnalysisConfiguration(),
                Deployment = ParseDeploymentConfiguration(),
                Backup = ParseBackupConfiguration(),
                Logging = ParseLoggingConfiguration(),
                Authentication = ParseAuthenticationConfiguration(),
                Environment = ParseEnvironmentConfiguration()
            };

            return config;
        }

        public void ValidateConfiguration(SqlSchemaConfiguration config)
        {
            _logger.LogDebug("Validating configuration");

            var errors = new List<string>();

            // Validate language selection
            if (!config.Language.HasSingleLanguageSelected())
            {
                if (config.Language.GetSelectedLanguage() == string.Empty)
                {
                    errors.Add("No language specified. Set exactly one: LANGUAGE_CSHARP, LANGUAGE_JAVA, LANGUAGE_PYTHON, LANGUAGE_JAVASCRIPT, LANGUAGE_TYPESCRIPT, or LANGUAGE_GO to 'true'.");
                }
                else
                {
                    errors.Add("Multiple languages specified. Set exactly one language environment variable to 'true'.");
                }
            }

            // Validate database provider selection
            if (!config.Database.HasSingleProviderSelected())
            {
                if (config.Database.GetSelectedProvider() == string.Empty)
                {
                    errors.Add("No database provider specified. Set exactly one: DATABASE_SQLSERVER, DATABASE_POSTGRESQL, DATABASE_MYSQL, DATABASE_ORACLE, or DATABASE_SQLITE to 'true'.");
                }
                else
                {
                    errors.Add("Multiple database providers specified. Set exactly one database provider environment variable to 'true'.");
                }
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(config.TrackAttribute))
            {
                errors.Add("TRACK_ATTRIBUTE is required.");
            }

            if (string.IsNullOrWhiteSpace(config.RepoUrl))
            {
                errors.Add("REPO_URL is required.");
            }

            if (string.IsNullOrWhiteSpace(config.Branch))
            {
                errors.Add("BRANCH is required.");
            }

            if (string.IsNullOrWhiteSpace(config.License.ServerUrl))
            {
                errors.Add("LICENSE_SERVER is required.");
            }

            // Validate database configuration
            ValidateDatabaseConfig(config.Database, errors);

            // Validate environment and vertical for beta/prod
            ValidateEnvironmentConfig(config.Environment, errors);

            // Validate tag template
            try
            {
                ValidateTagTemplate(config.TagTemplate.Template, "TAG_TEMPLATE");
            }
            catch (Exception ex)
            {
                errors.Add($"Tag template validation failed: {ex.Message}");
            }

            if (errors.Any())
            {
                var errorMessage = "Configuration validation failed:\n" + string.Join("\n", errors.Select(e => $"  - {e}"));
                _logger.LogError(errorMessage);
                throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration, errorMessage);
            }

            _logger.LogInformation("✓ Configuration validation passed");
        }

        public void LogConfiguration(SqlSchemaConfiguration config)
        {
            _logger.LogInformation("=== SQL Schema Generator Configuration ===");
            _logger.LogInformation("Language: {Language}", config.Language.GetSelectedLanguage().ToUpperInvariant());
            _logger.LogInformation("Database Provider: {DatabaseProvider}", config.Database.GetSelectedProvider().ToUpperInvariant());
            _logger.LogInformation("Track Attribute: {TrackAttribute}", config.TrackAttribute);
            _logger.LogInformation("Repository: {RepoUrl}", config.RepoUrl);
            _logger.LogInformation("Branch: {Branch}", config.Branch);
            _logger.LogInformation("License Server: {LicenseServer}", MaskSensitiveUrl(config.License.ServerUrl));
            _logger.LogInformation("Mode: {Mode}", config.Operation.Mode.ToUpperInvariant());
            _logger.LogInformation("Database: {DatabaseServer}/{DatabaseName}",
                MaskSensitiveUrl(config.Database.Server), config.Database.DatabaseName);
            _logger.LogInformation("Environment: {Environment}", config.Environment.Environment);
            _logger.LogInformation("Tag Template: {TagTemplate}", config.TagTemplate.Template);

            if (config.Operation.ValidateOnly)
            {
                _logger.LogInformation("VALIDATE ONLY mode enabled");
            }

            if (config.Operation.NoOp)
            {
                _logger.LogInformation("NO-OP mode enabled");
            }

            if (!string.IsNullOrEmpty(config.Environment.Vertical))
            {
                _logger.LogInformation("Vertical: {Vertical}", config.Environment.Vertical);
            }

            if (config.KeyVault != null)
            {
                _logger.LogInformation("Key Vault: {VaultType} - {VaultUrl}",
                    config.KeyVault.Type.ToUpperInvariant(), MaskSensitiveUrl(config.KeyVault.Url));
            }

            _logger.LogInformation("==========================================");
        }

        private LanguageConfiguration ParseLanguageConfiguration()
        {
            return new LanguageConfiguration
            {
                CSharp = GetBooleanEnvironmentVariable("LANGUAGE_CSHARP"),
                Java = GetBooleanEnvironmentVariable("LANGUAGE_JAVA"),
                Python = GetBooleanEnvironmentVariable("LANGUAGE_PYTHON"),
                JavaScript = GetBooleanEnvironmentVariable("LANGUAGE_JAVASCRIPT"),
                TypeScript = GetBooleanEnvironmentVariable("LANGUAGE_TYPESCRIPT"),
                Go = GetBooleanEnvironmentVariable("LANGUAGE_GO")
            };
        }

        private DatabaseConfiguration ParseDatabaseConfiguration()
        {
            return new DatabaseConfiguration
            {
                // Provider selection
                SqlServer = GetBooleanEnvironmentVariable("DATABASE_SQLSERVER"),
                PostgreSQL = GetBooleanEnvironmentVariable("DATABASE_POSTGRESQL"),
                MySQL = GetBooleanEnvironmentVariable("DATABASE_MYSQL"),
                Oracle = GetBooleanEnvironmentVariable("DATABASE_ORACLE"),
                SQLite = GetBooleanEnvironmentVariable("DATABASE_SQLITE"),

                // Connection settings
                Server = GetEnvironmentVariable("DATABASE_SERVER"),
                DatabaseName = GetEnvironmentVariable("DATABASE_NAME"),
                Schema = GetEnvironmentVariable("DATABASE_SCHEMA"),
                Port = GetIntegerEnvironmentVariable("DATABASE_PORT", 0),

                // Authentication
                Username = GetEnvironmentVariable("DATABASE_USERNAME"),
                Password = GetEnvironmentVariable("DATABASE_PASSWORD"),
                UsernameVaultKey = GetEnvironmentVariable("DATABASE_USERNAME_VAULT_KEY"),
                PasswordVaultKey = GetEnvironmentVariable("DATABASE_PASSWORD_VAULT_KEY"),
                UseIntegratedAuth = GetBooleanEnvironmentVariable("DATABASE_USE_INTEGRATED_AUTH"),
                ConnectionString = GetEnvironmentVariable("DATABASE_CONNECTION_STRING"),

                // Connection behavior
                ConnectionTimeoutSeconds = GetIntegerEnvironmentVariable("DATABASE_CONNECTION_TIMEOUT", 30),
                CommandTimeoutSeconds = GetIntegerEnvironmentVariable("DATABASE_COMMAND_TIMEOUT", 300),
                RetryAttempts = GetIntegerEnvironmentVariable("DATABASE_RETRY_ATTEMPTS", 3),
                RetryIntervalSeconds = GetIntegerEnvironmentVariable("DATABASE_RETRY_INTERVAL", 5),

                // Provider-specific settings
                SqlServerInstance = GetEnvironmentVariable("SQLSERVER_INSTANCE"),
                SqlServerEncrypt = GetBooleanEnvironmentVariable("SQLSERVER_ENCRYPT", true),
                SqlServerTrustCert = GetBooleanEnvironmentVariable("SQLSERVER_TRUST_CERT"),
                SqlServerBackupType = GetEnvironmentVariable("SQLSERVER_BACKUP_TYPE", "FULL"),

                PostgreSqlSslMode = GetEnvironmentVariable("POSTGRESQL_SSL_MODE", "require"),
                PostgreSqlSchemaSearchPath = GetEnvironmentVariable("POSTGRESQL_SCHEMA_SEARCH_PATH"),
                PostgreSqlApplicationName = GetEnvironmentVariable("POSTGRESQL_APPLICATION_NAME", "sql-schema-generator"),

                MySqlSslMode = GetEnvironmentVariable("MYSQL_SSL_MODE", "REQUIRED"),
                MySqlCharset = GetEnvironmentVariable("MYSQL_CHARSET", "utf8mb4"),
                MySqlEngine = GetEnvironmentVariable("MYSQL_ENGINE", "InnoDB"),

                OracleServiceName = GetEnvironmentVariable("ORACLE_SERVICE_NAME"),
                OracleTablespace = GetEnvironmentVariable("ORACLE_TABLESPACE"),
                OracleTempTablespace = GetEnvironmentVariable("ORACLE_TEMP_TABLESPACE"),

                SqliteFilePath = GetEnvironmentVariable("SQLITE_FILE_PATH"),
                SqliteJournalMode = GetEnvironmentVariable("SQLITE_JOURNAL_MODE", "WAL"),
                SqliteSynchronous = GetEnvironmentVariable("SQLITE_SYNCHRONOUS", "FULL")
            };
        }

        private LicenseConfiguration ParseLicenseConfiguration()
        {
            return new LicenseConfiguration
            {
                ServerUrl = GetRequiredEnvironmentVariable("LICENSE_SERVER"),
                ToolName = GetEnvironmentVariable("TOOL_NAME", "sql-schema-generator"),
                TimeoutSeconds = GetIntegerEnvironmentVariable("LICENSE_TIMEOUT", 300),
                RetryIntervalSeconds = GetIntegerEnvironmentVariable("LICENSE_RETRY_INTERVAL", 30)
            };
        }

        private KeyVaultConfiguration? ParseKeyVaultConfiguration()
        {
            var vaultType = GetEnvironmentVariable("VAULT_TYPE");
            if (string.IsNullOrWhiteSpace(vaultType))
            {
                return null;
            }

            var config = new KeyVaultConfiguration
            {
                Type = vaultType.ToLowerInvariant(),
                Url = GetEnvironmentVariable("VAULT_URL", string.Empty)
            };

            // Parse vault-specific parameters
            switch (config.Type)
            {
                case "azure":
                    config.Parameters["ClientId"] = GetEnvironmentVariable("AZURE_CLIENT_ID", string.Empty);
                    config.Parameters["ClientSecret"] = GetEnvironmentVariable("AZURE_CLIENT_SECRET", string.Empty);
                    config.Parameters["TenantId"] = GetEnvironmentVariable("AZURE_TENANT_ID", string.Empty);
                    break;

                case "aws":
                    config.Parameters["Region"] = GetEnvironmentVariable("AWS_REGION", "us-east-1");
                    config.Parameters["AccessKeyId"] = GetEnvironmentVariable("AWS_ACCESS_KEY_ID", string.Empty);
                    config.Parameters["SecretAccessKey"] = GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", string.Empty);
                    break;

                case "hashicorp":
                    config.Parameters["Token"] = GetEnvironmentVariable("VAULT_TOKEN", string.Empty);
                    break;
            }

            return config;
        }

        private TagTemplateConfiguration ParseTagTemplateConfiguration()
        {
            return new TagTemplateConfiguration
            {
                Template = GetEnvironmentVariable("TAG_TEMPLATE", "{branch}/{repo}/schema/{version}")
            };
        }

        private OperationConfiguration ParseOperationConfiguration()
        {
            return new OperationConfiguration
            {
                Mode = GetEnvironmentVariable("MODE", "noop").ToLowerInvariant(),
                ValidateOnly = GetBooleanEnvironmentVariable("VALIDATE_ONLY"),
                NoOp = GetBooleanEnvironmentVariable("NO_OP", true),
                SkipBackup = GetBooleanEnvironmentVariable("SKIP_BACKUP")
            };
        }

        private SchemaAnalysisConfiguration ParseSchemaAnalysisConfiguration()
        {
            return new SchemaAnalysisConfiguration
            {
                AssemblyPaths = GetEnvironmentVariable("ASSEMBLY_PATHS"),
                BuildOutputPath = GetEnvironmentVariable("BUILD_OUTPUT_PATH"),
                ScriptsPath = GetEnvironmentVariable("SCRIPTS_PATH"),
                IgnoreExportAttribute = GetBooleanEnvironmentVariable("IGNORE_EXPORT_ATTRIBUTE"),
                GenerateIndexes = GetBooleanEnvironmentVariable("GENERATE_INDEXES", true),
                GenerateFkIndexes = GetBooleanEnvironmentVariable("GENERATE_FK_INDEXES", true),
                EnableCrossSchemaRefs = GetBooleanEnvironmentVariable("ENABLE_CROSS_SCHEMA_REFS", true),
                ValidationLevel = GetEnvironmentVariable("SCHEMA_VALIDATION_LEVEL", "standard")
            };
        }

        private DeploymentConfiguration ParseDeploymentConfiguration()
        {
            return new DeploymentConfiguration
            {
                Enable29PhaseDeployment = GetBooleanEnvironmentVariable("ENABLE_29_PHASE_DEPLOYMENT", true),
                SkipWarningPhases = GetBooleanEnvironmentVariable("SKIP_WARNING_PHASES"),
                CustomPhaseOrder = GetEnvironmentVariable("CUSTOM_PHASE_ORDER")
            };
        }

        private BackupConfiguration ParseBackupConfiguration()
        {
            return new BackupConfiguration
            {
                BackupBeforeDeployment = GetBooleanEnvironmentVariable("BACKUP_BEFORE_DEPLOYMENT", true),
                RetentionDays = GetIntegerEnvironmentVariable("BACKUP_RETENTION_DAYS", 7),
                RestorePointLabel = GetEnvironmentVariable("RESTORE_POINT_LABEL")
            };
        }

        private LoggingConfiguration ParseLoggingConfiguration()
        {
            return new LoggingConfiguration
            {
                Verbose = GetBooleanEnvironmentVariable("VERBOSE"),
                LogLevel = GetEnvironmentVariable("LOG_LEVEL", "INFO").ToUpperInvariant(),
                SchemaDump = GetBooleanEnvironmentVariable("SCHEMA_DUMP")
            };
        }

        private AuthenticationConfiguration ParseAuthenticationConfiguration()
        {
            return new AuthenticationConfiguration
            {
                PatToken = GetEnvironmentVariable("PAT_TOKEN"),
                PatSecretName = GetEnvironmentVariable("PAT_SECRET_NAME"),
                PipelineToken = GetPipelineToken()
            };
        }

        private EnvironmentConfiguration ParseEnvironmentConfiguration()
        {
            return new EnvironmentConfiguration
            {
                Environment = GetEnvironmentVariable("ENVIRONMENT", "dev"),
                Vertical = GetEnvironmentVariable("VERTICAL")
            };
        }

        private string? GetPipelineToken()
        {
            // Try Azure DevOps first
            var adoToken = GetEnvironmentVariable("System.AccessToken");
            if (!string.IsNullOrWhiteSpace(adoToken))
            {
                return adoToken;
            }

            // Try GitHub Actions
            var githubToken = GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(githubToken))
            {
                return githubToken;
            }

            // Try Jenkins (common variable names)
            var jenkinsToken = GetEnvironmentVariable("GIT_TOKEN") ?? GetEnvironmentVariable("SCM_TOKEN");
            return jenkinsToken;
        }

        private void ValidateDatabaseConfig(DatabaseConfiguration config, List<string> errors)
        {
            var provider = config.GetSelectedProvider();

            // Basic connection validation
            if (string.IsNullOrWhiteSpace(config.Server) && string.IsNullOrWhiteSpace(config.ConnectionString))
            {
                if (provider != "sqlite") // SQLite uses file path, not server
                {
                    errors.Add("DATABASE_SERVER is required when DATABASE_CONNECTION_STRING is not provided");
                }
            }

            if (string.IsNullOrWhiteSpace(config.DatabaseName) && string.IsNullOrWhiteSpace(config.ConnectionString))
            {
                if (provider != "sqlite") // SQLite uses file path, not database name
                {
                    errors.Add("DATABASE_NAME is required when DATABASE_CONNECTION_STRING is not provided");
                }
            }

            // Provider-specific validation
            switch (provider)
            {
                case "sqlite":
                    if (string.IsNullOrWhiteSpace(config.SqliteFilePath) && string.IsNullOrWhiteSpace(config.ConnectionString))
                    {
                        errors.Add("SQLITE_FILE_PATH is required when DATABASE_SQLITE=true and DATABASE_CONNECTION_STRING is not provided");
                    }
                    break;

                case "oracle":
                    if (string.IsNullOrWhiteSpace(config.OracleServiceName) && string.IsNullOrWhiteSpace(config.ConnectionString))
                    {
                        errors.Add("ORACLE_SERVICE_NAME is required when DATABASE_ORACLE=true and DATABASE_CONNECTION_STRING is not provided");
                    }
                    break;
            }

            // Authentication validation
            if (!config.UseIntegratedAuth && string.IsNullOrWhiteSpace(config.ConnectionString))
            {
                if (string.IsNullOrWhiteSpace(config.Username) && string.IsNullOrWhiteSpace(config.UsernameVaultKey))
                {
                    errors.Add("DATABASE_USERNAME or DATABASE_USERNAME_VAULT_KEY is required when integrated authentication is not used");
                }

                if (string.IsNullOrWhiteSpace(config.Password) && string.IsNullOrWhiteSpace(config.PasswordVaultKey))
                {
                    errors.Add("DATABASE_PASSWORD or DATABASE_PASSWORD_VAULT_KEY is required when integrated authentication is not used");
                }
            }
        }

        private void ValidateEnvironmentConfig(EnvironmentConfiguration config, List<string> errors)
        {
            var environment = config.Environment.ToLowerInvariant();

            if (environment is "beta" or "prod")
            {
                if (string.IsNullOrWhiteSpace(config.Vertical))
                {
                    errors.Add($"VERTICAL is required for {environment.ToUpperInvariant()} deployments");
                }
            }
        }

        private void ValidateTagTemplate(string template, string templateName)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new ArgumentException($"{templateName} cannot be empty");
            }

            var supportedTokens = new[]
            {
                "branch", "repo", "version", "major", "minor", "patch",
                "date", "datetime", "commit-hash", "commit-hash-full",
                "build-number", "user", "database", "environment", "vertical"
            };

            // Find all tokens in the template
            var tokens = new List<string>();
            var start = 0;
            while (true)
            {
                var openBrace = template.IndexOf('{', start);
                if (openBrace == -1) break;

                var closeBrace = template.IndexOf('}', openBrace);
                if (closeBrace == -1)
                {
                    throw new ArgumentException($"Invalid {templateName}: Unclosed token at position {openBrace}");
                }

                var token = template.Substring(openBrace + 1, closeBrace - openBrace - 1);
                tokens.Add(token);
                start = closeBrace + 1;
            }

            // Validate all tokens are supported
            foreach (var token in tokens)
            {
                if (!supportedTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Invalid {templateName}: Unknown token '{{{token}}}'. Supported tokens: {string.Join(", ", supportedTokens.Select(t => $"{{{t}}}"))}");
                }
            }
        }

        private string GetRequiredEnvironmentVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration,
                    $"Required environment variable {name} is not set or is empty.");
            }
            return value;
        }

        private string GetEnvironmentVariable(string name, string defaultValue = "")
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        private bool GetBooleanEnvironmentVariable(string name, bool defaultValue = false)
        {
            var value = GetEnvironmentVariable(name);
            return bool.TryParse(value, out var result) ? result : defaultValue;
        }

        private int GetIntegerEnvironmentVariable(string name, int defaultValue)
        {
            var value = GetEnvironmentVariable(name);
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private string MaskSensitiveUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            try
            {
                var uri = new Uri(url);
                return $"{uri.Scheme}://{uri.Host}{uri.PathAndQuery}";
            }
            catch
            {
                return url;
            }
        }
    }
}