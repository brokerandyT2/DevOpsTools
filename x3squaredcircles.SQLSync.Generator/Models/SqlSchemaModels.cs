using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient; // Changed from System.Data.SqlClient
using System.Linq;

namespace x3squaredcircles.SQLSync.Generator.Models
{
    // Exit codes for the application
    public enum SqlSchemaExitCode
    {
        Success = 0,
        InvalidConfiguration = 1,
        EntityDiscoveryFailure = 2,
        DatabaseConnectionFailure = 3,
        SchemaValidationFailure = 4,
        DeploymentExecutionFailure = 5,
        LicenseUnavailable = 6,
        GitOperationFailure = 7,
        AuthenticationFailure = 8,
        KeyVaultAccessFailure = 9,
        WarningApprovalRequired = 10,
        RiskyDualApprovalRequired = 11
    }

    // Risk levels for operations
    public enum RiskLevel
    {
        Safe = 0,
        Warning = 1,
        Risky = 2
    }

    // Main exception class
    public class SqlSchemaException : Exception
    {
        public SqlSchemaExitCode ExitCode { get; }

        public SqlSchemaException(SqlSchemaExitCode exitCode, string message) : base(message)
        {
            ExitCode = exitCode;
        }

        public SqlSchemaException(SqlSchemaExitCode exitCode, string message, Exception innerException) : base(message, innerException)
        {
            ExitCode = exitCode;
        }
    }

    // Configuration models
    public class SqlSchemaConfiguration
    {
        public LanguageConfiguration Language { get; set; } = new LanguageConfiguration();
        public DatabaseConfiguration Database { get; set; } = new DatabaseConfiguration();
        public string TrackAttribute { get; set; } = string.Empty;
        public string RepoUrl { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public LicenseConfiguration License { get; set; } = new LicenseConfiguration();
        public KeyVaultConfiguration? KeyVault { get; set; }
        public TagTemplateConfiguration TagTemplate { get; set; } = new TagTemplateConfiguration();
        public OperationConfiguration Operation { get; set; } = new OperationConfiguration();
        public SchemaAnalysisConfiguration SchemaAnalysis { get; set; } = new SchemaAnalysisConfiguration();
        public DeploymentConfiguration Deployment { get; set; } = new DeploymentConfiguration();
        public BackupConfiguration Backup { get; set; } = new BackupConfiguration();
        public LoggingConfiguration Logging { get; set; } = new LoggingConfiguration();
        public AuthenticationConfiguration Authentication { get; set; } = new AuthenticationConfiguration();
        public EnvironmentConfiguration Environment { get; set; } = new EnvironmentConfiguration();
    }

    public class LanguageConfiguration
    {
        public bool CSharp { get; set; }
        public bool Java { get; set; }
        public bool Python { get; set; }
        public bool JavaScript { get; set; }
        public bool TypeScript { get; set; }
        public bool Go { get; set; }

        public bool HasSingleLanguageSelected()
        {
            var selectedCount = new[] { CSharp, Java, Python, JavaScript, TypeScript, Go }.Count(x => x);
            return selectedCount == 1;
        }

        public string GetSelectedLanguage()
        {
            if (CSharp) return "csharp";
            if (Java) return "java";
            if (Python) return "python";
            if (JavaScript) return "javascript";
            if (TypeScript) return "typescript";
            if (Go) return "go";
            return string.Empty;
        }
    }

    public class DatabaseConfiguration
    {
        // Provider selection
        public bool SqlServer { get; set; }
        public bool PostgreSQL { get; set; }
        public bool MySQL { get; set; }
        public bool Oracle { get; set; }
        public bool SQLite { get; set; }

        // Connection settings
        public string Server { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public int Port { get; set; }

        // Authentication
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UsernameVaultKey { get; set; } = string.Empty;
        public string PasswordVaultKey { get; set; } = string.Empty;
        public bool UseIntegratedAuth { get; set; }
        public string ConnectionString { get; set; } = string.Empty;

        // Connection behavior
        public int ConnectionTimeoutSeconds { get; set; } = 30;
        public int CommandTimeoutSeconds { get; set; } = 300;
        public int RetryAttempts { get; set; } = 3;
        public int RetryIntervalSeconds { get; set; } = 5;

        // Provider-specific settings
        public string SqlServerInstance { get; set; } = string.Empty;
        public bool SqlServerEncrypt { get; set; } = true;
        public bool SqlServerTrustCert { get; set; }
        public string SqlServerBackupType { get; set; } = "FULL";

        public string PostgreSqlSslMode { get; set; } = "require";
        public string PostgreSqlSchemaSearchPath { get; set; } = string.Empty;
        public string PostgreSqlApplicationName { get; set; } = "sql-schema-generator";

        public string MySqlSslMode { get; set; } = "REQUIRED";
        public string MySqlCharset { get; set; } = "utf8mb4";
        public string MySqlEngine { get; set; } = "InnoDB";

        public string OracleServiceName { get; set; } = string.Empty;
        public string OracleTablespace { get; set; } = string.Empty;
        public string OracleTempTablespace { get; set; } = string.Empty;

        public string SqliteFilePath { get; set; } = string.Empty;
        public string SqliteJournalMode { get; set; } = "WAL";
        public string SqliteSynchronous { get; set; } = "FULL";

        public bool HasSingleProviderSelected()
        {
            var selectedCount = new[] { SqlServer, PostgreSQL, MySQL, Oracle, SQLite }.Count(x => x);
            return selectedCount == 1;
        }

        public string GetSelectedProvider()
        {
            if (SqlServer) return "sqlserver";
            if (PostgreSQL) return "postgresql";
            if (MySQL) return "mysql";
            if (Oracle) return "oracle";
            if (SQLite) return "sqlite";
            return string.Empty;
        }

        public string BuildConnectionString()
        {
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                return ConnectionString;
            }

            var provider = GetSelectedProvider();
            return provider switch
            {
                "sqlserver" => BuildSqlServerConnectionString(),
                "postgresql" => BuildPostgreSqlConnectionString(),
                "mysql" => BuildMySqlConnectionString(),
                "oracle" => BuildOracleConnectionString(),
                "sqlite" => BuildSqliteConnectionString(),
                _ => throw new NotSupportedException($"Connection string building not supported for provider: {provider}")
            };
        }

        private string BuildSqlServerConnectionString()
        {
            // Use Microsoft.Data.SqlClient.SqlConnectionStringBuilder instead of System.Data.SqlClient
            var builder = new SqlConnectionStringBuilder();

            if (!string.IsNullOrEmpty(SqlServerInstance))
            {
                builder.DataSource = $"{Server}\\{SqlServerInstance}";
            }
            else
            {
                builder.DataSource = Server;
                if (Port > 0)
                {
                    builder.DataSource = $"{Server},{Port}";
                }
            }

            builder.InitialCatalog = DatabaseName;

            if (UseIntegratedAuth)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = Username;
                builder.Password = Password;
            }

            builder.ConnectTimeout = ConnectionTimeoutSeconds;
            builder.CommandTimeout = CommandTimeoutSeconds;
            builder.Encrypt = SqlServerEncrypt;
            builder.TrustServerCertificate = SqlServerTrustCert;

            return builder.ConnectionString;
        }

        private string BuildPostgreSqlConnectionString()
        {
            var parts = new List<string>
            {
                $"Host={Server}",
                $"Database={DatabaseName}",
                $"Timeout={ConnectionTimeoutSeconds}",
                $"Command Timeout={CommandTimeoutSeconds}",
                $"SSL Mode={PostgreSqlSslMode}",
                $"Application Name={PostgreSqlApplicationName}"
            };

            if (Port > 0)
            {
                parts.Add($"Port={Port}");
            }

            if (!UseIntegratedAuth)
            {
                if (!string.IsNullOrEmpty(Username)) parts.Add($"Username={Username}");
                if (!string.IsNullOrEmpty(Password)) parts.Add($"Password={Password}");
            }

            if (!string.IsNullOrEmpty(PostgreSqlSchemaSearchPath))
            {
                parts.Add($"Search Path={PostgreSqlSchemaSearchPath}");
            }

            return string.Join(";", parts);
        }

        private string BuildMySqlConnectionString()
        {
            var parts = new List<string>
            {
                $"Server={Server}",
                $"Database={DatabaseName}",
                $"Connection Timeout={ConnectionTimeoutSeconds}",
                $"Default Command Timeout={CommandTimeoutSeconds}",
                $"SSL Mode={MySqlSslMode}",
                $"CharSet={MySqlCharset}"
            };

            if (Port > 0)
            {
                parts.Add($"Port={Port}");
            }

            if (!UseIntegratedAuth)
            {
                if (!string.IsNullOrEmpty(Username)) parts.Add($"Uid={Username}");
                if (!string.IsNullOrEmpty(Password)) parts.Add($"Pwd={Password}");
            }

            return string.Join(";", parts);
        }

        private string BuildOracleConnectionString()
        {
            var port = Port > 0 ? Port : 1521;
            var serviceName = OracleServiceName ?? "ORCL";
            var dataSource = $"{Server}:{port}/{serviceName}";

            var parts = new List<string>
            {
                $"Data Source={dataSource}",
                $"Connection Timeout={ConnectionTimeoutSeconds}"
            };

            if (!UseIntegratedAuth)
            {
                if (!string.IsNullOrEmpty(Username)) parts.Add($"User Id={Username}");
                if (!string.IsNullOrEmpty(Password)) parts.Add($"Password={Password}");
            }

            return string.Join(";", parts);
        }

        private string BuildSqliteConnectionString()
        {
            var parts = new List<string>
            {
                $"Data Source={SqliteFilePath ?? ":memory:"}",
                "Mode=ReadWriteCreate",
                "Cache=Shared",
                $"Journal Mode={SqliteJournalMode}",
                $"Synchronous={SqliteSynchronous}"
            };

            return string.Join(";", parts);
        }
    }

    public class LicenseConfiguration
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string ToolName { get; set; } = "sql-schema-generator";
        public int TimeoutSeconds { get; set; } = 300;
        public int RetryIntervalSeconds { get; set; } = 30;
    }

    public class KeyVaultConfiguration
    {
        public string Type { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    public class TagTemplateConfiguration
    {
        public string Template { get; set; } = "{branch}/{repo}/schema/{version}";
    }

    public class OperationConfiguration
    {
        public string Mode { get; set; } = "noop";
        public bool ValidateOnly { get; set; }
        public bool NoOp { get; set; } = true;
        public bool SkipBackup { get; set; }
    }

    public class SchemaAnalysisConfiguration
    {
        public string AssemblyPaths { get; set; } = string.Empty;
        public string BuildOutputPath { get; set; } = string.Empty;
        public string ScriptsPath { get; set; } = string.Empty;
        public bool IgnoreExportAttribute { get; set; }
        public bool GenerateIndexes { get; set; } = true;
        public bool GenerateFkIndexes { get; set; } = true;
        public bool EnableCrossSchemaRefs { get; set; } = true;
        public string ValidationLevel { get; set; } = "standard";
    }

    public class DeploymentConfiguration
    {
        public bool Enable29PhaseDeployment { get; set; } = true;
        public bool SkipWarningPhases { get; set; }
        public string CustomPhaseOrder { get; set; } = string.Empty;
    }

    public class BackupConfiguration
    {
        public bool BackupBeforeDeployment { get; set; } = true;
        public int RetentionDays { get; set; } = 7;
        public string RestorePointLabel { get; set; } = string.Empty;
    }

    public class LoggingConfiguration
    {
        public bool Verbose { get; set; }
        public string LogLevel { get; set; } = "INFO";
        public bool SchemaDump { get; set; }
    }

    public class AuthenticationConfiguration
    {
        public string PatToken { get; set; } = string.Empty;
        public string PatSecretName { get; set; } = string.Empty;
        public string PipelineToken { get; set; } = string.Empty;
    }

    public class EnvironmentConfiguration
    {
        public string Environment { get; set; } = "dev";
        public string Vertical { get; set; } = string.Empty;
    }

    // Entity discovery models
    public class EntityDiscoveryResult
    {
        public List<DiscoveredEntity> Entities { get; set; } = new List<DiscoveredEntity>();
        public string Language { get; set; } = string.Empty;
        public string TrackAttribute { get; set; } = string.Empty;
        public DateTime DiscoveryTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class DiscoveredEntity
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public int SourceLine { get; set; }
        public List<DiscoveredProperty> Properties { get; set; } = new List<DiscoveredProperty>();
        public List<DiscoveredIndex> Indexes { get; set; } = new List<DiscoveredIndex>();
        public List<DiscoveredRelationship> Relationships { get; set; } = new List<DiscoveredRelationship>();
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
    }

    public class DiscoveredProperty
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string SqlType { get; set; } = string.Empty;
        public bool IsNullable { get; set; } = true;
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }
        public bool IsUnique { get; set; }
        public bool IsIndexed { get; set; }
        public int? MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public string? DefaultValue { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
    }

    public class DiscoveredIndex
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new List<string>();
        public bool IsUnique { get; set; }
        public bool IsClustered { get; set; }
        public string? FilterExpression { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
    }

    public class DiscoveredRelationship
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ReferencedEntity { get; set; } = string.Empty;
        public string ReferencedTable { get; set; } = string.Empty;
        public List<string> ForeignKeyColumns { get; set; } = new List<string>();
        public List<string> ReferencedColumns { get; set; } = new List<string>();
        public string OnDeleteAction { get; set; } = "NO_ACTION";
        public string OnUpdateAction { get; set; } = "NO_ACTION";
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
    }

    // Database schema models
    public class DatabaseSchema
    {
        public string DatabaseName { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public DateTime AnalysisTime { get; set; }
        public List<SchemaTable> Tables { get; set; } = new List<SchemaTable>();
        public List<SchemaView> Views { get; set; } = new List<SchemaView>();
        public List<SchemaIndex> Indexes { get; set; } = new List<SchemaIndex>();
        public List<SchemaConstraint> Constraints { get; set; } = new List<SchemaConstraint>();
        public List<SchemaProcedure> Procedures { get; set; } = new List<SchemaProcedure>();
        public List<SchemaFunction> Functions { get; set; } = new List<SchemaFunction>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaTable
    {
        public string Name { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public List<SchemaColumn> Columns { get; set; } = new List<SchemaColumn>();
        public List<SchemaIndex> Indexes { get; set; } = new List<SchemaIndex>();
        public List<SchemaConstraint> Constraints { get; set; } = new List<SchemaConstraint>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaColumn
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsNullable { get; set; } = true;
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public int? MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public string? DefaultValue { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaIndex
    {
        public string Name { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new List<string>();
        public bool IsUnique { get; set; }
        public bool IsClustered { get; set; }
        public string? FilterExpression { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaConstraint
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new List<string>();
        public List<string> ReferencedColumns { get; set; } = new List<string>();
        public string? ReferencedTable { get; set; }
        public string? ReferencedSchema { get; set; }
        public string OnDeleteAction { get; set; } = "NO_ACTION";
        public string OnUpdateAction { get; set; } = "NO_ACTION";
        public string? CheckExpression { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaView
    {
        public string Name { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public string Definition { get; set; } = string.Empty;
        public List<SchemaColumn> Columns { get; set; } = new List<SchemaColumn>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaProcedure
    {
        public string Name { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public string Definition { get; set; } = string.Empty;
        public List<SchemaParameter> Parameters { get; set; } = new List<SchemaParameter>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaFunction
    {
        public string Name { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public string Definition { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public List<SchemaParameter> Parameters { get; set; } = new List<SchemaParameter>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaParameter
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public string? DefaultValue { get; set; }
    }

    public enum ParameterDirection
    {
        Input,
        Output,
        InputOutput,
        ReturnValue
    }

    // Schema validation models
    public class SchemaValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<SchemaChange> Changes { get; set; } = new List<SchemaChange>();
        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();
        public List<ValidationWarning> Warnings { get; set; } = new List<ValidationWarning>();
        public DateTime ValidationTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SchemaChange
    {
        public string Type { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Safe;
        public List<string> Dependencies { get; set; } = new List<string>();
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }

    public class ValidationError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
    }

    public class ValidationWarning
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Warning;
    }

    // Risk assessment models
    public class RiskAssessment
    {
        public RiskLevel OverallRiskLevel { get; set; } = RiskLevel.Safe;
        public bool RequiresApproval { get; set; }
        public bool RequiresDualApproval { get; set; }
        public int SafeOperations { get; set; }
        public int WarningOperations { get; set; }
        public int RiskyOperations { get; set; }
        public DateTime AssessmentTime { get; set; }
        public List<RiskFactor> RiskFactors { get; set; } = new List<RiskFactor>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class RiskFactor
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Safe;
        public string Category { get; set; } = string.Empty;
        public List<string> AffectedObjects { get; set; } = new List<string>();
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }

    // Deployment models
    public class DeploymentPlan
    {
        public List<DeploymentPhase> Phases { get; set; } = new List<DeploymentPhase>();
        public RiskLevel OverallRiskLevel { get; set; } = RiskLevel.Safe;
        public bool Use29PhaseDeployment { get; set; } = true;
        public DateTime CreatedTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class DeploymentPhase
    {
        public int PhaseNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<DeploymentOperation> Operations { get; set; } = new List<DeploymentOperation>();
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Safe;
        public bool RequiresApproval { get; set; }
        public bool CanRollback { get; set; } = true;
        public List<string> Dependencies { get; set; } = new List<string>();
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }

    public class DeploymentOperation
    {
        public string Type { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public string SqlCommand { get; set; } = string.Empty;
        public string? RollbackCommand { get; set; }
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Safe;
        public List<string> Dependencies { get; set; } = new List<string>();
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }

    public class DeploymentResult
    {
        public bool Success { get; set; }
        public List<PhaseResult> PhaseResults { get; set; } = new List<PhaseResult>();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class PhaseResult
    {
        public int PhaseNumber { get; set; }
        public string PhaseName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int OperationsExecuted { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    // SQL generation models
    public class SqlScript
    {
        public string Content { get; set; } = string.Empty;
        public List<SqlStatement> Statements { get; set; } = new List<SqlStatement>();
        public DateTime GeneratedTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SqlStatement
    {
        public string Sql { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Safe;
        public int PhaseNumber { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }

    // License models
    public class LicenseSession
    {
        public string SessionId { get; set; } = string.Empty;
        public bool BurstMode { get; set; }
        public int BurstCountRemaining { get; set; }
        public DateTime ExpiresAt { get; set; }
        public System.Threading.Timer? HeartbeatTimer { get; set; }
    }

    public class LicenseAcquireRequest
    {
        public string ToolName { get; set; } = string.Empty;
        public string ToolVersion { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string BuildId { get; set; } = string.Empty;
    }

    public class LicenseAcquireResponse
    {
        public bool LicenseGranted { get; set; }
        public string? SessionId { get; set; }
        public bool BurstMode { get; set; }
        public int BurstCountRemaining { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Reason { get; set; }
        public int RetryAfterSeconds { get; set; }
        public bool BurstEventsExhausted { get; set; }
    }

    public class LicenseReleaseRequest
    {
        public string SessionId { get; set; } = string.Empty;
    }

    public class LicenseHeartbeatRequest
    {
        public string SessionId { get; set; } = string.Empty;
    }
}