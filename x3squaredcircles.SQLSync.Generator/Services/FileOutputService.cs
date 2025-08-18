using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IFileOutputService
    {
        Task GenerateOutputsAsync(SqlSchemaConfiguration config, EntityDiscoveryResult entities,
            DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult,
            RiskAssessment riskAssessment, DeploymentPlan deploymentPlan, SqlScript sqlScript,
            TagTemplateResult tagResult, LicenseSession? licenseSession);
    }

    public class FileOutputService : IFileOutputService
    {
        private readonly ILogger<FileOutputService> _logger;
        private readonly string _outputDirectory = "/src";

        public FileOutputService(ILogger<FileOutputService> logger)
        {
            _logger = logger;
        }

        public async Task GenerateOutputsAsync(SqlSchemaConfiguration config, EntityDiscoveryResult entities,
            DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult,
            RiskAssessment riskAssessment, DeploymentPlan deploymentPlan, SqlScript sqlScript,
            TagTemplateResult tagResult, LicenseSession? licenseSession)
        {
            try
            {
                _logger.LogInformation("Generating output files for {Language} analysis targeting {Provider}",
                    config.Language.GetSelectedLanguage().ToUpperInvariant(),
                    config.Database.GetSelectedProvider().ToUpperInvariant());

                // Generate core output files (as specified in documentation)
                await GeneratePipelineToolsLogAsync(config, licenseSession);
                await GenerateSchemaAnalysisAsync(entities, currentSchema, targetSchema, config);
                await GenerateDeploymentPlanAsync(deploymentPlan, config);
                await GenerateValidationReportAsync(validationResult, riskAssessment, config);
                await GenerateCompiledDeploymentScriptAsync(sqlScript, config);
                await GenerateTagPatternsAsync(tagResult, config);

                // Generate analysis and diagnostic reports
                await GenerateEntityDiscoveryReportAsync(entities, config);
                await GenerateRiskAssessmentReportAsync(riskAssessment, config);
                await GenerateDeploymentSummaryAsync(deploymentPlan, riskAssessment, tagResult, config);
                await GenerateSchemaComparisonReportAsync(currentSchema, targetSchema, validationResult, config);

                _logger.LogInformation("✓ All output files generated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate output files");
                throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration,
                    $"Failed to generate output files: {ex.Message}", ex);
            }
        }

        private async Task GeneratePipelineToolsLogAsync(SqlSchemaConfiguration config, LicenseSession? licenseSession)
        {
            try
            {
                var version = GetToolVersion();
                var logEntry = $"sql-schema-generator={version}";
                var logPath = Path.Combine(_outputDirectory, "pipeline-tools.log");

                // Append to existing log or create new
                await File.AppendAllTextAsync(logPath, logEntry + Environment.NewLine);

                _logger.LogDebug("Pipeline tools log updated: {LogPath} with version {Version}", logPath, version);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update pipeline tools log");
            }
        }

        private string GetToolVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;

                if (version != null)
                {
                    // Return semantic version format (Major.Minor.Patch)
                    return $"{version.Major}.{version.Minor}.{version.Build}";
                }

                _logger.LogWarning("Could not determine assembly version, using fallback");
                return "unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract assembly version, using fallback");
                return "unknown";
            }
        }

        private async Task GenerateSchemaAnalysisAsync(EntityDiscoveryResult entities, DatabaseSchema currentSchema,
            DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            var analysis = new
            {
                discovery = new
                {
                    language = entities.Language,
                    track_attribute = entities.TrackAttribute,
                    discovery_time = entities.DiscoveryTime,
                    entities = entities.Entities.Select(e => new
                    {
                        name = e.Name,
                        full_name = e.FullName,
                        @namespace = e.Namespace,
                        table_name = e.TableName,
                        schema_name = e.SchemaName,
                        source_file = e.SourceFile,
                        source_line = e.SourceLine,
                        properties = e.Properties.Select(p => new
                        {
                            name = p.Name,
                            type = p.Type,
                            sql_type = p.SqlType,
                            is_nullable = p.IsNullable,
                            is_primary_key = p.IsPrimaryKey,
                            is_foreign_key = p.IsForeignKey,
                            is_unique = p.IsUnique,
                            is_indexed = p.IsIndexed,
                            max_length = p.MaxLength,
                            precision = p.Precision,
                            scale = p.Scale,
                            default_value = p.DefaultValue,
                            attributes = p.Attributes
                        }),
                        relationships = e.Relationships.Select(r => new
                        {
                            name = r.Name,
                            type = r.Type,
                            referenced_entity = r.ReferencedEntity,
                            referenced_table = r.ReferencedTable,
                            foreign_key_columns = r.ForeignKeyColumns,
                            referenced_columns = r.ReferencedColumns,
                            on_delete_action = r.OnDeleteAction,
                            on_update_action = r.OnUpdateAction,
                            attributes = r.Attributes
                        }),
                        indexes = e.Indexes.Select(i => new
                        {
                            name = i.Name,
                            columns = i.Columns,
                            is_unique = i.IsUnique,
                            is_clustered = i.IsClustered,
                            filter_expression = i.FilterExpression,
                            attributes = i.Attributes
                        }),
                        attributes = e.Attributes
                    }),
                    metadata = entities.Metadata
                },
                current_schema = SerializeSchema(currentSchema),
                target_schema = SerializeSchema(targetSchema),
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    language = config.Language.GetSelectedLanguage(),
                    database_provider = config.Database.GetSelectedProvider(),
                    track_attribute = config.TrackAttribute,
                    environment = config.Environment.Environment,
                    vertical = config.Environment.Vertical
                }
            };

            var json = JsonSerializer.Serialize(analysis, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "schema-analysis.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("Schema analysis written to: {FilePath}", filePath);
        }

        private async Task GenerateDeploymentPlanAsync(DeploymentPlan deploymentPlan, SqlSchemaConfiguration config)
        {
            var plan = new
            {
                phases = deploymentPlan.Phases.Select(p => new
                {
                    phase_number = p.PhaseNumber,
                    name = p.Name,
                    description = p.Description,
                    risk_level = p.RiskLevel.ToString(),
                    requires_approval = p.RequiresApproval,
                    can_rollback = p.CanRollback,
                    dependencies = p.Dependencies,
                    operations = p.Operations.Select(o => new
                    {
                        type = o.Type,
                        object_name = o.ObjectName,
                        schema = o.Schema,
                        sql_command = o.SqlCommand,
                        rollback_command = o.RollbackCommand,
                        risk_level = o.RiskLevel.ToString(),
                        dependencies = o.Dependencies,
                        properties = o.Properties
                    }),
                    properties = p.Properties
                }),
                overall_risk_level = deploymentPlan.OverallRiskLevel.ToString(),
                use_29_phase_deployment = deploymentPlan.Use29PhaseDeployment,
                created_time = deploymentPlan.CreatedTime,
                metadata = deploymentPlan.Metadata,
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    database_provider = config.Database.GetSelectedProvider(),
                    environment = config.Environment.Environment
                }
            };

            var json = JsonSerializer.Serialize(plan, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "deployment-plan.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("Deployment plan written to: {FilePath}", filePath);
        }

        private async Task GenerateValidationReportAsync(SchemaValidationResult validationResult,
            RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            var report = new
            {
                validation = new
                {
                    is_valid = validationResult.IsValid,
                    validation_time = validationResult.ValidationTime,
                    changes = validationResult.Changes.Select(c => new
                    {
                        type = c.Type,
                        object_type = c.ObjectType,
                        object_name = c.ObjectName,
                        schema = c.Schema,
                        description = c.Description,
                        risk_level = c.RiskLevel.ToString(),
                        dependencies = c.Dependencies,
                        properties = c.Properties
                    }),
                    errors = validationResult.Errors.Select(e => new
                    {
                        code = e.Code,
                        message = e.Message,
                        object_name = e.ObjectName,
                        schema = e.Schema
                    }),
                    warnings = validationResult.Warnings.Select(w => new
                    {
                        code = w.Code,
                        message = w.Message,
                        object_name = w.ObjectName,
                        schema = w.Schema,
                        risk_level = w.RiskLevel.ToString()
                    }),
                    metadata = validationResult.Metadata
                },
                risk_assessment = new
                {
                    overall_risk_level = riskAssessment.OverallRiskLevel.ToString(),
                    requires_approval = riskAssessment.RequiresApproval,
                    requires_dual_approval = riskAssessment.RequiresDualApproval,
                    safe_operations = riskAssessment.SafeOperations,
                    warning_operations = riskAssessment.WarningOperations,
                    risky_operations = riskAssessment.RiskyOperations,
                    assessment_time = riskAssessment.AssessmentTime,
                    risk_factors = riskAssessment.RiskFactors.Select(rf => new
                    {
                        name = rf.Name,
                        description = rf.Description,
                        risk_level = rf.RiskLevel.ToString(),
                        category = rf.Category,
                        affected_objects = rf.AffectedObjects,
                        properties = rf.Properties
                    }),
                    metadata = riskAssessment.Metadata
                },
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    database_provider = config.Database.GetSelectedProvider(),
                    environment = config.Environment.Environment
                }
            };

            var json = JsonSerializer.Serialize(report, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "validation-report.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("Validation report written to: {FilePath}", filePath);
        }

        private async Task GenerateCompiledDeploymentScriptAsync(SqlScript sqlScript, SqlSchemaConfiguration config)
        {
            var filePath = Path.Combine(_outputDirectory, "compiled-deployment.sql");
            await File.WriteAllTextAsync(filePath, sqlScript.Content);

            _logger.LogDebug("Compiled deployment script written to: {FilePath}", filePath);

            // Also generate statement-by-statement breakdown
            await GenerateStatementBreakdownAsync(sqlScript, config);
        }

        private async Task GenerateStatementBreakdownAsync(SqlScript sqlScript, SqlSchemaConfiguration config)
        {
            var breakdown = new
            {
                total_statements = sqlScript.Statements.Count,
                statements_by_type = sqlScript.Statements
                    .GroupBy(s => s.Type)
                    .ToDictionary(g => g.Key, g => g.Count()),
                statements_by_risk = sqlScript.Statements
                    .GroupBy(s => s.RiskLevel.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),
                statements_by_phase = sqlScript.Statements
                    .GroupBy(s => s.PhaseNumber)
                    .ToDictionary(g => g.Key, g => g.Count()),
                statements = sqlScript.Statements.Select((s, index) => new
                {
                    sequence = index + 1,
                    type = s.Type,
                    object_name = s.ObjectName,
                    schema = s.Schema,
                    risk_level = s.RiskLevel.ToString(),
                    phase_number = s.PhaseNumber,
                    sql_length = s.Sql.Length,
                    properties = s.Properties
                }),
                metadata = sqlScript.Metadata,
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    database_provider = config.Database.GetSelectedProvider()
                }
            };

            var json = JsonSerializer.Serialize(breakdown, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "sql-statement-breakdown.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("SQL statement breakdown written to: {FilePath}", filePath);
        }

        private async Task GenerateTagPatternsAsync(TagTemplateResult tagResult, SqlSchemaConfiguration config)
        {
            var tagPatterns = new
            {
                template = tagResult.Template,
                generated_tag = tagResult.GeneratedTag,
                token_values = tagResult.TokenValues,
                generation_time = tagResult.GenerationTime,
                metadata = tagResult.Metadata,
                alternative_patterns = new
                {
                    docker_tag = SanitizeForDocker(tagResult.GeneratedTag),
                    helm_chart_version = SanitizeForHelm(tagResult.GeneratedTag),
                    kubernetes_label = SanitizeForKubernetes(tagResult.GeneratedTag),
                    file_safe = SanitizeForFilename(tagResult.GeneratedTag),
                    azure_resource_name = SanitizeForAzureResource(tagResult.GeneratedTag)
                },
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    environment = config.Environment.Environment
                }
            };

            var json = JsonSerializer.Serialize(tagPatterns, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "tag-patterns.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("Tag patterns written to: {FilePath}", filePath);
        }

        private async Task GenerateEntityDiscoveryReportAsync(EntityDiscoveryResult entities, SqlSchemaConfiguration config)
        {
            var language = config.Language.GetSelectedLanguage();
            var report = new
            {
                discovery_summary = new
                {
                    language = language,
                    track_attribute = entities.TrackAttribute,
                    total_entities = entities.Entities.Count,
                    discovery_time = entities.DiscoveryTime,
                    source_files_analyzed = entities.Entities.Select(e => e.SourceFile).Distinct().Count(),
                    namespaces_found = entities.Entities.Select(e => e.Namespace).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count()
                },
                entity_breakdown = new
                {
                    by_namespace = entities.Entities
                        .GroupBy(e => e.Namespace ?? "Default")
                        .ToDictionary(g => g.Key, g => g.Count()),
                    by_source_file = entities.Entities
                        .GroupBy(e => e.SourceFile)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    property_statistics = new
                    {
                        total_properties = entities.Entities.Sum(e => e.Properties.Count),
                        avg_properties_per_entity = entities.Entities.Any() ? entities.Entities.Average(e => e.Properties.Count) : 0,
                        primary_key_properties = entities.Entities.Sum(e => e.Properties.Count(p => p.IsPrimaryKey)),
                        foreign_key_properties = entities.Entities.Sum(e => e.Properties.Count(p => p.IsForeignKey)),
                        unique_properties = entities.Entities.Sum(e => e.Properties.Count(p => p.IsUnique))
                    },
                    relationship_statistics = new
                    {
                        total_relationships = entities.Entities.Sum(e => e.Relationships.Count),
                        one_to_many = entities.Entities.Sum(e => e.Relationships.Count(r => r.Type == "OneToMany")),
                        many_to_one = entities.Entities.Sum(e => e.Relationships.Count(r => r.Type == "ManyToOne")),
                        many_to_many = entities.Entities.Sum(e => e.Relationships.Count(r => r.Type == "ManyToMany"))
                    }
                },
                language_specific_analysis = GenerateLanguageSpecificAnalysis(entities, language),
                attribute_analysis = new
                {
                    track_attribute_usage = entities.TrackAttribute,
                    entities_with_custom_table_names = entities.Entities.Count(e => !e.Name.Equals(e.TableName, StringComparison.OrdinalIgnoreCase)),
                    entities_with_custom_schemas = entities.Entities.Count(e => !string.IsNullOrEmpty(e.SchemaName)),
                    most_common_property_types = entities.Entities
                        .SelectMany(e => e.Properties)
                        .GroupBy(p => p.Type)
                        .OrderByDescending(g => g.Count())
                        .Take(10)
                        .ToDictionary(g => g.Key, g => g.Count())
                },
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    language = language,
                    track_attribute = entities.TrackAttribute
                }
            };

            var json = JsonSerializer.Serialize(report, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "entity-discovery-report.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("Entity discovery report written to: {FilePath}", filePath);
        }

        private async Task GenerateRiskAssessmentReportAsync(RiskAssessment riskAssessment, SqlSchemaConfiguration config)
        {
            var report = new
            {
                risk_summary = new
                {
                    overall_risk_level = riskAssessment.OverallRiskLevel.ToString(),
                    requires_approval = riskAssessment.RequiresApproval,
                    requires_dual_approval = riskAssessment.RequiresDualApproval,
                    assessment_time = riskAssessment.AssessmentTime
                },
                operation_counts = new
                {
                    safe_operations = riskAssessment.SafeOperations,
                    warning_operations = riskAssessment.WarningOperations,
                    risky_operations = riskAssessment.RiskyOperations,
                    total_operations = riskAssessment.SafeOperations + riskAssessment.WarningOperations + riskAssessment.RiskyOperations
                },
                risk_factors = riskAssessment.RiskFactors.Select(rf => new
                {
                    name = rf.Name,
                    description = rf.Description,
                    risk_level = rf.RiskLevel.ToString(),
                    category = rf.Category,
                    affected_object_count = rf.AffectedObjects.Count,
                    affected_objects = rf.AffectedObjects.Take(5), // Limit for readability
                    properties = rf.Properties
                }),
                risk_categories = riskAssessment.RiskFactors
                    .GroupBy(rf => rf.Category)
                    .ToDictionary(g => g.Key, g => new
                    {
                        count = g.Count(),
                        highest_risk = g.Max(rf => rf.RiskLevel).ToString(),
                        factors = g.Select(rf => rf.Name)
                    }),
                approval_requirements = new
                {
                    approval_needed = riskAssessment.RequiresApproval,
                    dual_approval_needed = riskAssessment.RequiresDualApproval,
                    approval_reason = GetApprovalReason(riskAssessment),
                    bypass_options = GetApprovalBypassOptions(config)
                },
                metadata = riskAssessment.Metadata,
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    environment = config.Environment.Environment,
                    database_provider = config.Database.GetSelectedProvider()
                }
            };

            var json = JsonSerializer.Serialize(report, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "risk-assessment-report.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("Risk assessment report written to: {FilePath}", filePath);
        }

        private async Task GenerateDeploymentSummaryAsync(DeploymentPlan deploymentPlan, RiskAssessment riskAssessment, TagTemplateResult tagResult, SqlSchemaConfiguration config)
        {
            var summary = new
            {
                deployment_overview = new
                {
                    generated_tag = tagResult.GeneratedTag,
                    environment = config.Environment.Environment,
                    vertical = config.Environment.Vertical,
                    database_provider = config.Database.GetSelectedProvider(),
                    database_name = config.Database.DatabaseName,
                    deployment_mode = config.Operation.Mode,
                    overall_risk_level = deploymentPlan.OverallRiskLevel.ToString()
                },
                phase_summary = new
                {
                    total_phases = deploymentPlan.Phases.Count,
                    phases_requiring_approval = deploymentPlan.Phases.Count(p => p.RequiresApproval),
                    phases_with_rollback = deploymentPlan.Phases.Count(p => p.CanRollback),
                    total_operations = deploymentPlan.Phases.Sum(p => p.Operations.Count),
                    use_29_phase_deployment = deploymentPlan.Use29PhaseDeployment
                },
                operations_by_type = deploymentPlan.Phases
                    .SelectMany(p => p.Operations)
                    .GroupBy(o => o.Type)
                    .ToDictionary(g => g.Key, g => g.Count()),
                operations_by_risk = deploymentPlan.Phases
                    .SelectMany(p => p.Operations)
                    .GroupBy(o => o.RiskLevel.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),
                estimated_execution_time = EstimateExecutionTime(deploymentPlan),
                rollback_capability = new
                {
                    can_auto_rollback = deploymentPlan.Phases.All(p => p.CanRollback),
                    phases_without_rollback = deploymentPlan.Phases.Where(p => !p.CanRollback).Select(p => p.Name),
                    rollback_complexity = CalculateRollbackComplexity(deploymentPlan)
                },
                approval_workflow = new
                {
                    requires_approval = riskAssessment.RequiresApproval,
                    requires_dual_approval = riskAssessment.RequiresDualApproval,
                    approval_trigger_reasons = riskAssessment.RiskFactors
                        .Where(rf => rf.Properties.ContainsKey("requires_approval") && (bool)rf.Properties["requires_approval"])
                        .Select(rf => rf.Name)
                },
                next_steps = GenerateNextSteps(config, riskAssessment, deploymentPlan),
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    created_by = "sql-schema-generator"
                }
            };

            var json = JsonSerializer.Serialize(summary, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "deployment-summary.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("Deployment summary written to: {FilePath}", filePath);
        }

        private async Task GenerateSchemaComparisonReportAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var comparison = new
            {
                schema_comparison = new
                {
                    current_schema = new
                    {
                        table_count = currentSchema.Tables.Count,
                        constraint_count = currentSchema.Constraints.Count,
                        index_count = currentSchema.Indexes.Count,
                        view_count = currentSchema.Views.Count,
                        procedure_count = currentSchema.Procedures.Count,
                        function_count = currentSchema.Functions.Count
                    },
                    target_schema = new
                    {
                        table_count = targetSchema.Tables.Count,
                        constraint_count = targetSchema.Constraints.Count,
                        index_count = targetSchema.Indexes.Count,
                        view_count = targetSchema.Views.Count,
                        procedure_count = targetSchema.Procedures.Count,
                        function_count = targetSchema.Functions.Count
                    },
                    differences = new
                    {
                        tables_added = targetSchema.Tables.Count - currentSchema.Tables.Count,
                        constraints_added = targetSchema.Constraints.Count - currentSchema.Constraints.Count,
                        indexes_added = targetSchema.Indexes.Count - currentSchema.Indexes.Count
                    }
                },
                change_summary = new
                {
                    total_changes = validationResult.Changes.Count,
                    changes_by_type = validationResult.Changes
                        .GroupBy(c => c.Type)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    changes_by_object_type = validationResult.Changes
                        .GroupBy(c => c.ObjectType)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    changes_by_risk = validationResult.Changes
                        .GroupBy(c => c.RiskLevel.ToString())
                        .ToDictionary(g => g.Key, g => g.Count())
                },
                validation_results = new
                {
                    is_valid = validationResult.IsValid,
                    error_count = validationResult.Errors.Count,
                    warning_count = validationResult.Warnings.Count,
                    validation_time = validationResult.ValidationTime
                },
                detailed_changes = validationResult.Changes.Take(50).Select(c => new // Limit for file size
                {
                    type = c.Type,
                    object_type = c.ObjectType,
                    object_name = c.ObjectName,
                    schema = c.Schema,
                    description = c.Description,
                    risk_level = c.RiskLevel.ToString()
                }),
                generation_info = new
                {
                    generated_at = DateTime.UtcNow,
                    generator_version = GetToolVersion(),
                    database_provider = config.Database.GetSelectedProvider()
                }
            };

            var json = JsonSerializer.Serialize(comparison, GetJsonOptions());
            var filePath = Path.Combine(_outputDirectory, "schema-comparison-report.json");
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("Schema comparison report written to: {FilePath}", filePath);
        }

        // Helper methods for serialization and analysis
        private object SerializeSchema(DatabaseSchema schema)
        {
            return new
            {
                database_name = schema.DatabaseName,
                provider = schema.Provider,
                analysis_time = schema.AnalysisTime,
                tables = schema.Tables.Select(t => new
                {
                    name = t.Name,
                    schema = t.Schema,
                    columns = t.Columns.Select(c => new
                    {
                        name = c.Name,
                        data_type = c.DataType,
                        is_nullable = c.IsNullable,
                        is_primary_key = c.IsPrimaryKey,
                        is_identity = c.IsIdentity,
                        max_length = c.MaxLength,
                        precision = c.Precision,
                        scale = c.Scale,
                        default_value = c.DefaultValue,
                        metadata = c.Metadata
                    }),
                    metadata = t.Metadata
                }),
                constraints = schema.Constraints.Select(c => new
                {
                    name = c.Name,
                    type = c.Type,
                    table_name = c.TableName,
                    schema = c.Schema,
                    columns = c.Columns,
                    referenced_table = c.ReferencedTable,
                    referenced_schema = c.ReferencedSchema,
                    referenced_columns = c.ReferencedColumns,
                    on_delete_action = c.OnDeleteAction,
                    on_update_action = c.OnUpdateAction,
                    check_expression = c.CheckExpression,
                    metadata = c.Metadata
                }),
                indexes = schema.Indexes.Select(i => new
                {
                    name = i.Name,
                    table_name = i.TableName,
                    schema = i.Schema,
                    columns = i.Columns,
                    is_unique = i.IsUnique,
                    is_clustered = i.IsClustered,
                    filter_expression = i.FilterExpression,
                    metadata = i.Metadata
                }),
                views = schema.Views.Select(v => new
                {
                    name = v.Name,
                    schema = v.Schema,
                    definition = v.Definition?.Length > 1000 ? v.Definition.Substring(0, 1000) + "..." : v.Definition, // Truncate for file size
                    metadata = v.Metadata
                }),
                procedures = schema.Procedures.Select(p => new
                {
                    name = p.Name,
                    schema = p.Schema,
                    parameters = p.Parameters.Select(param => new
                    {
                        name = param.Name,
                        data_type = param.DataType,
                        direction = param.Direction,
                        default_value = param.DefaultValue
                    }),
                    metadata = p.Metadata
                }),
                functions = schema.Functions.Select(f => new
                {
                    name = f.Name,
                    schema = f.Schema,
                    return_type = f.ReturnType,
                    parameters = f.Parameters.Select(param => new
                    {
                        name = param.Name,
                        data_type = param.DataType,
                        direction = param.Direction,
                        default_value = param.DefaultValue
                    }),
                    metadata = f.Metadata
                }),
                metadata = schema.Metadata
            };
        }

        private object GenerateLanguageSpecificAnalysis(EntityDiscoveryResult entities, string language)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => new
                {
                    assembly_count = entities.Entities.Select(e => e.Attributes.GetValueOrDefault("assembly", "Unknown")).Distinct().Count(),
                    namespace_count = entities.Entities.Select(e => e.Namespace).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count(),
                    attribute_usage = entities.TrackAttribute,
                    common_data_annotations = entities.Entities
                        .SelectMany(e => e.Properties)
                        .Where(p => p.Attributes.Any())
                        .SelectMany(p => p.Attributes.Keys)
                        .GroupBy(k => k)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    entity_framework_patterns = new
                    {
                        navigation_properties = entities.Entities.Sum(e => e.Relationships.Count),
                        identity_columns = entities.Entities.Sum(e => e.Properties.Count(p => p.Attributes.ContainsKey("auto_increment") && (bool)p.Attributes["auto_increment"]))
                    }
                },
                "java" => new
                {
                    jpa_annotations_found = entities.Entities.Any(e => e.Attributes.ContainsKey("entity_type")),
                    package_count = entities.Entities.Select(e => e.Namespace).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count(),
                    annotation_usage = entities.TrackAttribute,
                    hibernate_patterns = new
                    {
                        entity_relationships = entities.Entities.Sum(e => e.Relationships.Count),
                        generated_values = entities.Entities.Sum(e => e.Properties.Count(p => p.Attributes.ContainsKey("generated")))
                    }
                },
                "python" => new
                {
                    module_count = entities.Entities.Select(e => e.SourceFile).Distinct().Count(),
                    decorator_usage = entities.TrackAttribute,
                    sqlalchemy_patterns = new
                    {
                        table_definitions = entities.Entities.Count,
                        relationship_definitions = entities.Entities.Sum(e => e.Relationships.Count)
                    },
                    naming_conventions = new
                    {
                        snake_case_tables = entities.Entities.Count(e => e.TableName.Contains('_')),
                        camel_case_properties = entities.Entities.Sum(e => e.Properties.Count(p => char.IsUpper(p.Name[0])))
                    }
                },
                "javascript" => new
                {
                    file_count = entities.Entities.Select(e => e.SourceFile).Distinct().Count(),
                    decorator_usage = entities.TrackAttribute,
                    sequelize_patterns = new
                    {
                        model_definitions = entities.Entities.Count,
                        association_definitions = entities.Entities.Sum(e => e.Relationships.Count)
                    }
                },
                "typescript" => new
                {
                    file_count = entities.Entities.Select(e => e.SourceFile).Distinct().Count(),
                    decorator_usage = entities.TrackAttribute,
                    typeorm_patterns = new
                    {
                        entity_decorators = entities.Entities.Count,
                        column_decorators = entities.Entities.Sum(e => e.Properties.Count),
                        relationship_decorators = entities.Entities.Sum(e => e.Relationships.Count)
                    }
                },
                "go" => new
                {
                    package_count = entities.Entities.Select(e => e.Namespace).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count(),
                    struct_tag_usage = entities.TrackAttribute,
                    gorm_patterns = new
                    {
                        struct_definitions = entities.Entities.Count,
                        tag_annotations = entities.Entities.Sum(e => e.Properties.Count(p => p.Attributes.Any()))
                    }
                },
                _ => new { language_not_recognized = true }
            };
        }

        private string GetApprovalReason(RiskAssessment riskAssessment)
        {
            if (riskAssessment.RequiresDualApproval)
                return "Risky operations detected requiring dual approval";
            if (riskAssessment.RequiresApproval)
                return "Warning-level operations detected requiring single approval";
            return "No approval required";
        }

        private object GetApprovalBypassOptions(SqlSchemaConfiguration config)
        {
            return new
            {
                dev_environment_bypass = config.Environment.Environment.ToLowerInvariant() == "dev",
                validate_only_mode = config.Operation.ValidateOnly,
                noop_mode = config.Operation.NoOp,
                environment_variable = "BYPASS_APPROVAL"
            };
        }

        private string EstimateExecutionTime(DeploymentPlan deploymentPlan)
        {
            var totalOperations = deploymentPlan.Phases.Sum(p => p.Operations.Count);
            var estimatedMinutes = Math.Max(1, totalOperations / 10); // Rough estimate: 10 operations per minute

            if (estimatedMinutes < 60)
                return $"{estimatedMinutes} minutes";

            var hours = estimatedMinutes / 60;
            var minutes = estimatedMinutes % 60;
            return $"{hours}h {minutes}m";
        }

        private string CalculateRollbackComplexity(DeploymentPlan deploymentPlan)
        {
            var nonRollbackPhases = deploymentPlan.Phases.Count(p => !p.CanRollback);
            var totalPhases = deploymentPlan.Phases.Count;

            if (nonRollbackPhases == 0)
                return "Low - All phases can be automatically rolled back";
            if (nonRollbackPhases < totalPhases / 3)
                return "Medium - Some manual intervention required";
            return "High - Significant manual intervention required";
        }

        private List<string> GenerateNextSteps(SqlSchemaConfiguration config, RiskAssessment riskAssessment, DeploymentPlan deploymentPlan)
        {
            var steps = new List<string>();

            if (config.Operation.ValidateOnly)
            {
                steps.Add("Validation complete - review reports before proceeding to execution");
            }
            else if (config.Operation.NoOp)
            {
                steps.Add("Analysis complete - change MODE to 'execute' for actual deployment");
            }

            if (riskAssessment.RequiresDualApproval)
            {
                steps.Add("Obtain dual approval before deployment due to risky operations");
            }
            else if (riskAssessment.RequiresApproval)
            {
                steps.Add("Obtain single approval before deployment due to warning-level operations");
            }

            if (!config.Operation.SkipBackup && config.Environment.Environment.ToLowerInvariant() != "dev")
            {
                steps.Add("Ensure database backup is created before deployment");
            }

            if (deploymentPlan.Phases.Any(p => !p.CanRollback))
            {
                steps.Add("Review rollback procedures for phases that cannot be automatically rolled back");
            }

            steps.Add("Execute deployment using the generated SQL script");
            steps.Add("Monitor deployment progress and validate successful completion");

            return steps;
        }

        private JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        // Utility methods for alternative tag formats
        private string SanitizeForDocker(string tag)
        {
            // Docker tag rules: lowercase, no spaces, limited special chars
            return tag.ToLowerInvariant()
                     .Replace(' ', '-')
                     .Replace('_', '-')
                     .Replace('/', '-')
                     .Replace('\\', '-')
                     .TrimStart('-')
                     .TrimEnd('-');
        }

        private string SanitizeForHelm(string tag)
        {
            // Helm chart version must follow SemVer
            var sanitized = tag.Replace('/', '-')
                              .Replace('_', '-')
                              .Replace(' ', '-');

            // Try to extract version-like pattern
            var match = System.Text.RegularExpressions.Regex.Match(sanitized, @"(\d+\.\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : "1.0.0";
        }

        private string SanitizeForKubernetes(string tag)
        {
            // Kubernetes label value rules
            return tag.ToLowerInvariant()
                     .Replace(' ', '-')
                     .Replace('_', '-')
                     .Replace('/', '-')
                     .Replace('\\', '-')
                     .TrimStart('-', '.')
                     .TrimEnd('-', '.');
        }

        private string SanitizeForFilename(string tag)
        {
            // File-safe characters only
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = tag;

            foreach (var c in invalid)
            {
                sanitized = sanitized.Replace(c, '-');
            }

            return sanitized.Replace(' ', '-')
                           .Replace("--", "-")
                           .Trim('-');
        }

        private string SanitizeForAzureResource(string tag)
        {
            // Azure resource naming rules: alphanumeric and hyphens
            return System.Text.RegularExpressions.Regex.Replace(tag, @"[^a-zA-Z0-9\-]", "-")
                                                       .Replace("--", "-")
                                                       .TrimStart('-')
                                                       .TrimEnd('-')
                                                       .ToLowerInvariant();
        }
    }
}