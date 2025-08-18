using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IDeploymentPlanService
    {
        Task<DeploymentPlan> GenerateDeploymentPlanAsync(SchemaValidationResult validationResult, RiskAssessment riskAssessment, SqlSchemaConfiguration config);
    }

    public class DeploymentPlanService : IDeploymentPlanService
    {
        private readonly ILogger<DeploymentPlanService> _logger;

        public DeploymentPlanService(ILogger<DeploymentPlanService> logger)
        {
            _logger = logger;
        }

        public async Task<DeploymentPlan> GenerateDeploymentPlanAsync(SchemaValidationResult validationResult, RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Generating deployment plan for {ChangeCount} schema changes", validationResult.Changes.Count);

                var deploymentPlan = new DeploymentPlan
                {
                    Phases = new List<DeploymentPhase>(),
                    OverallRiskLevel = riskAssessment.OverallRiskLevel,
                    Use29PhaseDeployment = config.Deployment.Enable29PhaseDeployment,
                    CreatedTime = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object>
                    {
                        ["total_changes"] = validationResult.Changes.Count,
                        ["risk_level"] = riskAssessment.OverallRiskLevel.ToString(),
                        ["requires_approval"] = riskAssessment.RequiresApproval,
                        ["requires_dual_approval"] = riskAssessment.RequiresDualApproval,
                        ["database_provider"] = config.Database.GetSelectedProvider(),
                        ["environment"] = config.Environment.Environment
                    }
                };

                if (config.Deployment.Enable29PhaseDeployment)
                {
                    await Generate29PhaseDeploymentAsync(validationResult, deploymentPlan, config);
                }
                else
                {
                    await GenerateSimpleDeploymentAsync(validationResult, deploymentPlan, config);
                }

                // Apply custom phase ordering if specified
                if (!string.IsNullOrEmpty(config.Deployment.CustomPhaseOrder))
                {
                    ApplyCustomPhaseOrdering(deploymentPlan, config.Deployment.CustomPhaseOrder);
                }

                // Skip warning phases if configured
                if (config.Deployment.SkipWarningPhases)
                {
                    SkipWarningPhases(deploymentPlan);
                }

                // Validate deployment plan
                ValidateDeploymentPlan(deploymentPlan, validationResult);

                _logger.LogInformation("✓ Deployment plan generated: {PhaseCount} phases, {OperationCount} operations",
                    deploymentPlan.Phases.Count, deploymentPlan.Phases.Sum(p => p.Operations.Count));

                return deploymentPlan;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate deployment plan");
                throw new SqlSchemaException(SqlSchemaExitCode.SchemaValidationFailure,
                    $"Failed to generate deployment plan: {ex.Message}", ex);
            }
        }

        private async Task Generate29PhaseDeploymentAsync(SchemaValidationResult validationResult, DeploymentPlan deploymentPlan, SqlSchemaConfiguration config)
        {
            _logger.LogDebug("Generating comprehensive 29-phase deployment plan");

            var changes = validationResult.Changes.ToList();
            var provider = config.Database.GetSelectedProvider();

            // Phase 1: Pre-deployment validation
            deploymentPlan.Phases.Add(CreatePhase(1, "Pre-deployment Validation",
                "Validate deployment prerequisites and environment", new List<DeploymentOperation>
                {
                    new DeploymentOperation
                    {
                        Type = "VALIDATION",
                        ObjectName = "deployment_environment",
                        SqlCommand = GenerateValidationScript(provider),
                        RiskLevel = RiskLevel.Safe,
                        Properties = new Dictionary<string, object> { ["validation_type"] = "environment" }
                    }
                }, RiskLevel.Safe));

            // Phase 2: Backup operations
            if (config.Backup.BackupBeforeDeployment && !config.Operation.SkipBackup)
            {
                deploymentPlan.Phases.Add(CreatePhase(2, "Database Backup",
                    "Create backup before schema changes", new List<DeploymentOperation>
                    {
                        new DeploymentOperation
                        {
                            Type = "BACKUP",
                            ObjectName = config.Database.DatabaseName,
                            SqlCommand = GenerateBackupScript(config),
                            RiskLevel = RiskLevel.Safe,
                            Properties = new Dictionary<string, object> { ["backup_type"] = config.Database.SqlServerBackupType }
                        }
                    }, RiskLevel.Safe));
            }

            // Phase 3-5: Drop dependent objects (views, procedures, functions)
            CreateDropDependentObjectPhases(deploymentPlan, changes, 3);

            // Phase 6-8: Drop constraints (FK, Check, Unique)
            CreateDropConstraintPhases(deploymentPlan, changes, 6);

            // Phase 9-12: Drop indexes
            CreateDropIndexPhases(deploymentPlan, changes, 9);

            // Phase 13: Drop columns
            CreateDropColumnPhase(deploymentPlan, changes, 13);

            // Phase 14: Drop tables
            CreateDropTablePhase(deploymentPlan, changes, 14);

            // Phase 15-16: Create tables and add columns
            CreateTableCreationPhases(deploymentPlan, changes, 15);

            // Phase 17-19: Alter column properties
            CreateAlterColumnPhases(deploymentPlan, changes, 17);

            // Phase 20-22: Create constraints (PK, Unique, Check)
            CreateConstraintCreationPhases(deploymentPlan, changes, 20);

            // Phase 23-25: Create indexes
            CreateIndexCreationPhases(deploymentPlan, changes, 23);

            // Phase 26: Create foreign key constraints
            CreateForeignKeyCreationPhase(deploymentPlan, changes, 26);

            // Phase 27-28: Create dependent objects (views, procedures, functions)
            CreateDependentObjectCreationPhases(deploymentPlan, changes, 27);

            // Phase 29: Post-deployment validation and cleanup
            deploymentPlan.Phases.Add(CreatePhase(29, "Post-deployment Validation",
                "Validate deployment success and perform cleanup", new List<DeploymentOperation>
                {
                    new DeploymentOperation
                    {
                        Type = "VALIDATION",
                        ObjectName = "deployment_success",
                        SqlCommand = GeneratePostDeploymentValidationScript(provider),
                        RiskLevel = RiskLevel.Safe,
                        Properties = new Dictionary<string, object> { ["validation_type"] = "post_deployment" }
                    }
                }, RiskLevel.Safe));
        }

        private async Task GenerateSimpleDeploymentAsync(SchemaValidationResult validationResult, DeploymentPlan deploymentPlan, SqlSchemaConfiguration config)
        {
            _logger.LogDebug("Generating simple deployment plan");

            var changes = validationResult.Changes.ToList();

            // Phase 1: Destructive operations (drops)
            var dropOperations = changes.Where(c => c.Type == "DROP").ToList();
            if (dropOperations.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(1, "Drop Operations",
                    "Remove obsolete database objects", ConvertChangesToOperations(dropOperations, config),
                    dropOperations.Max(o => o.RiskLevel)));
            }

            // Phase 2: Structural changes (creates and alters)
            var structuralOperations = changes.Where(c => c.Type == "CREATE" || c.Type == "ALTER").ToList();
            if (structuralOperations.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(2, "Structural Changes",
                    "Apply database structure modifications", ConvertChangesToOperations(structuralOperations, config),
                    structuralOperations.Any() ? structuralOperations.Max(o => o.RiskLevel) : RiskLevel.Safe));
            }

            // Phase 3: Post-deployment validation
            deploymentPlan.Phases.Add(CreatePhase(3, "Validation",
                "Validate deployment completion", new List<DeploymentOperation>
                {
                    new DeploymentOperation
                    {
                        Type = "VALIDATION",
                        ObjectName = "deployment_completion",
                        SqlCommand = GeneratePostDeploymentValidationScript(config.Database.GetSelectedProvider()),
                        RiskLevel = RiskLevel.Safe
                    }
                }, RiskLevel.Safe));
        }

        private void CreateDropDependentObjectPhases(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int startPhase)
        {
            // Phase 3: Drop views
            var dropViews = changes.Where(c => c.Type == "DROP" && c.ObjectType == "VIEW").ToList();
            if (dropViews.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase, "Drop Views",
                    "Remove views that depend on tables or columns being modified",
                    ConvertChangesToOperations(dropViews), RiskLevel.Warning));
            }

            // Phase 4: Drop procedures
            var dropProcedures = changes.Where(c => c.Type == "DROP" && c.ObjectType == "PROCEDURE").ToList();
            if (dropProcedures.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 1, "Drop Procedures",
                    "Remove stored procedures that depend on tables or columns being modified",
                    ConvertChangesToOperations(dropProcedures), RiskLevel.Warning));
            }

            // Phase 5: Drop functions
            var dropFunctions = changes.Where(c => c.Type == "DROP" && c.ObjectType == "FUNCTION").ToList();
            if (dropFunctions.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 2, "Drop Functions",
                    "Remove functions that depend on tables or columns being modified",
                    ConvertChangesToOperations(dropFunctions), RiskLevel.Warning));
            }
        }

        private void CreateDropConstraintPhases(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int startPhase)
        {
            // Phase 6: Drop foreign key constraints
            var dropForeignKeys = changes.Where(c => c.Type == "DROP" && c.ObjectType == "CONSTRAINT" &&
                c.Properties.ContainsKey("constraint_type") && c.Properties["constraint_type"].ToString() == "FK").ToList();
            if (dropForeignKeys.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase, "Drop Foreign Key Constraints",
                    "Remove foreign key constraints to allow table modifications",
                    ConvertChangesToOperations(dropForeignKeys), RiskLevel.Warning));
            }

            // Phase 7: Drop check constraints
            var dropChecks = changes.Where(c => c.Type == "DROP" && c.ObjectType == "CONSTRAINT" &&
                c.Properties.ContainsKey("constraint_type") && c.Properties["constraint_type"].ToString() == "CK").ToList();
            if (dropChecks.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 1, "Drop Check Constraints",
                    "Remove check constraints that may conflict with data changes",
                    ConvertChangesToOperations(dropChecks), RiskLevel.Safe));
            }

            // Phase 8: Drop unique constraints
            var dropUnique = changes.Where(c => c.Type == "DROP" && c.ObjectType == "CONSTRAINT" &&
                c.Properties.ContainsKey("constraint_type") && c.Properties["constraint_type"].ToString() == "UQ").ToList();
            if (dropUnique.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 2, "Drop Unique Constraints",
                    "Remove unique constraints to allow data modifications",
                    ConvertChangesToOperations(dropUnique), RiskLevel.Safe));
            }
        }

        private void CreateDropIndexPhases(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int startPhase)
        {
            // Phase 9: Drop non-clustered indexes
            var dropNonClusteredIndexes = changes.Where(c => c.Type == "DROP" && c.ObjectType == "INDEX" &&
                (!c.Properties.ContainsKey("is_clustered") || !(bool)c.Properties["is_clustered"])).ToList();
            if (dropNonClusteredIndexes.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase, "Drop Non-Clustered Indexes",
                    "Remove non-clustered indexes to improve modification performance",
                    ConvertChangesToOperations(dropNonClusteredIndexes), RiskLevel.Safe));
            }

            // Phase 10: Drop clustered indexes
            var dropClusteredIndexes = changes.Where(c => c.Type == "DROP" && c.ObjectType == "INDEX" &&
                c.Properties.ContainsKey("is_clustered") && (bool)c.Properties["is_clustered"]).ToList();
            if (dropClusteredIndexes.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 1, "Drop Clustered Indexes",
                    "Remove clustered indexes (causes table reorganization)",
                    ConvertChangesToOperations(dropClusteredIndexes), RiskLevel.Warning));
            }
        }

        private void CreateDropColumnPhase(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int phase)
        {
            var dropColumns = changes.Where(c => c.Type == "ALTER" && c.ObjectType == "COLUMN" &&
                c.Description.Contains("Drop column")).ToList();
            if (dropColumns.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(phase, "Drop Columns",
                    "Remove columns (permanent data loss)",
                    ConvertChangesToOperations(dropColumns), RiskLevel.Risky, true));
            }
        }

        private void CreateDropTablePhase(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int phase)
        {
            var dropTables = changes.Where(c => c.Type == "DROP" && c.ObjectType == "TABLE").ToList();
            if (dropTables.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(phase, "Drop Tables",
                    "Remove tables (permanent data loss)",
                    ConvertChangesToOperations(dropTables), RiskLevel.Risky, true));
            }
        }

        private void CreateTableCreationPhases(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int startPhase)
        {
            // Phase 15: Create tables
            var createTables = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "TABLE").ToList();
            if (createTables.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase, "Create Tables",
                    "Add new tables to the database",
                    ConvertChangesToOperations(createTables), RiskLevel.Safe));
            }

            // Phase 16: Add columns
            var addColumns = changes.Where(c => c.Type == "ALTER" && c.ObjectType == "COLUMN" &&
                c.Description.Contains("Add column")).ToList();
            if (addColumns.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 1, "Add Columns",
                    "Add new columns to existing tables",
                    ConvertChangesToOperations(addColumns), RiskLevel.Safe));
            }
        }

        private void CreateAlterColumnPhases(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int startPhase)
        {
            // Phase 17: Alter column data types
            var alterColumnTypes = changes.Where(c => c.Type == "ALTER" && c.ObjectType == "COLUMN" &&
                c.Description.Contains("data type")).ToList();
            if (alterColumnTypes.Any())
            {
                var riskLevel = alterColumnTypes.Any(c => c.Properties.ContainsKey("potential_data_loss") &&
                    (bool)c.Properties["potential_data_loss"]) ? RiskLevel.Risky : RiskLevel.Warning;

                deploymentPlan.Phases.Add(CreatePhase(startPhase, "Alter Column Data Types",
                    "Modify column data types (potential data conversion)",
                    ConvertChangesToOperations(alterColumnTypes), riskLevel, riskLevel == RiskLevel.Risky));
            }

            // Phase 18: Alter column nullability
            var alterColumnNullability = changes.Where(c => c.Type == "ALTER" && c.ObjectType == "COLUMN" &&
                c.Description.Contains("nullable")).ToList();
            if (alterColumnNullability.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 1, "Alter Column Nullability",
                    "Modify column null/not null constraints",
                    ConvertChangesToOperations(alterColumnNullability), RiskLevel.Warning));
            }

            // Phase 19: Alter column defaults
            var alterColumnDefaults = changes.Where(c => c.Type == "ALTER" && c.ObjectType == "COLUMN" &&
                c.Description.Contains("default")).ToList();
            if (alterColumnDefaults.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 2, "Alter Column Defaults",
                    "Modify column default values",
                    ConvertChangesToOperations(alterColumnDefaults), RiskLevel.Safe));
            }
        }

        private void CreateConstraintCreationPhases(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int startPhase)
        {
            // Phase 20: Create primary key constraints
            var createPrimaryKeys = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "CONSTRAINT" &&
                c.Properties.ContainsKey("constraint_type") && c.Properties["constraint_type"].ToString() == "PK").ToList();
            if (createPrimaryKeys.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase, "Create Primary Key Constraints",
                    "Add primary key constraints to tables",
                    ConvertChangesToOperations(createPrimaryKeys), RiskLevel.Safe));
            }

            // Phase 21: Create unique constraints
            var createUnique = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "CONSTRAINT" &&
                c.Properties.ContainsKey("constraint_type") && c.Properties["constraint_type"].ToString() == "UQ").ToList();
            if (createUnique.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 1, "Create Unique Constraints",
                    "Add unique constraints to enforce data uniqueness",
                    ConvertChangesToOperations(createUnique), RiskLevel.Warning));
            }

            // Phase 22: Create check constraints
            var createChecks = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "CONSTRAINT" &&
                c.Properties.ContainsKey("constraint_type") && c.Properties["constraint_type"].ToString() == "CK").ToList();
            if (createChecks.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 2, "Create Check Constraints",
                    "Add check constraints to enforce data validation rules",
                    ConvertChangesToOperations(createChecks), RiskLevel.Warning));
            }
        }

        private void CreateIndexCreationPhases(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int startPhase)
        {
            // Phase 23: Create clustered indexes
            var createClusteredIndexes = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "INDEX" &&
                c.Properties.ContainsKey("is_clustered") && (bool)c.Properties["is_clustered"]).ToList();
            if (createClusteredIndexes.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase, "Create Clustered Indexes",
                    "Add clustered indexes (causes table reorganization)",
                    ConvertChangesToOperations(createClusteredIndexes), RiskLevel.Warning));
            }

            // Phase 24: Create unique non-clustered indexes
            var createUniqueIndexes = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "INDEX" &&
                c.Properties.ContainsKey("is_unique") && (bool)c.Properties["is_unique"] &&
                (!c.Properties.ContainsKey("is_clustered") || !(bool)c.Properties["is_clustered"])).ToList();
            if (createUniqueIndexes.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 1, "Create Unique Non-Clustered Indexes",
                    "Add unique non-clustered indexes for performance and uniqueness",
                    ConvertChangesToOperations(createUniqueIndexes), RiskLevel.Safe));
            }

            // Phase 25: Create non-clustered indexes
            var createNonClusteredIndexes = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "INDEX" &&
                (!c.Properties.ContainsKey("is_unique") || !(bool)c.Properties["is_unique"]) &&
                (!c.Properties.ContainsKey("is_clustered") || !(bool)c.Properties["is_clustered"])).ToList();
            if (createNonClusteredIndexes.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 2, "Create Non-Clustered Indexes",
                    "Add non-clustered indexes for query performance",
                    ConvertChangesToOperations(createNonClusteredIndexes), RiskLevel.Safe));
            }
        }

        private void CreateForeignKeyCreationPhase(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int phase)
        {
            var createForeignKeys = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "CONSTRAINT" &&
                c.Properties.ContainsKey("constraint_type") && c.Properties["constraint_type"].ToString() == "FK").ToList();
            if (createForeignKeys.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(phase, "Create Foreign Key Constraints",
                    "Add foreign key constraints to enforce referential integrity",
                    ConvertChangesToOperations(createForeignKeys), RiskLevel.Warning));
            }
        }

        private void CreateDependentObjectCreationPhases(DeploymentPlan deploymentPlan, List<SchemaChange> changes, int startPhase)
        {
            // Phase 27: Create views
            var createViews = changes.Where(c => c.Type == "CREATE" && c.ObjectType == "VIEW").ToList();
            if (createViews.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase, "Create Views",
                    "Add views that depend on modified tables",
                    ConvertChangesToOperations(createViews), RiskLevel.Safe));
            }

            // Phase 28: Create procedures and functions
            var createProcedures = changes.Where(c => c.Type == "CREATE" &&
                (c.ObjectType == "PROCEDURE" || c.ObjectType == "FUNCTION")).ToList();
            if (createProcedures.Any())
            {
                deploymentPlan.Phases.Add(CreatePhase(startPhase + 1, "Create Procedures and Functions",
                    "Add stored procedures and functions that depend on modified schema",
                    ConvertChangesToOperations(createProcedures), RiskLevel.Safe));
            }
        }

        private DeploymentPhase CreatePhase(int phaseNumber, string name, string description,
            List<DeploymentOperation> operations, RiskLevel riskLevel = RiskLevel.Safe, bool requiresApproval = false)
        {
            return new DeploymentPhase
            {
                PhaseNumber = phaseNumber,
                Name = name,
                Description = description,
                Operations = operations ?? new List<DeploymentOperation>(),
                RiskLevel = riskLevel,
                RequiresApproval = requiresApproval || riskLevel == RiskLevel.Risky,
                CanRollback = CalculateRollbackCapability(operations),
                Dependencies = new List<string>(),
                Properties = new Dictionary<string, object>
                {
                    ["operation_count"] = operations?.Count ?? 0,
                    ["risk_level"] = riskLevel.ToString()
                }
            };
        }

        private List<DeploymentOperation> ConvertChangesToOperations(List<SchemaChange> changes, SqlSchemaConfiguration config = null)
        {
            return changes.Select(change => new DeploymentOperation
            {
                Type = change.Type,
                ObjectName = change.ObjectName,
                Schema = change.Schema,
                SqlCommand = GenerateSqlForChange(change, config),
                RollbackCommand = GenerateRollbackSqlForChange(change, config),
                RiskLevel = change.RiskLevel,
                Dependencies = change.Dependencies,
                Properties = new Dictionary<string, object>(change.Properties)
                {
                    ["object_type"] = change.ObjectType,
                    ["description"] = change.Description
                }
            }).ToList();
        }

        private string GenerateSqlForChange(SchemaChange change, SqlSchemaConfiguration config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config), "Configuration is required for SQL generation");

            var provider = config.Database.GetSelectedProvider();
            var schema = change.Schema ?? GetDefaultSchema(provider);

            // This is a simplified SQL generator - real implementation would need full schema change context
            return change.Type switch
            {
                "CREATE" when change.ObjectType == "TABLE" =>
                    $"CREATE TABLE {FormatObjectName(change.ObjectName, provider, schema)} ( /* columns from change.Properties */ );",
                "CREATE" when change.ObjectType == "INDEX" =>
                    $"CREATE INDEX {FormatIdentifier(change.ObjectName, provider)} ON {GetTableNameFromChange(change, provider, schema)} ( /* columns */ );",
                "DROP" when change.ObjectType == "TABLE" =>
                    $"DROP TABLE {FormatObjectName(change.ObjectName, provider, schema)};",
                "DROP" when change.ObjectType == "CONSTRAINT" =>
                    $"ALTER TABLE {GetTableNameFromChange(change, provider, schema)} DROP CONSTRAINT {FormatIdentifier(change.ObjectName, provider)};",
                _ => $"-- {change.Type} {change.ObjectType} {change.ObjectName}: {change.Description}"
            };
        }

        private string GenerateRollbackSqlForChange(SchemaChange change, SqlSchemaConfiguration config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config), "Configuration is required for rollback SQL generation");

            return change.Type switch
            {
                "CREATE" => $"-- Rollback: DROP {change.ObjectType} {change.ObjectName}",
                "DROP" => $"-- Rollback: Cannot automatically recreate dropped {change.ObjectType} {change.ObjectName}",
                "ALTER" => $"-- Rollback: Manual intervention required for ALTER {change.ObjectType} {change.ObjectName}",
                _ => $"-- Rollback: Unknown operation type {change.Type}"
            };
        }

        private string GetTableNameFromChange(SchemaChange change, string provider, string schema)
        {
            if (change.Properties.ContainsKey("table_name"))
            {
                return FormatObjectName(change.Properties["table_name"].ToString()!, provider, schema);
            }
            return FormatObjectName("UnknownTable", provider, schema);
        }

        private bool CalculateRollbackCapability(List<DeploymentOperation> operations)
        {
            var nonRollbackTypes = new[] { "DROP", "DELETE", "TRUNCATE" };
            return operations?.All(op => !nonRollbackTypes.Contains(op.Type.ToUpperInvariant())) ?? true;
        }

        private string GenerateValidationScript(string provider)
        {
            return provider switch
            {
                "sqlserver" => "SELECT @@VERSION AS ServerVersion, DB_NAME() AS CurrentDatabase;",
                "postgresql" => "SELECT version() AS server_version, current_database() AS current_database;",
                "mysql" => "SELECT VERSION() AS server_version, DATABASE() AS current_database;",
                "oracle" => "SELECT * FROM v$version WHERE banner LIKE 'Oracle%';",
                "sqlite" => "SELECT sqlite_version() AS sqlite_version;",
                _ => throw new NotSupportedException($"Validation script not supported for provider: {provider}")
            };
        }

        private string GenerateBackupScript(SqlSchemaConfiguration config)
        {
            var provider = config.Database.GetSelectedProvider();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

            return provider switch
            {
                "sqlserver" => $"BACKUP DATABASE [{config.Database.DatabaseName}] TO DISK = N'backup_{timestamp}.bak';",
                "postgresql" => $"-- pg_dump {config.Database.DatabaseName} > backup_{timestamp}.sql",
                "mysql" => $"-- mysqldump {config.Database.DatabaseName} > backup_{timestamp}.sql",
                _ => throw new NotSupportedException($"Backup command not supported for provider: {provider}")
            };
        }

        private string GeneratePostDeploymentValidationScript(string provider)
        {
            return provider switch
            {
                "sqlserver" => "SELECT COUNT(*) AS table_count FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';",
                "postgresql" => "SELECT COUNT(*) AS table_count FROM information_schema.tables WHERE table_type = 'BASE TABLE';",
                "mysql" => "SELECT COUNT(*) AS table_count FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';",
                "oracle" => "SELECT COUNT(*) AS table_count FROM user_tables;",
                "sqlite" => "SELECT COUNT(*) AS table_count FROM sqlite_master WHERE type='table';",
                _ => throw new NotSupportedException($"Post-deployment validation not supported for provider: {provider}")
            };
        }

        private void ApplyCustomPhaseOrdering(DeploymentPlan deploymentPlan, string customPhaseOrder)
        {
            try
            {
                var phaseOrder = customPhaseOrder.Split(',').Select(int.Parse).ToList();
                var reorderedPhases = new List<DeploymentPhase>();

                foreach (var phaseNumber in phaseOrder)
                {
                    var phase = deploymentPlan.Phases.FirstOrDefault(p => p.PhaseNumber == phaseNumber);
                    if (phase != null)
                    {
                        reorderedPhases.Add(phase);
                    }
                }

                // Add any phases not specified in the custom order
                var unspecifiedPhases = deploymentPlan.Phases
                    .Where(p => !phaseOrder.Contains(p.PhaseNumber))
                    .OrderBy(p => p.PhaseNumber);
                reorderedPhases.AddRange(unspecifiedPhases);

                deploymentPlan.Phases = reorderedPhases;
                _logger.LogInformation("Applied custom phase ordering: {PhaseOrder}", customPhaseOrder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply custom phase ordering: {PhaseOrder}", customPhaseOrder);
                throw new SqlSchemaException(SqlSchemaExitCode.SchemaValidationFailure,
                    $"Invalid custom phase order format: {customPhaseOrder}", ex);
            }
        }


        private void SkipWarningPhases(DeploymentPlan deploymentPlan)
        {
            var phasesToSkip = deploymentPlan.Phases.Where(p => p.RiskLevel == RiskLevel.Warning && !p.RequiresApproval).ToList();

            foreach (var phase in phasesToSkip)
            {
                deploymentPlan.Phases.Remove(phase);
                _logger.LogInformation("Skipped warning phase: {PhaseName}", phase.Name);
            }

            // Renumber remaining phases
            for (int i = 0; i < deploymentPlan.Phases.Count; i++)
            {
                deploymentPlan.Phases[i].PhaseNumber = i + 1;
            }
        }

        private void ValidateDeploymentPlan(DeploymentPlan deploymentPlan, SchemaValidationResult validationResult)
        {
            // Ensure all changes are represented in the deployment plan
            var planOperations = deploymentPlan.Phases.SelectMany(p => p.Operations).ToList();
            var changeCount = validationResult.Changes.Count;
            var operationCount = planOperations.Count(op => op.Type != "VALIDATION" && op.Type != "BACKUP");

            if (operationCount < changeCount)
            {
                _logger.LogWarning("Deployment plan may be incomplete: {OperationCount} operations for {ChangeCount} changes",
                    operationCount, changeCount);
            }

            // Validate phase dependencies
            ValidatePhaseDependencies(deploymentPlan);

            // Ensure risky operations are properly flagged
            var riskyOperations = planOperations.Where(op => op.RiskLevel == RiskLevel.Risky).ToList();
            var riskyPhases = deploymentPlan.Phases.Where(p => p.RiskLevel == RiskLevel.Risky).ToList();

            if (riskyOperations.Any() && !riskyPhases.Any())
            {
                _logger.LogWarning("Risky operations detected but no risky phases marked for approval");
            }

            _logger.LogDebug("Deployment plan validation complete: {PhaseCount} phases, {OperationCount} operations",
                deploymentPlan.Phases.Count, planOperations.Count);
        }

        private void ValidatePhaseDependencies(DeploymentPlan deploymentPlan)
        {
            // Check for proper ordering of dependent operations
            var phasesByNumber = deploymentPlan.Phases.OrderBy(p => p.PhaseNumber).ToList();

            // Drops should come before creates
            var lastDropPhase = phasesByNumber.LastOrDefault(p => p.Operations.Any(op => op.Type == "DROP"));
            var firstCreatePhase = phasesByNumber.FirstOrDefault(p => p.Operations.Any(op => op.Type == "CREATE"));

            if (lastDropPhase != null && firstCreatePhase != null &&
                lastDropPhase.PhaseNumber > firstCreatePhase.PhaseNumber)
            {
                _logger.LogWarning("Potential dependency issue: DROP operations after CREATE operations");
            }

            // Foreign key constraints should come after table creation
            var lastTableCreatePhase = phasesByNumber.LastOrDefault(p =>
                p.Operations.Any(op => op.Type == "CREATE" &&
                op.Properties.ContainsKey("object_type") &&
                op.Properties["object_type"].ToString() == "TABLE"));

            var firstFkPhase = phasesByNumber.FirstOrDefault(p =>
                p.Operations.Any(op => op.Type == "CREATE" &&
                op.Properties.ContainsKey("object_type") &&
                op.Properties["object_type"].ToString() == "CONSTRAINT" &&
                op.Properties.ContainsKey("constraint_type") &&
                op.Properties["constraint_type"].ToString() == "FK"));

            if (lastTableCreatePhase != null && firstFkPhase != null &&
                firstFkPhase.PhaseNumber < lastTableCreatePhase.PhaseNumber)
            {
                _logger.LogWarning("Potential dependency issue: Foreign key constraints before table creation");
            }
        }

        private string FormatObjectName(string objectName, string provider, string schema)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                throw new ArgumentException("Object name cannot be null or empty", nameof(objectName));

            var formattedName = FormatIdentifier(objectName, provider);

            if (!string.IsNullOrEmpty(schema) && schema != GetDefaultSchema(provider))
            {
                var formattedSchema = FormatIdentifier(schema, provider);
                return $"{formattedSchema}.{formattedName}";
            }

            return formattedName;
        }

        private string FormatIdentifier(string identifier, string provider)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));

            return provider switch
            {
                "sqlserver" => $"[{identifier}]",
                "postgresql" => $"\"{identifier}\"",
                "mysql" => $"`{identifier}`",
                "oracle" => $"\"{identifier}\"",
                "sqlite" => $"[{identifier}]",
                _ => throw new NotSupportedException($"Identifier formatting not supported for provider: {provider}")
            };
        }

        private string GetDefaultSchema(string provider)
        {
            return provider switch
            {
                "sqlserver" => "dbo",
                "postgresql" => "public",
                "mysql" => "",
                "oracle" => "SYSTEM",
                "sqlite" => "",
                _ => throw new NotSupportedException($"Default schema not defined for provider: {provider}")
            };
        }
    }
}