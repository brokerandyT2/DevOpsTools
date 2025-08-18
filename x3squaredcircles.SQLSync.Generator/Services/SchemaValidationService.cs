using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ISchemaValidationService
    {
        Task<SchemaValidationResult> ValidateSchemaChangesAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SqlSchemaConfiguration config);
    }

    public class SchemaValidationService : ISchemaValidationService
    {
        private readonly ILogger<SchemaValidationService> _logger;

        public SchemaValidationService(ILogger<SchemaValidationService> logger)
        {
            _logger = logger;
        }

        public async Task<SchemaValidationResult> ValidateSchemaChangesAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Validating schema changes between current and target schemas");

                var validationResult = new SchemaValidationResult
                {
                    Changes = new List<SchemaChange>(),
                    Errors = new List<ValidationError>(),
                    Warnings = new List<ValidationWarning>(),
                    IsValid = true,
                    ValidationTime = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object>
                    {
                        ["current_tables"] = currentSchema.Tables.Count,
                        ["target_tables"] = targetSchema.Tables.Count,
                        ["validation_level"] = config.SchemaAnalysis.ValidationLevel,
                        ["provider"] = config.Database.GetSelectedProvider()
                    }
                };

                await ValidateTablesAsync(currentSchema, targetSchema, validationResult, config);
                await ValidateConstraintsAsync(currentSchema, targetSchema, validationResult, config);
                await ValidateIndexesAsync(currentSchema, targetSchema, validationResult, config);
                await ValidateViewsAsync(currentSchema, targetSchema, validationResult, config);
                await ValidateProceduresAsync(currentSchema, targetSchema, validationResult, config);
                await ValidateFunctionsAsync(currentSchema, targetSchema, validationResult, config);

                await ValidateDependenciesAsync(validationResult, config);
                await ValidateNamingConventionsAsync(targetSchema, validationResult, config);
                await ValidateCrossSchemaReferencesAsync(validationResult, config);

                if (validationResult.Errors.Any())
                {
                    validationResult.IsValid = false;
                }

                _logger.LogInformation("✓ Schema validation complete: {ChangeCount} changes, {ErrorCount} errors, {WarningCount} warnings",
                    validationResult.Changes.Count, validationResult.Errors.Count, validationResult.Warnings.Count);

                return validationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schema validation failed");
                throw new SqlSchemaException(SqlSchemaExitCode.SchemaValidationFailure,
                    $"Schema validation failed: {ex.Message}", ex);
            }
        }

        private async Task ValidateTablesAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var currentTables = currentSchema.Tables.ToDictionary(t => $"{t.Schema}.{t.Name}", StringComparer.OrdinalIgnoreCase);
            var targetTables = targetSchema.Tables.ToDictionary(t => $"{t.Schema}.{t.Name}", StringComparer.OrdinalIgnoreCase);

            foreach (var targetTable in targetTables)
            {
                if (!currentTables.ContainsKey(targetTable.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "CREATE",
                        ObjectType = "TABLE",
                        ObjectName = targetTable.Value.Name,
                        Schema = targetTable.Value.Schema,
                        Description = $"Create table {targetTable.Key}",
                        RiskLevel = RiskLevel.Safe,
                        Properties = new Dictionary<string, object>
                        {
                            ["column_count"] = targetTable.Value.Columns.Count,
                            ["has_primary_key"] = targetTable.Value.Columns.Any(c => c.IsPrimaryKey)
                        }
                    };
                    validationResult.Changes.Add(change);
                }
                else
                {
                    var currentTable = currentTables[targetTable.Key];
                    await ValidateTableColumnsAsync(currentTable, targetTable.Value, validationResult, config);
                }
            }

            foreach (var currentTable in currentTables)
            {
                if (!targetTables.ContainsKey(currentTable.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "DROP",
                        ObjectType = "TABLE",
                        ObjectName = currentTable.Value.Name,
                        Schema = currentTable.Value.Schema,
                        Description = $"Drop table {currentTable.Key}",
                        RiskLevel = RiskLevel.Risky,
                        Properties = new Dictionary<string, object>
                        {
                            ["contains_data"] = true,
                            ["has_dependencies"] = await HasTableDependenciesAsync(currentTable.Value, currentSchema)
                        }
                    };
                    validationResult.Changes.Add(change);

                    var warning = new ValidationWarning
                    {
                        Code = "TABLE_DROP",
                        Message = $"Table {currentTable.Key} will be dropped. This is a destructive operation that will result in data loss.",
                        ObjectName = currentTable.Value.Name,
                        Schema = currentTable.Value.Schema,
                        RiskLevel = RiskLevel.Risky
                    };
                    validationResult.Warnings.Add(warning);
                }
            }
        }

        private async Task ValidateTableColumnsAsync(SchemaTable currentTable, SchemaTable targetTable, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var currentColumns = currentTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var targetColumns = targetTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var targetColumn in targetColumns)
            {
                if (!currentColumns.ContainsKey(targetColumn.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "ALTER",
                        ObjectType = "COLUMN",
                        ObjectName = $"{targetTable.Name}.{targetColumn.Value.Name}",
                        Schema = targetTable.Schema,
                        Description = $"Add column {targetColumn.Value.Name} to table {targetTable.Name}",
                        RiskLevel = targetColumn.Value.IsNullable ? RiskLevel.Safe : RiskLevel.Warning,
                        Properties = new Dictionary<string, object>
                        {
                            ["data_type"] = targetColumn.Value.DataType,
                            ["is_nullable"] = targetColumn.Value.IsNullable,
                            ["has_default"] = !string.IsNullOrEmpty(targetColumn.Value.DefaultValue)
                        }
                    };
                    validationResult.Changes.Add(change);

                    if (!targetColumn.Value.IsNullable && string.IsNullOrEmpty(targetColumn.Value.DefaultValue))
                    {
                        var warning = new ValidationWarning
                        {
                            Code = "NON_NULLABLE_COLUMN_WITHOUT_DEFAULT",
                            Message = $"Adding non-nullable column {targetColumn.Value.Name} without default value to table {targetTable.Name} may fail if table contains data",
                            ObjectName = targetColumn.Value.Name,
                            Schema = targetTable.Schema,
                            RiskLevel = RiskLevel.Warning
                        };
                        validationResult.Warnings.Add(warning);
                    }
                }
                else
                {
                    var currentColumn = currentColumns[targetColumn.Key];
                    await ValidateColumnChangesAsync(currentColumn, targetColumn.Value, targetTable, validationResult, config);
                }
            }

            foreach (var currentColumn in currentColumns)
            {
                if (!targetColumns.ContainsKey(currentColumn.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "ALTER",
                        ObjectType = "COLUMN",
                        ObjectName = $"{currentTable.Name}.{currentColumn.Value.Name}",
                        Schema = currentTable.Schema,
                        Description = $"Drop column {currentColumn.Value.Name} from table {currentTable.Name}",
                        RiskLevel = RiskLevel.Risky,
                        Properties = new Dictionary<string, object>
                        {
                            ["data_type"] = currentColumn.Value.DataType,
                            ["is_primary_key"] = currentColumn.Value.IsPrimaryKey,
                            ["contains_data"] = true
                        }
                    };
                    validationResult.Changes.Add(change);

                    var warning = new ValidationWarning
                    {
                        Code = "COLUMN_DROP",
                        Message = $"Column {currentColumn.Value.Name} will be dropped from table {currentTable.Name}. This will result in data loss.",
                        ObjectName = currentColumn.Value.Name,
                        Schema = currentTable.Schema,
                        RiskLevel = RiskLevel.Risky
                    };
                    validationResult.Warnings.Add(warning);

                    if (currentColumn.Value.IsPrimaryKey)
                    {
                        var error = new ValidationError
                        {
                            Code = "PRIMARY_KEY_COLUMN_DROP",
                            Message = $"Cannot drop primary key column {currentColumn.Value.Name} from table {currentTable.Name}",
                            ObjectName = currentColumn.Value.Name,
                            Schema = currentTable.Schema
                        };
                        validationResult.Errors.Add(error);
                    }
                }
            }
        }

        private async Task ValidateColumnChangesAsync(SchemaColumn currentColumn, SchemaColumn targetColumn, SchemaTable table, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            if (!currentColumn.DataType.Equals(targetColumn.DataType, StringComparison.OrdinalIgnoreCase))
            {
                var riskLevel = await AssessDataTypeChangeRiskAsync(currentColumn.DataType, targetColumn.DataType, config);

                var change = new SchemaChange
                {
                    Type = "ALTER",
                    ObjectType = "COLUMN",
                    ObjectName = $"{table.Name}.{currentColumn.Name}",
                    Schema = table.Schema,
                    Description = $"Change column {currentColumn.Name} data type from {currentColumn.DataType} to {targetColumn.DataType}",
                    RiskLevel = riskLevel,
                    Properties = new Dictionary<string, object>
                    {
                        ["old_data_type"] = currentColumn.DataType,
                        ["new_data_type"] = targetColumn.DataType,
                        ["potential_data_loss"] = await MayDataTypeCauseLossAsync(currentColumn.DataType, targetColumn.DataType, config)
                    }
                };
                validationResult.Changes.Add(change);

                if (riskLevel == RiskLevel.Risky)
                {
                    var warning = new ValidationWarning
                    {
                        Code = "RISKY_DATA_TYPE_CHANGE",
                        Message = $"Changing data type of column {currentColumn.Name} from {currentColumn.DataType} to {targetColumn.DataType} may cause data loss or conversion errors",
                        ObjectName = currentColumn.Name,
                        Schema = table.Schema,
                        RiskLevel = RiskLevel.Risky
                    };
                    validationResult.Warnings.Add(warning);
                }
            }

            if (currentColumn.IsNullable && !targetColumn.IsNullable)
            {
                var change = new SchemaChange
                {
                    Type = "ALTER",
                    ObjectType = "COLUMN",
                    ObjectName = $"{table.Name}.{currentColumn.Name}",
                    Schema = table.Schema,
                    Description = $"Change column {currentColumn.Name} from nullable to not nullable",
                    RiskLevel = RiskLevel.Warning,
                    Properties = new Dictionary<string, object>
                    {
                        ["nullable_change"] = "nullable_to_not_nullable",
                        ["has_default"] = !string.IsNullOrEmpty(targetColumn.DefaultValue)
                    }
                };
                validationResult.Changes.Add(change);

                if (string.IsNullOrEmpty(targetColumn.DefaultValue))
                {
                    var warning = new ValidationWarning
                    {
                        Code = "NULLABLE_TO_NOT_NULL_WITHOUT_DEFAULT",
                        Message = $"Changing column {currentColumn.Name} from nullable to not nullable without providing a default value may fail if NULL values exist",
                        ObjectName = currentColumn.Name,
                        Schema = table.Schema,
                        RiskLevel = RiskLevel.Warning
                    };
                    validationResult.Warnings.Add(warning);
                }
            }

            if (currentColumn.MaxLength.HasValue && targetColumn.MaxLength.HasValue &&
                currentColumn.MaxLength > targetColumn.MaxLength)
            {
                var change = new SchemaChange
                {
                    Type = "ALTER",
                    ObjectType = "COLUMN",
                    ObjectName = $"{table.Name}.{currentColumn.Name}",
                    Schema = table.Schema,
                    Description = $"Reduce column {currentColumn.Name} max length from {currentColumn.MaxLength} to {targetColumn.MaxLength}",
                    RiskLevel = RiskLevel.Risky,
                    Properties = new Dictionary<string, object>
                    {
                        ["old_max_length"] = currentColumn.MaxLength,
                        ["new_max_length"] = targetColumn.MaxLength,
                        ["potential_truncation"] = true
                    }
                };
                validationResult.Changes.Add(change);

                var warning = new ValidationWarning
                {
                    Code = "COLUMN_LENGTH_REDUCTION",
                    Message = $"Reducing max length of column {currentColumn.Name} from {currentColumn.MaxLength} to {targetColumn.MaxLength} may cause data truncation",
                    ObjectName = currentColumn.Name,
                    Schema = table.Schema,
                    RiskLevel = RiskLevel.Risky
                };
                validationResult.Warnings.Add(warning);
            }
        }

        private async Task ValidateConstraintsAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var currentConstraints = currentSchema.Constraints.ToDictionary(c => $"{c.Schema}.{c.Name}", StringComparer.OrdinalIgnoreCase);
            var targetConstraints = targetSchema.Constraints.ToDictionary(c => $"{c.Schema}.{c.Name}", StringComparer.OrdinalIgnoreCase);

            foreach (var targetConstraint in targetConstraints)
            {
                if (!currentConstraints.ContainsKey(targetConstraint.Key))
                {
                    var riskLevel = targetConstraint.Value.Type switch
                    {
                        "PK" => RiskLevel.Safe,
                        "UQ" => RiskLevel.Warning,
                        "FK" => RiskLevel.Warning,
                        "CK" => RiskLevel.Warning,
                        _ => RiskLevel.Safe
                    };

                    var change = new SchemaChange
                    {
                        Type = "CREATE",
                        ObjectType = "CONSTRAINT",
                        ObjectName = targetConstraint.Value.Name,
                        Schema = targetConstraint.Value.Schema,
                        Description = $"Create {GetConstraintTypeDescription(targetConstraint.Value.Type)} constraint {targetConstraint.Value.Name}",
                        RiskLevel = riskLevel,
                        Properties = new Dictionary<string, object>
                        {
                            ["constraint_type"] = targetConstraint.Value.Type,
                            ["table_name"] = targetConstraint.Value.TableName,
                            ["columns"] = targetConstraint.Value.Columns
                        }
                    };
                    validationResult.Changes.Add(change);

                    if (targetConstraint.Value.Type == "FK" && !config.SchemaAnalysis.EnableCrossSchemaRefs)
                    {
                        await ValidateForeignKeyReferenceAsync(targetConstraint.Value, targetSchema, validationResult);
                    }
                }
            }

            foreach (var currentConstraint in currentConstraints)
            {
                if (!targetConstraints.ContainsKey(currentConstraint.Key))
                {
                    var riskLevel = currentConstraint.Value.Type switch
                    {
                        "PK" => RiskLevel.Risky,
                        "FK" => RiskLevel.Safe,
                        "UQ" => RiskLevel.Warning,
                        "CK" => RiskLevel.Safe,
                        _ => RiskLevel.Safe
                    };

                    var change = new SchemaChange
                    {
                        Type = "DROP",
                        ObjectType = "CONSTRAINT",
                        ObjectName = currentConstraint.Value.Name,
                        Schema = currentConstraint.Value.Schema,
                        Description = $"Drop {GetConstraintTypeDescription(currentConstraint.Value.Type)} constraint {currentConstraint.Value.Name}",
                        RiskLevel = riskLevel,
                        Properties = new Dictionary<string, object>
                        {
                            ["constraint_type"] = currentConstraint.Value.Type,
                            ["table_name"] = currentConstraint.Value.TableName
                        }
                    };
                    validationResult.Changes.Add(change);

                    if (currentConstraint.Value.Type == "PK")
                    {
                        var warning = new ValidationWarning
                        {
                            Code = "PRIMARY_KEY_DROP",
                            Message = $"Dropping primary key constraint {currentConstraint.Value.Name} may affect replication and references",
                            ObjectName = currentConstraint.Value.Name,
                            Schema = currentConstraint.Value.Schema,
                            RiskLevel = RiskLevel.Risky
                        };
                        validationResult.Warnings.Add(warning);
                    }
                }
            }
        }

        private async Task ValidateIndexesAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var currentIndexes = currentSchema.Indexes.ToDictionary(i => $"{i.Schema}.{i.Name}", StringComparer.OrdinalIgnoreCase);
            var targetIndexes = targetSchema.Indexes.ToDictionary(i => $"{i.Schema}.{i.Name}", StringComparer.OrdinalIgnoreCase);

            foreach (var targetIndex in targetIndexes)
            {
                if (!currentIndexes.ContainsKey(targetIndex.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "CREATE",
                        ObjectType = "INDEX",
                        ObjectName = targetIndex.Value.Name,
                        Schema = targetIndex.Value.Schema,
                        Description = $"Create {(targetIndex.Value.IsUnique ? "unique " : "")}{(targetIndex.Value.IsClustered ? "clustered " : "")}index {targetIndex.Value.Name}",
                        RiskLevel = targetIndex.Value.IsClustered ? RiskLevel.Warning : RiskLevel.Safe,
                        Properties = new Dictionary<string, object>
                        {
                            ["table_name"] = targetIndex.Value.TableName,
                            ["columns"] = targetIndex.Value.Columns,
                            ["is_unique"] = targetIndex.Value.IsUnique,
                            ["is_clustered"] = targetIndex.Value.IsClustered
                        }
                    };
                    validationResult.Changes.Add(change);

                    if (targetIndex.Value.IsClustered)
                    {
                        var warning = new ValidationWarning
                        {
                            Code = "CLUSTERED_INDEX_CREATE",
                            Message = $"Creating clustered index {targetIndex.Value.Name} will reorganize table data and may take significant time",
                            ObjectName = targetIndex.Value.Name,
                            Schema = targetIndex.Value.Schema,
                            RiskLevel = RiskLevel.Warning
                        };
                        validationResult.Warnings.Add(warning);
                    }
                }
            }

            foreach (var currentIndex in currentIndexes)
            {
                if (!targetIndexes.ContainsKey(currentIndex.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "DROP",
                        ObjectType = "INDEX",
                        ObjectName = currentIndex.Value.Name,
                        Schema = currentIndex.Value.Schema,
                        Description = $"Drop index {currentIndex.Value.Name}",
                        RiskLevel = currentIndex.Value.IsClustered ? RiskLevel.Warning : RiskLevel.Safe,
                        Properties = new Dictionary<string, object>
                        {
                            ["table_name"] = currentIndex.Value.TableName,
                            ["is_unique"] = currentIndex.Value.IsUnique,
                            ["is_clustered"] = currentIndex.Value.IsClustered
                        }
                    };
                    validationResult.Changes.Add(change);
                }
            }
        }

        private async Task ValidateViewsAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var currentViews = currentSchema.Views.ToDictionary(v => $"{v.Schema}.{v.Name}", StringComparer.OrdinalIgnoreCase);
            var targetViews = targetSchema.Views.ToDictionary(v => $"{v.Schema}.{v.Name}", StringComparer.OrdinalIgnoreCase);

            foreach (var targetView in targetViews)
            {
                if (!currentViews.ContainsKey(targetView.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "CREATE",
                        ObjectType = "VIEW",
                        ObjectName = targetView.Value.Name,
                        Schema = targetView.Value.Schema,
                        Description = $"Create view {targetView.Key}",
                        RiskLevel = RiskLevel.Safe,
                        Properties = new Dictionary<string, object>
                        {
                            ["has_definition"] = !string.IsNullOrEmpty(targetView.Value.Definition)
                        }
                    };
                    validationResult.Changes.Add(change);
                }
                else
                {
                    var currentView = currentViews[targetView.Key];
                    if (!string.Equals(currentView.Definition?.Trim(), targetView.Value.Definition?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        var change = new SchemaChange
                        {
                            Type = "ALTER",
                            ObjectType = "VIEW",
                            ObjectName = targetView.Value.Name,
                            Schema = targetView.Value.Schema,
                            Description = $"Alter view {targetView.Key}",
                            RiskLevel = RiskLevel.Safe,
                            Properties = new Dictionary<string, object>
                            {
                                ["definition_changed"] = true
                            }
                        };
                        validationResult.Changes.Add(change);
                    }
                }
            }

            foreach (var currentView in currentViews)
            {
                if (!targetViews.ContainsKey(currentView.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "DROP",
                        ObjectType = "VIEW",
                        ObjectName = currentView.Value.Name,
                        Schema = currentView.Value.Schema,
                        Description = $"Drop view {currentView.Key}",
                        RiskLevel = RiskLevel.Safe,
                        Properties = new Dictionary<string, object>()
                    };
                    validationResult.Changes.Add(change);
                }
            }
        }

        private async Task ValidateProceduresAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var currentProcedures = currentSchema.Procedures.ToDictionary(p => $"{p.Schema}.{p.Name}", StringComparer.OrdinalIgnoreCase);
            var targetProcedures = targetSchema.Procedures.ToDictionary(p => $"{p.Schema}.{p.Name}", StringComparer.OrdinalIgnoreCase);

            foreach (var targetProcedure in targetProcedures)
            {
                if (!currentProcedures.ContainsKey(targetProcedure.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "CREATE",
                        ObjectType = "PROCEDURE",
                        ObjectName = targetProcedure.Value.Name,
                        Schema = targetProcedure.Value.Schema,
                        Description = $"Create procedure {targetProcedure.Key}",
                        RiskLevel = RiskLevel.Safe,
                        Properties = new Dictionary<string, object>
                        {
                            ["parameter_count"] = targetProcedure.Value.Parameters.Count
                        }
                    };
                    validationResult.Changes.Add(change);
                }
            }

            foreach (var currentProcedure in currentProcedures)
            {
                if (!targetProcedures.ContainsKey(currentProcedure.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "DROP",
                        ObjectType = "PROCEDURE",
                        ObjectName = currentProcedure.Value.Name,
                        Schema = currentProcedure.Value.Schema,
                        Description = $"Drop procedure {currentProcedure.Key}",
                        RiskLevel = RiskLevel.Warning,
                        Properties = new Dictionary<string, object>()
                    };
                    validationResult.Changes.Add(change);

                    var warning = new ValidationWarning
                    {
                        Code = "PROCEDURE_DROP",
                        Message = $"Dropping procedure {currentProcedure.Key} may break dependent applications",
                        ObjectName = currentProcedure.Value.Name,
                        Schema = currentProcedure.Value.Schema,
                        RiskLevel = RiskLevel.Warning
                    };
                    validationResult.Warnings.Add(warning);
                }
            }
        }

        private async Task ValidateFunctionsAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var currentFunctions = currentSchema.Functions.ToDictionary(f => $"{f.Schema}.{f.Name}", StringComparer.OrdinalIgnoreCase);
            var targetFunctions = targetSchema.Functions.ToDictionary(f => $"{f.Schema}.{f.Name}", StringComparer.OrdinalIgnoreCase);

            foreach (var targetFunction in targetFunctions)
            {
                if (!currentFunctions.ContainsKey(targetFunction.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "CREATE",
                        ObjectType = "FUNCTION",
                        ObjectName = targetFunction.Value.Name,
                        Schema = targetFunction.Value.Schema,
                        Description = $"Create function {targetFunction.Key}",
                        RiskLevel = RiskLevel.Safe,
                        Properties = new Dictionary<string, object>
                        {
                            ["return_type"] = targetFunction.Value.ReturnType,
                            ["parameter_count"] = targetFunction.Value.Parameters.Count
                        }
                    };
                    validationResult.Changes.Add(change);
                }
            }

            foreach (var currentFunction in currentFunctions)
            {
                if (!targetFunctions.ContainsKey(currentFunction.Key))
                {
                    var change = new SchemaChange
                    {
                        Type = "DROP",
                        ObjectType = "FUNCTION",
                        ObjectName = currentFunction.Value.Name,
                        Schema = currentFunction.Value.Schema,
                        Description = $"Drop function {currentFunction.Key}",
                        RiskLevel = RiskLevel.Warning,
                        Properties = new Dictionary<string, object>
                        {
                            ["return_type"] = currentFunction.Value.ReturnType
                        }
                    };
                    validationResult.Changes.Add(change);

                    var warning = new ValidationWarning
                    {
                        Code = "FUNCTION_DROP",
                        Message = $"Dropping function {currentFunction.Key} may break dependent views, procedures, or applications",
                        ObjectName = currentFunction.Value.Name,
                        Schema = currentFunction.Value.Schema,
                        RiskLevel = RiskLevel.Warning
                    };
                    validationResult.Warnings.Add(warning);
                }
            }
        }

        private async Task ValidateDependenciesAsync(SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var dependencyGraph = new Dictionary<string, List<string>>();

            foreach (var change in validationResult.Changes)
            {
                if (change.Dependencies.Any())
                {
                    var objectKey = $"{change.Schema}.{change.ObjectName}";
                    foreach (var dependency in change.Dependencies)
                    {
                        if (!dependencyGraph.ContainsKey(dependency))
                        {
                            dependencyGraph[dependency] = new List<string>();
                        }
                        dependencyGraph[dependency].Add(objectKey);
                    }
                }
            }

            var circularDependencies = await DetectCircularDependenciesAsync(dependencyGraph);
            foreach (var cycle in circularDependencies)
            {
                var error = new ValidationError
                {
                    Code = "CIRCULAR_DEPENDENCY",
                    Message = $"Circular dependency detected: {string.Join(" -> ", cycle)}",
                    ObjectName = cycle.FirstOrDefault() ?? "Unknown"
                };
                validationResult.Errors.Add(error);
            }
        }

        private async Task ValidateNamingConventionsAsync(DatabaseSchema targetSchema, SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            var provider = config.Database.GetSelectedProvider();
            var reservedWords = GetProviderReservedWords(provider);

            foreach (var table in targetSchema.Tables)
            {
                if (reservedWords.Contains(table.Name.ToUpperInvariant()))
                {
                    var warning = new ValidationWarning
                    {
                        Code = "RESERVED_WORD_TABLE_NAME",
                        Message = $"Table name '{table.Name}' is a reserved word in {provider.ToUpperInvariant()}",
                        ObjectName = table.Name,
                        Schema = table.Schema,
                        RiskLevel = RiskLevel.Warning
                    };
                    validationResult.Warnings.Add(warning);
                }

                foreach (var column in table.Columns)
                {
                    if (reservedWords.Contains(column.Name.ToUpperInvariant()))
                    {
                        var warning = new ValidationWarning
                        {
                            Code = "RESERVED_WORD_COLUMN_NAME",
                            Message = $"Column name '{column.Name}' in table '{table.Name}' is a reserved word in {provider.ToUpperInvariant()}",
                            ObjectName = column.Name,
                            Schema = table.Schema,
                            RiskLevel = RiskLevel.Warning
                        };
                        validationResult.Warnings.Add(warning);
                    }
                }
            }
        }

        private async Task ValidateCrossSchemaReferencesAsync(SchemaValidationResult validationResult, SqlSchemaConfiguration config)
        {
            if (config.SchemaAnalysis.EnableCrossSchemaRefs)
            {
                return;
            }

            var foreignKeyChanges = validationResult.Changes
                .Where(c => c.ObjectType == "CONSTRAINT" && c.Properties.ContainsKey("constraint_type") &&
                           c.Properties["constraint_type"].ToString() == "FK")
                .ToList();

            foreach (var fkChange in foreignKeyChanges)
            {
                if (fkChange.Properties.ContainsKey("referenced_table"))
                {
                    var referencedTable = fkChange.Properties["referenced_table"].ToString();
                    var warning = new ValidationWarning
                    {
                        Code = "CROSS_SCHEMA_REFERENCE_DISABLED",
                        Message = $"Foreign key constraint {fkChange.ObjectName} references table {referencedTable} but cross-schema references are disabled",
                        ObjectName = fkChange.ObjectName,
                        Schema = fkChange.Schema,
                        RiskLevel = RiskLevel.Warning
                    };
                    validationResult.Warnings.Add(warning);
                }
            }
        }

        private async Task<bool> HasTableDependenciesAsync(SchemaTable table, DatabaseSchema schema)
        {
            var tableName = table.Name;
            var tableSchema = table.Schema;

            var hasForeignKeyReferences = schema.Constraints.Any(c =>
                c.Type == "FK" &&
                c.ReferencedTable != null &&
                c.ReferencedTable.Equals(tableName, StringComparison.OrdinalIgnoreCase) &&
                (c.ReferencedSchema == null || c.ReferencedSchema.Equals(tableSchema, StringComparison.OrdinalIgnoreCase)));

            var hasViewDependencies = schema.Views.Any(v =>
                v.Definition != null &&
                v.Definition.Contains(tableName, StringComparison.OrdinalIgnoreCase));

            return hasForeignKeyReferences || hasViewDependencies;
        }

        private async Task<RiskLevel> AssessDataTypeChangeRiskAsync(string oldType, string newType, SqlSchemaConfiguration config)
        {
            var provider = config.Database.GetSelectedProvider();

            var oldTypeNormalized = NormalizeDataType(oldType, provider);
            var newTypeNormalized = NormalizeDataType(newType, provider);

            if (oldTypeNormalized.Category == newTypeNormalized.Category)
            {
                if (oldTypeNormalized.Size <= newTypeNormalized.Size)
                {
                    return RiskLevel.Safe;
                }
                else
                {
                    return RiskLevel.Risky;
                }
            }

            var compatibilityMatrix = GetDataTypeCompatibilityMatrix(provider);
            if (compatibilityMatrix.TryGetValue((oldTypeNormalized.Category, newTypeNormalized.Category), out var riskLevel))
            {
                return riskLevel;
            }

            return RiskLevel.Risky;
        }

        private async Task<bool> MayDataTypeCauseLossAsync(string oldType, string newType, SqlSchemaConfiguration config)
        {
            var provider = config.Database.GetSelectedProvider();
            var oldTypeNormalized = NormalizeDataType(oldType, provider);
            var newTypeNormalized = NormalizeDataType(newType, provider);

            if (oldTypeNormalized.Category != newTypeNormalized.Category)
            {
                return true;
            }

            if (oldTypeNormalized.Size > newTypeNormalized.Size)
            {
                return true;
            }

            if (oldTypeNormalized.Precision > newTypeNormalized.Precision ||
                oldTypeNormalized.Scale > newTypeNormalized.Scale)
            {
                return true;
            }

            return false;
        }

        private async Task ValidateForeignKeyReferenceAsync(SchemaConstraint foreignKey, DatabaseSchema targetSchema, SchemaValidationResult validationResult)
        {
            if (string.IsNullOrEmpty(foreignKey.ReferencedTable))
            {
                return;
            }

            var referencedTable = targetSchema.Tables.FirstOrDefault(t =>
                t.Name.Equals(foreignKey.ReferencedTable, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(foreignKey.ReferencedSchema) ||
                 t.Schema.Equals(foreignKey.ReferencedSchema, StringComparison.OrdinalIgnoreCase)));

            if (referencedTable == null)
            {
                var error = new ValidationError
                {
                    Code = "MISSING_REFERENCED_TABLE",
                    Message = $"Foreign key constraint {foreignKey.Name} references table {foreignKey.ReferencedTable} which does not exist in target schema",
                    ObjectName = foreignKey.Name,
                    Schema = foreignKey.Schema
                };
                validationResult.Errors.Add(error);
                return;
            }

            foreach (var referencedColumn in foreignKey.ReferencedColumns)
            {
                if (!referencedTable.Columns.Any(c => c.Name.Equals(referencedColumn, StringComparison.OrdinalIgnoreCase)))
                {
                    var error = new ValidationError
                    {
                        Code = "MISSING_REFERENCED_COLUMN",
                        Message = $"Foreign key constraint {foreignKey.Name} references column {referencedColumn} which does not exist in table {foreignKey.ReferencedTable}",
                        ObjectName = foreignKey.Name,
                        Schema = foreignKey.Schema
                    };
                    validationResult.Errors.Add(error);
                }
            }
        }

        private async Task<List<List<string>>> DetectCircularDependenciesAsync(Dictionary<string, List<string>> dependencyGraph)
        {
            var circularDependencies = new List<List<string>>();
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            foreach (var node in dependencyGraph.Keys)
            {
                if (!visited.Contains(node))
                {
                    var cycle = await DetectCycleAsync(node, dependencyGraph, visited, recursionStack, new List<string>());
                    if (cycle != null)
                    {
                        circularDependencies.Add(cycle);
                    }
                }
            }

            return circularDependencies;
        }

        private async Task<List<string>?> DetectCycleAsync(string node, Dictionary<string, List<string>> graph,
            HashSet<string> visited, HashSet<string> recursionStack, List<string> path)
        {
            visited.Add(node);
            recursionStack.Add(node);
            path.Add(node);

            if (graph.ContainsKey(node))
            {
                foreach (var neighbor in graph[node])
                {
                    if (!visited.Contains(neighbor))
                    {
                        var cycle = await DetectCycleAsync(neighbor, graph, visited, recursionStack, new List<string>(path));
                        if (cycle != null)
                        {
                            return cycle;
                        }
                    }
                    else if (recursionStack.Contains(neighbor))
                    {
                        var cycleStart = path.IndexOf(neighbor);
                        return path.Skip(cycleStart).Concat(new[] { neighbor }).ToList();
                    }
                }
            }

            recursionStack.Remove(node);
            return null;
        }

        private string GetConstraintTypeDescription(string constraintType)
        {
            return constraintType switch
            {
                "PK" => "primary key",
                "FK" => "foreign key",
                "UQ" => "unique",
                "CK" => "check",
                _ => constraintType.ToLowerInvariant()
            };
        }

        private HashSet<string> GetProviderReservedWords(string provider)
        {
            return provider switch
            {
                "sqlserver" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "BACKUP", "BEGIN", "BETWEEN", "BREAK",
                    "BROWSE", "BULK", "BY", "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED",
                    "COALESCE", "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT", "CONTAINS", "CONTINUE",
                    "CONVERT", "CREATE", "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
                    "CURRENT_USER", "CURSOR", "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE",
                    "DENY", "DESC", "DISK", "DISTINCT", "DISTRIBUTED", "DOUBLE", "DROP", "DUMP", "ELSE", "END",
                    "ERRLVL", "ESCAPE", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "EXIT", "EXTERNAL", "FETCH",
                    "FILE", "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FROM", "FULL", "FUNCTION", "GOTO",
                    "GRANT", "GROUP", "HAVING", "HOLDLOCK", "IDENTITY", "IF", "IN", "INDEX", "INNER", "INSERT",
                    "INTERSECT", "INTO", "IS", "JOIN", "KEY", "KILL", "LEFT", "LIKE", "LINENO", "LOAD", "MERGE",
                    "NATIONAL", "NOCHECK", "NONCLUSTERED", "NOT", "NULL", "NULLIF", "OF", "OFF", "OFFSETS",
                    "ON", "OPEN", "OPTION", "OR", "ORDER", "OUTER", "OVER", "PERCENT", "PIVOT", "PLAN",
                    "PRECISION", "PRIMARY", "PRINT", "PROC", "PROCEDURE", "PUBLIC", "RAISERROR", "READ",
                    "READTEXT", "RECONFIGURE", "REFERENCES", "REPLICATION", "RESTORE", "RESTRICT", "RETURN",
                    "REVERT", "REVOKE", "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE", "SAVE", "SCHEMA",
                    "SELECT", "SESSION_USER", "SET", "SETUSER", "SHUTDOWN", "SOME", "STATISTICS", "SYSTEM_USER",
                    "TABLE", "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP", "TRAN", "TRANSACTION", "TRIGGER",
                    "TRUNCATE", "TSEQUAL", "UNION", "UNIQUE", "UNPIVOT", "UPDATE", "UPDATETEXT", "USE", "USER",
                    "VALUES", "VARYING", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH", "WRITETEXT"
                },
                "postgresql" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ALL", "ANALYSE", "ANALYZE", "AND", "ANY", "ARRAY", "AS", "ASC", "ASYMMETRIC", "BOTH",
                    "CASE", "CAST", "CHECK", "COLLATE", "COLUMN", "CONSTRAINT", "CREATE", "CURRENT_CATALOG",
                    "CURRENT_DATE", "CURRENT_ROLE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER",
                    "DEFAULT", "DEFERRABLE", "DESC", "DISTINCT", "DO", "ELSE", "END", "EXCEPT", "FALSE",
                    "FETCH", "FOR", "FOREIGN", "FROM", "GRANT", "GROUP", "HAVING", "IN", "INITIALLY",
                    "INTERSECT", "INTO", "LEADING", "LIMIT", "LOCALTIME", "LOCALTIMESTAMP", "NOT", "NULL",
                    "OFFSET", "ON", "ONLY", "OR", "ORDER", "PLACING", "PRIMARY", "REFERENCES", "RETURNING",
                    "SELECT", "SESSION_USER", "SOME", "SYMMETRIC", "TABLE", "THEN", "TO", "TRAILING",
                    "TRUE", "UNION", "UNIQUE", "USER", "USING", "VARIADIC", "WHEN", "WHERE", "WINDOW", "WITH"
                },
                "mysql" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ACCESSIBLE", "ADD", "ALL", "ALTER", "ANALYZE", "AND", "AS", "ASC", "ASENSITIVE",
                    "BEFORE", "BETWEEN", "BIGINT", "BINARY", "BLOB", "BOTH", "BY", "CALL", "CASCADE",
                    "CASE", "CHANGE", "CHAR", "CHARACTER", "CHECK", "COLLATE", "COLUMN", "CONDITION",
                    "CONSTRAINT", "CONTINUE", "CONVERT", "CREATE", "CROSS", "CURRENT_DATE", "CURRENT_TIME",
                    "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR", "DATABASE", "DATABASES", "DAY_HOUR",
                    "DAY_MICROSECOND", "DAY_MINUTE", "DAY_SECOND", "DEC", "DECIMAL", "DECLARE", "DEFAULT",
                    "DELAYED", "DELETE", "DESC", "DESCRIBE", "DETERMINISTIC", "DISTINCT", "DISTINCTROW",
                    "DIV", "DOUBLE", "DROP", "DUAL", "EACH", "ELSE", "ELSEIF", "ENCLOSED", "ESCAPED",
                    "EXISTS", "EXIT", "EXPLAIN", "FALSE", "FETCH", "FLOAT", "FOR", "FORCE", "FOREIGN",
                    "FROM", "FULLTEXT", "GRANT", "GROUP", "HAVING", "HIGH_PRIORITY", "IF", "IGNORE", "IN",
                    "INDEX", "INFILE", "INNER", "INOUT", "INSENSITIVE", "INSERT", "INT", "INTEGER",
                    "INTERVAL", "INTO", "IS", "ITERATE", "JOIN", "KEY", "KEYS", "KILL", "LEADING", "LEAVE",
                    "LEFT", "LIKE", "LIMIT", "LINEAR", "LINES", "LOAD", "LOCALTIME", "LOCALTIMESTAMP",
                    "LOCK", "LONG", "LOOP", "LOW_PRIORITY", "MATCH", "MEDIUMBLOB", "MEDIUMINT", "MEDIUMTEXT",
                    "MIDDLEINT", "MINUTE_MICROSECOND", "MINUTE_SECOND", "MOD", "MODIFIES", "NATURAL", "NOT",
                    "NO_WRITE_TO_BINLOG", "NULL", "NUMERIC", "ON", "OPTIMIZE", "OPTION", "OPTIONALLY",
                    "OR", "ORDER", "OUT", "OUTER", "OUTFILE", "PRECISION", "PRIMARY", "PROCEDURE",
                    "PURGE", "RANGE", "READ", "READS", "READ_WRITE", "REAL", "REFERENCES", "REGEXP",
                    "RELEASE", "RENAME", "REPEAT", "REPLACE", "REQUIRE", "RESTRICT", "RETURN", "REVOKE",
                    "RIGHT", "RLIKE", "SCHEMA", "SCHEMAS", "SECOND_MICROSECOND", "SELECT", "SENSITIVE",
                    "SEPARATOR", "SET", "SHOW", "SMALLINT", "SPATIAL", "SPECIFIC", "SQL", "SQLEXCEPTION",
                    "SQLSTATE", "SQLWARNING", "SQL_BIG_RESULT", "SQL_CALC_FOUND_ROWS", "SQL_SMALL_RESULT",
                    "SSL", "STARTING", "STRAIGHT_JOIN", "TABLE", "TERMINATED", "THEN", "TINYBLOB",
                    "TINYINT", "TINYTEXT", "TO", "TRAILING", "TRIGGER", "TRUE", "UNDO", "UNION",
                    "UNIQUE", "UNLOCK", "UNSIGNED", "UPDATE", "USAGE", "USE", "USING", "UTC_DATE",
                    "UTC_TIME", "UTC_TIMESTAMP", "VALUES", "VARBINARY", "VARCHAR", "VARCHARACTER",
                    "VARYING", "WHEN", "WHERE", "WHILE", "WITH", "WRITE", "XOR", "YEAR_MONTH", "ZEROFILL"
                },
                _ => new HashSet<string>()
            };
        }

        private DataTypeInfo NormalizeDataType(string dataType, string provider)
        {
            var cleanType = dataType.ToUpperInvariant().Split('(')[0].Trim();

            return provider switch
            {
                "sqlserver" => NormalizeSqlServerDataType(cleanType, dataType),
                "postgresql" => NormalizePostgreSqlDataType(cleanType, dataType),
                "mysql" => NormalizeMySqlDataType(cleanType, dataType),
                "oracle" => NormalizeOracleDataType(cleanType, dataType),
                "sqlite" => NormalizeSqliteDataType(cleanType, dataType),
                _ => new DataTypeInfo { Category = "unknown", Size = 0, Precision = 0, Scale = 0 }
            };
        }

        private DataTypeInfo NormalizeSqlServerDataType(string cleanType, string fullType)
        {
            return cleanType switch
            {
                "TINYINT" => new DataTypeInfo { Category = "integer", Size = 1, Precision = 3, Scale = 0 },
                "SMALLINT" => new DataTypeInfo { Category = "integer", Size = 2, Precision = 5, Scale = 0 },
                "INT" => new DataTypeInfo { Category = "integer", Size = 4, Precision = 10, Scale = 0 },
                "BIGINT" => new DataTypeInfo { Category = "integer", Size = 8, Precision = 19, Scale = 0 },
                "BIT" => new DataTypeInfo { Category = "boolean", Size = 1, Precision = 1, Scale = 0 },
                "DECIMAL" or "NUMERIC" => ExtractDecimalInfo(fullType, "decimal"),
                "MONEY" => new DataTypeInfo { Category = "decimal", Size = 8, Precision = 19, Scale = 4 },
                "SMALLMONEY" => new DataTypeInfo { Category = "decimal", Size = 4, Precision = 10, Scale = 4 },
                "FLOAT" => new DataTypeInfo { Category = "float", Size = 8, Precision = 53, Scale = 0 },
                "REAL" => new DataTypeInfo { Category = "float", Size = 4, Precision = 24, Scale = 0 },
                "DATETIME" or "DATETIME2" or "SMALLDATETIME" => new DataTypeInfo { Category = "datetime", Size = 8, Precision = 0, Scale = 0 },
                "DATE" => new DataTypeInfo { Category = "date", Size = 3, Precision = 0, Scale = 0 },
                "TIME" => new DataTypeInfo { Category = "time", Size = 5, Precision = 0, Scale = 0 },
                "CHAR" or "NCHAR" => ExtractStringInfo(fullType, "char"),
                "VARCHAR" or "NVARCHAR" => ExtractStringInfo(fullType, "varchar"),
                "TEXT" or "NTEXT" => new DataTypeInfo { Category = "text", Size = int.MaxValue, Precision = 0, Scale = 0 },
                "BINARY" or "VARBINARY" => ExtractBinaryInfo(fullType),
                "UNIQUEIDENTIFIER" => new DataTypeInfo { Category = "guid", Size = 16, Precision = 0, Scale = 0 },
                _ => new DataTypeInfo { Category = "unknown", Size = 0, Precision = 0, Scale = 0 }
            };
        }

        private DataTypeInfo NormalizePostgreSqlDataType(string cleanType, string fullType)
        {
            return cleanType switch
            {
                "SMALLINT" or "INT2" => new DataTypeInfo { Category = "integer", Size = 2, Precision = 5, Scale = 0 },
                "INTEGER" or "INT" or "INT4" => new DataTypeInfo { Category = "integer", Size = 4, Precision = 10, Scale = 0 },
                "BIGINT" or "INT8" => new DataTypeInfo { Category = "integer", Size = 8, Precision = 19, Scale = 0 },
                "BOOLEAN" or "BOOL" => new DataTypeInfo { Category = "boolean", Size = 1, Precision = 1, Scale = 0 },
                "DECIMAL" or "NUMERIC" => ExtractDecimalInfo(fullType, "decimal"),
                "REAL" or "FLOAT4" => new DataTypeInfo { Category = "float", Size = 4, Precision = 24, Scale = 0 },
                "DOUBLE" or "FLOAT8" => new DataTypeInfo { Category = "float", Size = 8, Precision = 53, Scale = 0 },
                "TIMESTAMP" or "TIMESTAMPTZ" => new DataTypeInfo { Category = "datetime", Size = 8, Precision = 0, Scale = 0 },
                "DATE" => new DataTypeInfo { Category = "date", Size = 4, Precision = 0, Scale = 0 },
                "TIME" or "TIMETZ" => new DataTypeInfo { Category = "time", Size = 8, Precision = 0, Scale = 0 },
                "CHAR" or "CHARACTER" => ExtractStringInfo(fullType, "char"),
                "VARCHAR" => ExtractStringInfo(fullType, "varchar"),
                "TEXT" => new DataTypeInfo { Category = "text", Size = int.MaxValue, Precision = 0, Scale = 0 },
                "BYTEA" => new DataTypeInfo { Category = "binary", Size = int.MaxValue, Precision = 0, Scale = 0 },
                "UUID" => new DataTypeInfo { Category = "guid", Size = 16, Precision = 0, Scale = 0 },
                _ => new DataTypeInfo { Category = "unknown", Size = 0, Precision = 0, Scale = 0 }
            };
        }

        private DataTypeInfo NormalizeMySqlDataType(string cleanType, string fullType)
        {
            return cleanType switch
            {
                "TINYINT" => new DataTypeInfo { Category = "integer", Size = 1, Precision = 3, Scale = 0 },
                "SMALLINT" => new DataTypeInfo { Category = "integer", Size = 2, Precision = 5, Scale = 0 },
                "MEDIUMINT" => new DataTypeInfo { Category = "integer", Size = 3, Precision = 7, Scale = 0 },
                "INT" or "INTEGER" => new DataTypeInfo { Category = "integer", Size = 4, Precision = 10, Scale = 0 },
                "BIGINT" => new DataTypeInfo { Category = "integer", Size = 8, Precision = 19, Scale = 0 },
                "BOOLEAN" or "BOOL" => new DataTypeInfo { Category = "boolean", Size = 1, Precision = 1, Scale = 0 },
                "DECIMAL" or "NUMERIC" => ExtractDecimalInfo(fullType, "decimal"),
                "FLOAT" => new DataTypeInfo { Category = "float", Size = 4, Precision = 24, Scale = 0 },
                "DOUBLE" => new DataTypeInfo { Category = "float", Size = 8, Precision = 53, Scale = 0 },
                "DATETIME" or "TIMESTAMP" => new DataTypeInfo { Category = "datetime", Size = 8, Precision = 0, Scale = 0 },
                "DATE" => new DataTypeInfo { Category = "date", Size = 3, Precision = 0, Scale = 0 },
                "TIME" => new DataTypeInfo { Category = "time", Size = 3, Precision = 0, Scale = 0 },
                "CHAR" => ExtractStringInfo(fullType, "char"),
                "VARCHAR" => ExtractStringInfo(fullType, "varchar"),
                "TEXT" or "LONGTEXT" => new DataTypeInfo { Category = "text", Size = int.MaxValue, Precision = 0, Scale = 0 },
                "BINARY" or "VARBINARY" => ExtractBinaryInfo(fullType),
                "BLOB" or "LONGBLOB" => new DataTypeInfo { Category = "binary", Size = int.MaxValue, Precision = 0, Scale = 0 },
                _ => new DataTypeInfo { Category = "unknown", Size = 0, Precision = 0, Scale = 0 }
            };
        }

        private DataTypeInfo NormalizeOracleDataType(string cleanType, string fullType)
        {
            return cleanType switch
            {
                "NUMBER" => ExtractOracleNumberInfo(fullType),
                "BINARY_FLOAT" => new DataTypeInfo { Category = "float", Size = 4, Precision = 24, Scale = 0 },
                "BINARY_DOUBLE" => new DataTypeInfo { Category = "float", Size = 8, Precision = 53, Scale = 0 },
                "DATE" => new DataTypeInfo { Category = "datetime", Size = 7, Precision = 0, Scale = 0 },
                "TIMESTAMP" => new DataTypeInfo { Category = "datetime", Size = 11, Precision = 0, Scale = 0 },
                "CHAR" => ExtractStringInfo(fullType, "char"),
                "VARCHAR2" => ExtractStringInfo(fullType, "varchar"),
                "NCHAR" => ExtractStringInfo(fullType, "nchar"),
                "NVARCHAR2" => ExtractStringInfo(fullType, "nvarchar"),
                "CLOB" or "NCLOB" => new DataTypeInfo { Category = "text", Size = int.MaxValue, Precision = 0, Scale = 0 },
                "RAW" => ExtractBinaryInfo(fullType),
                "BLOB" => new DataTypeInfo { Category = "binary", Size = int.MaxValue, Precision = 0, Scale = 0 },
                _ => new DataTypeInfo { Category = "unknown", Size = 0, Precision = 0, Scale = 0 }
            };
        }

        private DataTypeInfo NormalizeSqliteDataType(string cleanType, string fullType)
        {
            return cleanType switch
            {
                "INTEGER" => new DataTypeInfo { Category = "integer", Size = 8, Precision = 19, Scale = 0 },
                "REAL" => new DataTypeInfo { Category = "float", Size = 8, Precision = 53, Scale = 0 },
                "TEXT" => new DataTypeInfo { Category = "text", Size = int.MaxValue, Precision = 0, Scale = 0 },
                "BLOB" => new DataTypeInfo { Category = "binary", Size = int.MaxValue, Precision = 0, Scale = 0 },
                "NUMERIC" => new DataTypeInfo { Category = "decimal", Size = 8, Precision = 19, Scale = 4 },
                _ => new DataTypeInfo { Category = "text", Size = int.MaxValue, Precision = 0, Scale = 0 }
            };
        }

        private DataTypeInfo ExtractDecimalInfo(string fullType, string category)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fullType, @"\((\d+),(\d+)\)");
            if (match.Success)
            {
                var precision = int.Parse(match.Groups[1].Value);
                var scale = int.Parse(match.Groups[2].Value);
                return new DataTypeInfo { Category = category, Size = precision / 2 + 1, Precision = precision, Scale = scale };
            }
            return new DataTypeInfo { Category = category, Size = 9, Precision = 18, Scale = 2 };
        }

        private DataTypeInfo ExtractStringInfo(string fullType, string category)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fullType, @"\((\d+)\)");
            if (match.Success)
            {
                var length = int.Parse(match.Groups[1].Value);
                return new DataTypeInfo { Category = category, Size = length, Precision = length, Scale = 0 };
            }
            return new DataTypeInfo { Category = category, Size = 255, Precision = 255, Scale = 0 };
        }

        private DataTypeInfo ExtractBinaryInfo(string fullType)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fullType, @"\((\d+)\)");
            if (match.Success)
            {
                var length = int.Parse(match.Groups[1].Value);
                return new DataTypeInfo { Category = "binary", Size = length, Precision = length, Scale = 0 };
            }
            return new DataTypeInfo { Category = "binary", Size = 255, Precision = 255, Scale = 0 };
        }

        private DataTypeInfo ExtractOracleNumberInfo(string fullType)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fullType, @"\((\d+),(\d+)\)");
            if (match.Success)
            {
                var precision = int.Parse(match.Groups[1].Value);
                var scale = int.Parse(match.Groups[2].Value);
                if (scale == 0)
                {
                    return new DataTypeInfo { Category = "integer", Size = precision <= 10 ? 4 : 8, Precision = precision, Scale = 0 };
                }
                return new DataTypeInfo { Category = "decimal", Size = precision / 2 + 1, Precision = precision, Scale = scale };
            }

            var precisionMatch = System.Text.RegularExpressions.Regex.Match(fullType, @"\((\d+)\)");
            if (precisionMatch.Success)
            {
                var precision = int.Parse(precisionMatch.Groups[1].Value);
                return new DataTypeInfo { Category = "integer", Size = precision <= 10 ? 4 : 8, Precision = precision, Scale = 0 };
            }

            return new DataTypeInfo { Category = "decimal", Size = 9, Precision = 18, Scale = 2 };
        }

        private Dictionary<(string from, string to), RiskLevel> GetDataTypeCompatibilityMatrix(string provider)
        {
            return new Dictionary<(string, string), RiskLevel>
            {
                // Safe conversions
                { ("integer", "integer"), RiskLevel.Safe },
                { ("integer", "decimal"), RiskLevel.Safe },
                { ("integer", "float"), RiskLevel.Safe },
                { ("char", "varchar"), RiskLevel.Safe },
                { ("varchar", "text"), RiskLevel.Safe },
                { ("date", "datetime"), RiskLevel.Safe },
                { ("time", "datetime"), RiskLevel.Safe },

                // Warning conversions
                { ("decimal", "integer"), RiskLevel.Warning },
                { ("float", "integer"), RiskLevel.Warning },
                { ("float", "decimal"), RiskLevel.Warning },
                { ("varchar", "char"), RiskLevel.Warning },
                { ("text", "varchar"), RiskLevel.Warning },
                { ("datetime", "date"), RiskLevel.Warning },
                { ("datetime", "time"), RiskLevel.Warning },

                // Risky conversions
                { ("text", "char"), RiskLevel.Risky },
                { ("binary", "text"), RiskLevel.Risky },
                { ("text", "binary"), RiskLevel.Risky },
                { ("guid", "text"), RiskLevel.Risky },
                { ("text", "guid"), RiskLevel.Risky },
                { ("boolean", "integer"), RiskLevel.Warning },
                { ("integer", "boolean"), RiskLevel.Risky }
            };
        }

        private class DataTypeInfo
        {
            public string Category { get; set; } = string.Empty;
            public int Size { get; set; }
            public int Precision { get; set; }
            public int Scale { get; set; }
        }
    }
}