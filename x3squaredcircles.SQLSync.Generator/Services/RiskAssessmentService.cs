using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IRiskAssessmentService
    {
        Task<RiskAssessment> AssessRiskAsync(SchemaValidationResult validationResult, SqlSchemaConfiguration config);
    }

    public class RiskAssessmentService : IRiskAssessmentService
    {
        private readonly ILogger<RiskAssessmentService> _logger;

        public RiskAssessmentService(ILogger<RiskAssessmentService> logger)
        {
            _logger = logger;
        }

        public async Task<RiskAssessment> AssessRiskAsync(SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Assessing deployment risk for {ChangeCount} schema changes", validationResult.Changes.Count);

                var riskAssessment = new RiskAssessment
                {
                    RiskFactors = new List<RiskFactor>(),
                    AssessmentTime = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object>
                    {
                        ["total_changes"] = validationResult.Changes.Count,
                        ["validation_errors"] = validationResult.Errors.Count,
                        ["validation_warnings"] = validationResult.Warnings.Count,
                        ["environment"] = config.Environment.Environment,
                        ["database_provider"] = config.Database.GetSelectedProvider()
                    }
                };

                // Assess individual schema changes
                await AssessSchemaChangesAsync(validationResult, riskAssessment, config);

                // Assess validation errors and warnings
                await AssessValidationIssuesAsync(validationResult, riskAssessment, config);

                // Assess environment-specific risks
                await AssessEnvironmentRisksAsync(riskAssessment, config);

                // Assess data loss risks
                await AssessDataLossRisksAsync(validationResult, riskAssessment, config);

                // Assess performance impact risks
                await AssessPerformanceRisksAsync(validationResult, riskAssessment, config);

                // Assess dependency risks
                await AssessDependencyRisksAsync(validationResult, riskAssessment, config);

                // Calculate overall risk level and requirements
                CalculateOverallRisk(riskAssessment);

                _logger.LogInformation("✓ Risk assessment complete: {RiskLevel} - {RiskFactorCount} risk factors identified",
                    riskAssessment.OverallRiskLevel, riskAssessment.RiskFactors.Count);

                return riskAssessment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Risk assessment failed");
                throw new SqlSchemaException(SqlSchemaExitCode.SchemaValidationFailure,
                    $"Risk assessment failed: {ex.Message}", ex);
            }
        }

        private async Task AssessSchemaChangesAsync(SchemaValidationResult validationResult, RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            var operationCounts = new Dictionary<RiskLevel, int>
            {
                [RiskLevel.Safe] = 0,
                [RiskLevel.Warning] = 0,
                [RiskLevel.Risky] = 0
            };

            foreach (var change in validationResult.Changes)
            {
                operationCounts[change.RiskLevel]++;

                // Assess specific high-risk operations
                await AssessSpecificOperationRisk(change, riskAssessment, config);
            }

            riskAssessment.SafeOperations = operationCounts[RiskLevel.Safe];
            riskAssessment.WarningOperations = operationCounts[RiskLevel.Warning];
            riskAssessment.RiskyOperations = operationCounts[RiskLevel.Risky];

            // Add risk factors for operation counts
            if (riskAssessment.RiskyOperations > 0)
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "High-Risk Operations",
                    Description = $"{riskAssessment.RiskyOperations} risky operations detected (DROP, ALTER with data loss potential)",
                    RiskLevel = RiskLevel.Risky,
                    Category = "Operations",
                    AffectedObjects = validationResult.Changes.Where(c => c.RiskLevel == RiskLevel.Risky).Select(c => c.ObjectName).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["risky_operation_count"] = riskAssessment.RiskyOperations,
                        ["requires_dual_approval"] = true
                    }
                });
            }

            if (riskAssessment.WarningOperations > 5)
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "High Volume Warning Operations",
                    Description = $"{riskAssessment.WarningOperations} warning-level operations may require review",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Volume",
                    AffectedObjects = validationResult.Changes.Where(c => c.RiskLevel == RiskLevel.Warning).Take(5).Select(c => c.ObjectName).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["warning_operation_count"] = riskAssessment.WarningOperations
                    }
                });
            }
        }

        private async Task AssessSpecificOperationRisk(SchemaChange change, RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            // DROP TABLE operations
            if (change.Type == "DROP" && change.ObjectType == "TABLE")
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Table Drop Operation",
                    Description = $"Table '{change.ObjectName}' will be dropped, resulting in complete data loss",
                    RiskLevel = RiskLevel.Risky,
                    Category = "Data Loss",
                    AffectedObjects = new List<string> { change.ObjectName },
                    Properties = new Dictionary<string, object>
                    {
                        ["operation_type"] = "DROP_TABLE",
                        ["data_loss"] = true,
                        ["reversible"] = false
                    }
                });
            }

            // DROP COLUMN operations
            if (change.Type == "ALTER" && change.ObjectType == "COLUMN" && change.Description.Contains("Drop column"))
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Column Drop Operation",
                    Description = $"Column '{change.ObjectName}' will be dropped, resulting in data loss",
                    RiskLevel = RiskLevel.Risky,
                    Category = "Data Loss",
                    AffectedObjects = new List<string> { change.ObjectName },
                    Properties = new Dictionary<string, object>
                    {
                        ["operation_type"] = "DROP_COLUMN",
                        ["data_loss"] = true,
                        ["reversible"] = false
                    }
                });
            }

            // Data type changes with potential loss
            if (change.Type == "ALTER" && change.ObjectType == "COLUMN" &&
                change.Properties.ContainsKey("potential_data_loss") &&
                (bool)change.Properties["potential_data_loss"])
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Data Type Change with Potential Loss",
                    Description = $"Column '{change.ObjectName}' data type change may cause data loss or conversion errors",
                    RiskLevel = RiskLevel.Risky,
                    Category = "Data Integrity",
                    AffectedObjects = new List<string> { change.ObjectName },
                    Properties = new Dictionary<string, object>
                    {
                        ["operation_type"] = "ALTER_COLUMN_TYPE",
                        ["old_type"] = change.Properties.GetValueOrDefault("old_data_type", "unknown"),
                        ["new_type"] = change.Properties.GetValueOrDefault("new_data_type", "unknown"),
                        ["potential_data_loss"] = true
                    }
                });
            }

            // Large clustered index operations
            if (change.Type == "CREATE" && change.ObjectType == "INDEX" &&
                change.Properties.ContainsKey("is_clustered") &&
                (bool)change.Properties["is_clustered"])
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Clustered Index Creation",
                    Description = $"Creating clustered index '{change.ObjectName}' will reorganize table data and may take significant time",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Performance",
                    AffectedObjects = new List<string> { change.ObjectName },
                    Properties = new Dictionary<string, object>
                    {
                        ["operation_type"] = "CREATE_CLUSTERED_INDEX",
                        ["table_reorganization"] = true,
                        ["blocking_operation"] = true
                    }
                });
            }

            // Primary key drops
            if (change.Type == "DROP" && change.ObjectType == "CONSTRAINT" &&
                change.Properties.ContainsKey("constraint_type") &&
                change.Properties["constraint_type"].ToString() == "PK")
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Primary Key Constraint Drop",
                    Description = $"Dropping primary key constraint '{change.ObjectName}' may affect replication and references",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Data Integrity",
                    AffectedObjects = new List<string> { change.ObjectName },
                    Properties = new Dictionary<string, object>
                    {
                        ["operation_type"] = "DROP_PRIMARY_KEY",
                        ["affects_replication"] = true,
                        ["affects_references"] = true
                    }
                });
            }
        }

        private async Task AssessValidationIssuesAsync(SchemaValidationResult validationResult, RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            // Critical validation errors
            if (validationResult.Errors.Any())
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Schema Validation Errors",
                    Description = $"{validationResult.Errors.Count} critical validation errors must be resolved before deployment",
                    RiskLevel = RiskLevel.Risky,
                    Category = "Validation",
                    AffectedObjects = validationResult.Errors.Select(e => e.ObjectName).Distinct().ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["error_count"] = validationResult.Errors.Count,
                        ["deployment_blocking"] = true,
                        ["error_codes"] = validationResult.Errors.Select(e => e.Code).Distinct().ToList()
                    }
                });
            }

            // High-priority warnings
            var riskyWarnings = validationResult.Warnings.Where(w => w.RiskLevel == RiskLevel.Risky).ToList();
            if (riskyWarnings.Any())
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "High-Risk Validation Warnings",
                    Description = $"{riskyWarnings.Count} high-risk warnings require careful review",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Validation",
                    AffectedObjects = riskyWarnings.Select(w => w.ObjectName).Distinct().ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["risky_warning_count"] = riskyWarnings.Count,
                        ["warning_codes"] = riskyWarnings.Select(w => w.Code).Distinct().ToList()
                    }
                });
            }
        }

        private async Task AssessEnvironmentRisksAsync(RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            var environment = config.Environment.Environment.ToLowerInvariant();

            // Production environment risks
            if (environment == "prod")
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Production Environment Deployment",
                    Description = "Deployment to production environment requires heightened caution and approval",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Environment",
                    AffectedObjects = new List<string>(),
                    Properties = new Dictionary<string, object>
                    {
                        ["environment"] = "production",
                        ["requires_approval"] = true,
                        ["backup_required"] = true
                    }
                });

                // Additional production-specific checks
                if (config.Operation.SkipBackup)
                {
                    riskAssessment.RiskFactors.Add(new RiskFactor
                    {
                        Name = "Production Deployment Without Backup",
                        Description = "Production deployment with backup disabled is extremely risky",
                        RiskLevel = RiskLevel.Risky,
                        Category = "Environment",
                        AffectedObjects = new List<string>(),
                        Properties = new Dictionary<string, object>
                        {
                            ["environment"] = "production",
                            ["backup_disabled"] = true,
                            ["recovery_impact"] = "severe"
                        }
                    });
                }
            }

            // Beta environment risks
            if (environment == "beta")
            {
                if (string.IsNullOrEmpty(config.Environment.Vertical))
                {
                    riskAssessment.RiskFactors.Add(new RiskFactor
                    {
                        Name = "Beta Deployment Missing Vertical",
                        Description = "Beta environment deployment requires vertical specification",
                        RiskLevel = RiskLevel.Warning,
                        Category = "Configuration",
                        AffectedObjects = new List<string>(),
                        Properties = new Dictionary<string, object>
                        {
                            ["environment"] = "beta",
                            ["missing_vertical"] = true
                        }
                    });
                }
            }
        }

        private async Task AssessDataLossRisksAsync(SchemaValidationResult validationResult, RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            var dataLossOperations = validationResult.Changes
                .Where(c => c.Properties.ContainsKey("potential_data_loss") && (bool)c.Properties["potential_data_loss"])
                .ToList();

            if (dataLossOperations.Any())
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Potential Data Loss Operations",
                    Description = $"{dataLossOperations.Count} operations may result in data loss or corruption",
                    RiskLevel = RiskLevel.Risky,
                    Category = "Data Loss",
                    AffectedObjects = dataLossOperations.Select(o => o.ObjectName).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["data_loss_operation_count"] = dataLossOperations.Count,
                        ["backup_critical"] = true,
                        ["rollback_complexity"] = "high"
                    }
                });
            }

            // Check for non-nullable columns without defaults on existing tables
            var nonNullableAdditions = validationResult.Changes
                .Where(c => c.Type == "ALTER" && c.ObjectType == "COLUMN" &&
                           c.Properties.ContainsKey("is_nullable") && !(bool)c.Properties["is_nullable"] &&
                           c.Properties.ContainsKey("has_default") && !(bool)c.Properties["has_default"])
                .ToList();

            if (nonNullableAdditions.Any())
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Non-Nullable Columns Without Defaults",
                    Description = $"{nonNullableAdditions.Count} non-nullable columns added without default values may cause deployment failure",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Data Integrity",
                    AffectedObjects = nonNullableAdditions.Select(o => o.ObjectName).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["non_nullable_additions"] = nonNullableAdditions.Count,
                        ["deployment_failure_risk"] = true
                    }
                });
            }
        }

        private async Task AssessPerformanceRisksAsync(SchemaValidationResult validationResult, RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            // Large table operations
            var tableOperations = validationResult.Changes.Where(c => c.ObjectType == "TABLE").ToList();
            if (tableOperations.Count > 10)
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "High Volume Table Operations",
                    Description = $"{tableOperations.Count} table operations may cause extended deployment time and lock contention",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Performance",
                    AffectedObjects = tableOperations.Select(o => o.ObjectName).Take(10).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["table_operation_count"] = tableOperations.Count,
                        ["lock_contention_risk"] = true,
                        ["deployment_time_impact"] = "high"
                    }
                });
            }

            // Index operations on large tables
            var indexOperations = validationResult.Changes.Where(c => c.ObjectType == "INDEX").ToList();
            if (indexOperations.Count > 20)
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "High Volume Index Operations",
                    Description = $"{indexOperations.Count} index operations may cause significant performance impact during deployment",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Performance",
                    AffectedObjects = indexOperations.Select(o => o.ObjectName).Take(10).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["index_operation_count"] = indexOperations.Count,
                        ["performance_impact"] = "high",
                        ["cpu_intensive"] = true
                    }
                });
            }

            // Column data type changes
            var dataTypeChanges = validationResult.Changes
                .Where(c => c.Type == "ALTER" && c.ObjectType == "COLUMN" &&
                           c.Properties.ContainsKey("old_data_type") && c.Properties.ContainsKey("new_data_type"))
                .ToList();

            if (dataTypeChanges.Count > 5)
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Multiple Data Type Changes",
                    Description = $"{dataTypeChanges.Count} data type changes may require table rebuilds and cause performance impact",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Performance",
                    AffectedObjects = dataTypeChanges.Select(o => o.ObjectName).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["data_type_change_count"] = dataTypeChanges.Count,
                        ["table_rebuild_risk"] = true,
                        ["io_intensive"] = true
                    }
                });
            }
        }

        private async Task AssessDependencyRisksAsync(SchemaValidationResult validationResult, RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            // Foreign key constraint operations
            var foreignKeyOperations = validationResult.Changes
                .Where(c => c.ObjectType == "CONSTRAINT" &&
                           c.Properties.ContainsKey("constraint_type") &&
                           c.Properties["constraint_type"].ToString() == "FK")
                .ToList();

            if (foreignKeyOperations.Any())
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Foreign Key Constraint Changes",
                    Description = $"{foreignKeyOperations.Count} foreign key changes may affect referential integrity and dependent applications",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Dependencies",
                    AffectedObjects = foreignKeyOperations.Select(o => o.ObjectName).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["foreign_key_operation_count"] = foreignKeyOperations.Count,
                        ["referential_integrity_impact"] = true,
                        ["application_dependency_risk"] = true
                    }
                });
            }

            // View and procedure drops
            var viewDrops = validationResult.Changes.Where(c => c.Type == "DROP" && c.ObjectType == "VIEW").ToList();
            var procedureDrops = validationResult.Changes.Where(c => c.Type == "DROP" && c.ObjectType == "PROCEDURE").ToList();

            if (viewDrops.Any() || procedureDrops.Any())
            {
                riskAssessment.RiskFactors.Add(new RiskFactor
                {
                    Name = "Dependent Object Drops",
                    Description = $"{viewDrops.Count} views and {procedureDrops.Count} procedures will be dropped, potentially breaking dependent applications",
                    RiskLevel = RiskLevel.Warning,
                    Category = "Dependencies",
                    AffectedObjects = viewDrops.Concat(procedureDrops).Select(o => o.ObjectName).ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        ["view_drops"] = viewDrops.Count,
                        ["procedure_drops"] = procedureDrops.Count,
                        ["application_break_risk"] = true
                    }
                });
            }

            // Cross-schema references when disabled
            if (!config.SchemaAnalysis.EnableCrossSchemaRefs)
            {
                var crossSchemaWarnings = validationResult.Warnings
                    .Where(w => w.Code == "CROSS_SCHEMA_REFERENCE_DISABLED")
                    .ToList();

                if (crossSchemaWarnings.Any())
                {
                    riskAssessment.RiskFactors.Add(new RiskFactor
                    {
                        Name = "Cross-Schema References Disabled",
                        Description = $"{crossSchemaWarnings.Count} cross-schema references detected but disabled - may cause constraint creation failures",
                        RiskLevel = RiskLevel.Warning,
                        Category = "Configuration",
                        AffectedObjects = crossSchemaWarnings.Select(w => w.ObjectName).ToList(),
                        Properties = new Dictionary<string, object>
                        {
                            ["cross_schema_warning_count"] = crossSchemaWarnings.Count,
                            ["constraint_failure_risk"] = true
                        }
                    });
                }
            }
        }

        private void CalculateOverallRisk(RiskAssessment riskAssessment)
        {
            // Determine overall risk level based on risk factors and operation counts
            if (riskAssessment.RiskFactors.Any(rf => rf.RiskLevel == RiskLevel.Risky) ||
                riskAssessment.RiskyOperations > 0)
            {
                riskAssessment.OverallRiskLevel = RiskLevel.Risky;
                riskAssessment.RequiresDualApproval = true;
                riskAssessment.RequiresApproval = true;
            }
            else if (riskAssessment.RiskFactors.Any(rf => rf.RiskLevel == RiskLevel.Warning) ||
                     riskAssessment.WarningOperations > 0)
            {
                riskAssessment.OverallRiskLevel = RiskLevel.Warning;
                riskAssessment.RequiresApproval = true;
                riskAssessment.RequiresDualApproval = false;
            }
            else
            {
                riskAssessment.OverallRiskLevel = RiskLevel.Safe;
                riskAssessment.RequiresApproval = false;
                riskAssessment.RequiresDualApproval = false;
            }

            // Add additional metadata
            riskAssessment.Metadata["risk_factor_count"] = riskAssessment.RiskFactors.Count;
            riskAssessment.Metadata["risky_factor_count"] = riskAssessment.RiskFactors.Count(rf => rf.RiskLevel == RiskLevel.Risky);
            riskAssessment.Metadata["warning_factor_count"] = riskAssessment.RiskFactors.Count(rf => rf.RiskLevel == RiskLevel.Warning);
            riskAssessment.Metadata["requires_approval"] = riskAssessment.RequiresApproval;
            riskAssessment.Metadata["requires_dual_approval"] = riskAssessment.RequiresDualApproval;

            _logger.LogDebug("Risk assessment summary: Overall={OverallRisk}, Approval={RequiresApproval}, DualApproval={RequiresDualApproval}, Factors={FactorCount}",
                riskAssessment.OverallRiskLevel, riskAssessment.RequiresApproval, riskAssessment.RequiresDualApproval, riskAssessment.RiskFactors.Count);
        }
    }
}