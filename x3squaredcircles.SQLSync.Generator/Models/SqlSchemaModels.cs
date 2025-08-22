using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace x3squaredcircles.SQLSync.Generator.Models
{
    #region Core Enums and Exceptions

    public enum SqlSchemaExitCode
    {
        Success = 0,
        InvalidConfiguration = 1,
        UnhandledException = 2,
        AuthenticationFailure = 3,
        LicenseValidationFailure = 4,
        LicenseUnavailable = 5,
        KeyVaultAccessFailure = 6,
        RepositoryAccessFailure = 7,
        GitOperationFailure = 8,
        EntityDiscoveryFailure = 9,
        DatabaseConnectionFailure = 10,
        SchemaAnalysisFailure = 11,
        SchemaValidationFailure = 12,
        RiskAssessmentFailure = 13,
        DeploymentPlanFailure = 14,
        SqlGenerationFailure = 15,
        DeploymentExecutionFailure = 16,
        ControlPointAborted = 17
    }

    public enum RiskLevel { Safe, Warning, Risky }
    public enum OperationMode { Generate, Deploy }
    public enum AuthMode { Password, AzureMsi, AwsIam, GcpServiceAccount }
    public enum DbProvider { SqlServer, PostgreSql, MySql, Oracle, SQLite }
    public enum VaultType { None, Azure, Aws, HashiCorp }

    public class SqlSchemaException : Exception
    {
        public SqlSchemaExitCode ExitCode { get; }
        public SqlSchemaException(SqlSchemaExitCode exitCode, string message) : base(message) { ExitCode = exitCode; }
        public SqlSchemaException(SqlSchemaExitCode exitCode, string message, Exception innerException) : base(message, innerException) { ExitCode = exitCode; }
    }

    #endregion

    #region Configuration Models

    public class SqlSchemaConfiguration
    {
        public OperationConfiguration Operation { get; set; } = new();
        public LanguageConfiguration Language { get; set; } = new();
        public DatabaseConfiguration Database { get; set; } = new();
        public AuthenticationConfiguration Authentication { get; set; } = new();
        public string TrackAttribute { get; set; }
        public string RepoUrl { get; set; }
        public string Branch { get; set; }
        public LicenseConfiguration License { get; set; } = new();
        public VaultConfiguration Vault { get; set; } = new();
        public TagTemplateConfiguration TagTemplate { get; set; } = new();
        public SchemaAnalysisConfiguration SchemaAnalysis { get; set; } = new();
        public DeploymentConfiguration Deployment { get; set; } = new();
        public BackupConfiguration Backup { get; set; } = new();
        public LoggingConfiguration Logging { get; set; } = new();
        public ObservabilityConfiguration Observability { get; set; } = new();
        public EnvironmentContext Environment { get; set; } = new();
    }

    public class OperationConfiguration
    {
        public OperationMode Mode { get; set; }
        public bool NoOp { get; set; }
    }

    public class LanguageConfiguration
    {
        public bool CSharp { get; set; }
        public bool Java { get; set; }
        public bool Python { get; set; }
        public bool TypeScript { get; set; }
        public bool Go { get; set; }
    }

    public class DatabaseConfiguration
    {
        public DbProvider Provider { get; set; }
        public string Server { get; set; }
        public string DatabaseName { get; set; }
        public string Schema { get; set; }
        public int Port { get; set; }
        public int ConnectionTimeoutSeconds { get; set; } = 30;
        public int CommandTimeoutSeconds { get; set; } = 300;
        public Dictionary<string, string> ProviderSpecificSettings { get; set; } = new();
    }

    public class AuthenticationConfiguration
    {
        public AuthMode AuthMode { get; set; }
        public string Username { get; set; }
        public string PasswordSecretName { get; set; }
        public string PatToken { get; set; }
        public string PatSecretName { get; set; }
        public string AzureTenantId { get; set; }
        public string AwsRegion { get; set; }
    }

    public class LicenseConfiguration
    {
        public string ServerUrl { get; set; }
        public string ToolName { get; set; }
        public int TimeoutSeconds { get; set; }
        public int RetryIntervalSeconds { get; set; }
    }

    public class VaultConfiguration
    {
        public VaultType Type { get; set; }
        public string Url { get; set; }
        public string AzureClientId { get; set; }
        public string AzureClientSecret { get; set; }
        public string AzureTenantId { get; set; }
        public string AwsRegion { get; set; }
        public string AwsAccessKeyId { get; set; }
        public string AwsSecretAccessKey { get; set; }
        public string HashiCorpToken { get; set; }
    }

    public class TagTemplateConfiguration
    {
        public string Template { get; set; }
    }

    public class SchemaAnalysisConfiguration
    {
        public string AssemblyPath { get; set; }
        public string SourcePaths { get; set; }
        public string ScriptsPath { get; set; }
    }

    public class DeploymentConfiguration
    {
        public bool Enable29PhaseDeployment { get; set; } = true;
    }

    public class BackupConfiguration
    {
        public bool SkipBackup { get; set; }
        public int RetentionDays { get; set; } = 7;
    }

    public class LoggingConfiguration
    {
        public bool Verbose { get; set; }
        public LogLevel LogLevel { get; set; }
    }

    public class ObservabilityConfiguration
    {
        public string FirehoseLogEndpointUrl { get; set; }
        public string FirehoseLogEndpointToken { get; set; }
    }

    public class EnvironmentContext
    {
        public string Environment { get; set; } = "dev";
        public string Vertical { get; set; }
    }
    #endregion

    #region Entity Discovery Models
    public class EntityDiscoveryResult
    {
        public List<DiscoveredEntity> Entities { get; set; } = new();
    }

    public class DiscoveredEntity
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string SchemaName { get; set; }
        public string SourceFile { get; set; }
        public List<DiscoveredProperty> Properties { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class DiscoveredProperty
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string SqlType { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
    }
    #endregion

    #region Schema Analysis Models
    public class DatabaseSchema
    {
        public List<SchemaTable> Tables { get; set; } = new();
    }

    public class SchemaTable
    {
        public string Name { get; set; }
        public string Schema { get; set; }
        public List<SchemaColumn> Columns { get; set; } = new();
    }

    public class SchemaColumn
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool IsNullable { get; set; }
    }
    #endregion

    #region Validation and Deployment Models
    public class SchemaValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<SchemaChange> Changes { get; set; } = new();
    }

    public class SchemaChange
    {
        public string Type { get; set; }
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string Schema { get; set; }
        public string Description { get; set; }
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Safe;
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    public class RiskAssessment
    {
        public RiskLevel OverallRiskLevel { get; set; } = RiskLevel.Safe;
        public List<string> Summary { get; set; } = new();
    }

    public class DeploymentPlan
    {
        public List<DeploymentPhase> Phases { get; set; } = new();
        public RiskLevel OverallRiskLevel { get; set; } = RiskLevel.Safe;
    }

    public class DeploymentPhase
    {
        public int PhaseNumber { get; set; }
        public string Name { get; set; }
        public List<DeploymentOperation> Operations { get; set; } = new();
    }

    public class DeploymentOperation
    {
        public string Type { get; set; }
        public string ObjectName { get; set; }
        public string Schema { get; set; }
        public string SqlCommand { get; set; }
        public string RollbackCommand { get; set; }
        public RiskLevel RiskLevel { get; set; }
    }

    public class DeploymentResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }
    #endregion

    #region Output Models
    public class SqlScript
    {
        public string Content { get; set; }
    }

    public class TagTemplateResult
    {
        public string GeneratedTag { get; set; }
        public Dictionary<string, string> TokenValues { get; set; } = new();
    }
    #endregion

    #region Control Point Models
    public enum ControlPointStage { OnRunStart, AfterDiscovery, AfterValidation, AfterRiskAssessment, BeforeBackup, BeforeExecution, Completion }
    public enum ControlPointEvent { Before, After, OnSuccess, OnFailure }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ControlPointAction { Continue, Abort }

    public class ControlPointPayload<T>
    {
        public string ToolName { get; set; }
        public string ToolVersion { get; set; }
        public string Stage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public T Data { get; set; }
    }

    public class ControlPointResponse<T>
    {
        public ControlPointAction Action { get; set; } = ControlPointAction.Continue;
        public string Message { get; set; }
        public T Data { get; set; }
    }

    public class ControlPointAbortedException : SqlSchemaException
    {
        public ControlPointAbortedException(string message)
            : base(SqlSchemaExitCode.ControlPointAborted, message)
        {
        }
    }
    #endregion
}