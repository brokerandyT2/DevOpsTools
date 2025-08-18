using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ISchemaAnalysisService
    {
        Task<DatabaseSchema> AnalyzeCurrentSchemaAsync(SqlSchemaConfiguration config);
        Task<DatabaseSchema> GenerateTargetSchemaAsync(EntityDiscoveryResult entities, SqlSchemaConfiguration config);
    }

    public class SchemaAnalysisService : ISchemaAnalysisService
    {
        private readonly IDatabaseProviderFactory _databaseProviderFactory;
        private readonly ILogger<SchemaAnalysisService> _logger;

        public SchemaAnalysisService(
            IDatabaseProviderFactory databaseProviderFactory,
            ILogger<SchemaAnalysisService> logger)
        {
            _databaseProviderFactory = databaseProviderFactory;
            _logger = logger;
        }

        public async Task<DatabaseSchema> AnalyzeCurrentSchemaAsync(SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Analyzing current database schema for {Provider}:{Database}",
                    config.Database.GetSelectedProvider().ToUpperInvariant(),
                    config.Database.DatabaseName);

                var provider = await _databaseProviderFactory.GetProviderAsync(config.Database.GetSelectedProvider());
                var currentSchema = await provider.GetCurrentSchemaAsync(config);

                _logger.LogInformation("✓ Current schema analysis complete: {TableCount} tables, {ViewCount} views, {IndexCount} indexes",
                    currentSchema.Tables.Count, currentSchema.Views.Count, currentSchema.Indexes.Count);

                return currentSchema;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze current database schema");
                throw new SqlSchemaException(SqlSchemaExitCode.DatabaseConnectionFailure,
                    $"Failed to analyze current database schema: {ex.Message}", ex);
            }
        }

        public async Task<DatabaseSchema> GenerateTargetSchemaAsync(EntityDiscoveryResult entities, SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Generating target schema from {EntityCount} discovered entities", entities.Entities.Count);

                var targetSchema = new DatabaseSchema
                {
                    DatabaseName = config.Database.DatabaseName,
                    Provider = config.Database.GetSelectedProvider(),
                    AnalysisTime = DateTime.UtcNow,
                    Tables = new List<SchemaTable>(),
                    Views = new List<SchemaView>(),
                    Indexes = new List<SchemaIndex>(),
                    Constraints = new List<SchemaConstraint>(),
                    Procedures = new List<SchemaProcedure>(),
                    Functions = new List<SchemaFunction>(),
                    Metadata = new Dictionary<string, object>
                    {
                        ["generated_from_entities"] = true,
                        ["entity_count"] = entities.Entities.Count,
                        ["language"] = entities.Language,
                        ["track_attribute"] = entities.TrackAttribute
                    }
                };

                foreach (var entity in entities.Entities)
                {
                    await GenerateTableFromEntityAsync(entity, targetSchema, config);
                }

                await GenerateConstraintsAsync(entities, targetSchema, config);
                await GenerateIndexesAsync(entities, targetSchema, config);

                _logger.LogInformation("✓ Target schema generation complete: {TableCount} tables, {ConstraintCount} constraints, {IndexCount} indexes",
                    targetSchema.Tables.Count, targetSchema.Constraints.Count, targetSchema.Indexes.Count);

                return targetSchema;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate target schema");
                throw new SqlSchemaException(SqlSchemaExitCode.SchemaValidationFailure,
                    $"Failed to generate target schema: {ex.Message}", ex);
            }
        }

        private async Task GenerateTableFromEntityAsync(DiscoveredEntity entity, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            try
            {
                var table = new SchemaTable
                {
                    Name = entity.TableName,
                    Schema = entity.SchemaName ?? config.Database.Schema ?? GetDefaultSchema(config.Database.GetSelectedProvider()),
                    Columns = new List<SchemaColumn>(),
                    Indexes = new List<SchemaIndex>(),
                    Constraints = new List<SchemaConstraint>(),
                    Metadata = new Dictionary<string, object>
                    {
                        ["entity_name"] = entity.Name,
                        ["entity_full_name"] = entity.FullName,
                        ["entity_namespace"] = entity.Namespace,
                        ["source_file"] = entity.SourceFile,
                        ["source_line"] = entity.SourceLine
                    }
                };

                foreach (var property in entity.Properties)
                {
                    var column = await GenerateColumnFromPropertyAsync(property, config);
                    table.Columns.Add(column);
                }

                await ValidateTableStructureAsync(table, entity, config);
                targetSchema.Tables.Add(table);

                _logger.LogDebug("Generated table {TableName} with {ColumnCount} columns from entity {EntityName}",
                    table.Name, table.Columns.Count, entity.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate table from entity: {EntityName}", entity.Name);
                throw;
            }
        }

        private async Task<SchemaColumn> GenerateColumnFromPropertyAsync(DiscoveredProperty property, SqlSchemaConfiguration config)
        {
            var column = new SchemaColumn
            {
                Name = property.Attributes.ContainsKey("column_name")
                    ? property.Attributes["column_name"].ToString()!
                    : property.Name,
                DataType = await RefineDataTypeAsync(property, config),
                IsNullable = property.IsNullable,
                IsPrimaryKey = property.IsPrimaryKey,
                IsIdentity = property.IsPrimaryKey && IsIdentityType(property.Type),
                MaxLength = property.MaxLength,
                Precision = property.Precision,
                Scale = property.Scale,
                DefaultValue = await ProcessDefaultValueAsync(property.DefaultValue, config),
                Metadata = new Dictionary<string, object>
                {
                    ["property_name"] = property.Name,
                    ["property_type"] = property.Type,
                    ["is_foreign_key"] = property.IsForeignKey,
                    ["is_unique"] = property.IsUnique,
                    ["is_indexed"] = property.IsIndexed,
                    ["source_attributes"] = property.Attributes
                }
            };

            return column;
        }

        private async Task<string> RefineDataTypeAsync(DiscoveredProperty property, SqlSchemaConfiguration config)
        {
            var provider = config.Database.GetSelectedProvider();
            var baseType = property.SqlType;

            if (property.MaxLength.HasValue && baseType.Contains("VARCHAR"))
            {
                if (provider == "sqlserver" && baseType.StartsWith("NVARCHAR"))
                {
                    return $"NVARCHAR({property.MaxLength})";
                }
                else if (baseType.Contains("VARCHAR"))
                {
                    return $"VARCHAR({property.MaxLength})";
                }
            }

            if (property.Precision.HasValue && property.Scale.HasValue && baseType.Contains("DECIMAL"))
            {
                return $"DECIMAL({property.Precision},{property.Scale})";
            }

            if (property.IsPrimaryKey && IsIdentityType(property.Type))
            {
                return provider switch
                {
                    "sqlserver" => baseType.Contains("BIGINT") ? "BIGINT IDENTITY(1,1)" : "INT IDENTITY(1,1)",
                    "postgresql" => baseType.Contains("BIGINT") ? "BIGSERIAL" : "SERIAL",
                    "mysql" => $"{baseType} AUTO_INCREMENT",
                    "sqlite" => "INTEGER PRIMARY KEY AUTOINCREMENT",
                    "oracle" => baseType.Contains("NUMBER") ? "NUMBER GENERATED BY DEFAULT AS IDENTITY" : baseType,
                    _ => baseType
                };
            }

            return baseType;
        }

        private async Task<string?> ProcessDefaultValueAsync(string? defaultValue, SqlSchemaConfiguration config)
        {
            if (string.IsNullOrEmpty(defaultValue))
                return null;

            var provider = config.Database.GetSelectedProvider();

            return provider switch
            {
                "sqlserver" when defaultValue.Equals("GETUTCDATE()", StringComparison.OrdinalIgnoreCase) => "GETUTCDATE()",
                "sqlserver" when defaultValue.Equals("NEWID()", StringComparison.OrdinalIgnoreCase) => "NEWID()",
                "postgresql" when defaultValue.Equals("NOW()", StringComparison.OrdinalIgnoreCase) => "NOW()",
                "postgresql" when defaultValue.Equals("UUID()", StringComparison.OrdinalIgnoreCase) => "gen_random_uuid()",
                "mysql" when defaultValue.Equals("NOW()", StringComparison.OrdinalIgnoreCase) => "NOW()",
                "mysql" when defaultValue.Equals("UUID()", StringComparison.OrdinalIgnoreCase) => "UUID()",
                "sqlite" when defaultValue.Equals("NOW()", StringComparison.OrdinalIgnoreCase) => "datetime('now')",
                "oracle" when defaultValue.Equals("SYSDATE", StringComparison.OrdinalIgnoreCase) => "SYSDATE",
                "oracle" when defaultValue.Equals("SYS_GUID()", StringComparison.OrdinalIgnoreCase) => "SYS_GUID()",
                _ => defaultValue
            };
        }

        private bool IsIdentityType(string type)
        {
            return type.ToLowerInvariant() switch
            {
                "int" or "int32" or "long" or "int64" or "short" or "int16" => true,
                _ => false
            };
        }

        private async Task ValidateTableStructureAsync(SchemaTable table, DiscoveredEntity entity, SqlSchemaConfiguration config)
        {
            if (!table.Columns.Any())
            {
                throw new InvalidOperationException($"Table {table.Name} has no columns");
            }

            var primaryKeys = table.Columns.Where(c => c.IsPrimaryKey).ToList();
            if (!primaryKeys.Any())
            {
                _logger.LogWarning("Table {TableName} has no primary key defined", table.Name);
            }
            else if (primaryKeys.Count > 1)
            {
                _logger.LogInformation("Table {TableName} has composite primary key with {KeyCount} columns",
                    table.Name, primaryKeys.Count);
            }

            var duplicateColumns = table.Columns
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateColumns.Any())
            {
                var duplicateNames = string.Join(", ", duplicateColumns.Select(g => g.Key));
                throw new InvalidOperationException($"Table {table.Name} has duplicate column names: {duplicateNames}");
            }

            await ValidateColumnNamesAsync(table, config);
        }

        private async Task ValidateColumnNamesAsync(SchemaTable table, SqlSchemaConfiguration config)
        {
            var provider = config.Database.GetSelectedProvider();
            var reservedWords = GetReservedWords(provider);

            foreach (var column in table.Columns)
            {
                if (reservedWords.Contains(column.Name.ToUpperInvariant()))
                {
                    _logger.LogWarning("Column {ColumnName} in table {TableName} is a reserved word in {Provider}",
                        column.Name, table.Name, provider.ToUpperInvariant());
                }

                if (column.Name.Length > GetMaxIdentifierLength(provider))
                {
                    _logger.LogWarning("Column {ColumnName} in table {TableName} exceeds maximum identifier length for {Provider}",
                        column.Name, table.Name, provider.ToUpperInvariant());
                }
            }
        }

        private async Task GenerateConstraintsAsync(EntityDiscoveryResult entities, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            _logger.LogDebug("Generating constraints for {EntityCount} entities", entities.Entities.Count);

            foreach (var entity in entities.Entities)
            {
                var table = targetSchema.Tables.FirstOrDefault(t =>
                    t.Name.Equals(entity.TableName, StringComparison.OrdinalIgnoreCase));

                if (table == null) continue;

                await GeneratePrimaryKeyConstraintsAsync(entity, table, targetSchema, config);
                await GenerateUniqueConstraintsAsync(entity, table, targetSchema, config);
                await GenerateForeignKeyConstraintsAsync(entity, table, targetSchema, config);
                await GenerateCheckConstraintsAsync(entity, table, targetSchema, config);
            }

            _logger.LogDebug("Generated {ConstraintCount} constraints", targetSchema.Constraints.Count);
        }

        private async Task GeneratePrimaryKeyConstraintsAsync(DiscoveredEntity entity, SchemaTable table, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            var primaryKeyColumns = table.Columns.Where(c => c.IsPrimaryKey).ToList();
            if (!primaryKeyColumns.Any()) return;

            var constraint = new SchemaConstraint
            {
                Name = $"PK_{table.Name}",
                Type = "PK",
                TableName = table.Name,
                Schema = table.Schema,
                Columns = primaryKeyColumns.Select(c => c.Name).ToList(),
                Metadata = new Dictionary<string, object>
                {
                    ["entity_name"] = entity.Name,
                    ["generated"] = true,
                    ["constraint_type"] = "PRIMARY_KEY"
                }
            };

            targetSchema.Constraints.Add(constraint);
        }

        private async Task GenerateUniqueConstraintsAsync(DiscoveredEntity entity, SchemaTable table, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            var uniqueProperties = entity.Properties.Where(p => p.IsUnique && !p.IsPrimaryKey).ToList();

            foreach (var property in uniqueProperties)
            {
                var column = table.Columns.FirstOrDefault(c =>
                    c.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase) ||
                    c.Metadata.ContainsKey("property_name") &&
                    c.Metadata["property_name"].ToString()!.Equals(property.Name, StringComparison.OrdinalIgnoreCase));

                if (column == null) continue;

                var constraint = new SchemaConstraint
                {
                    Name = $"UQ_{table.Name}_{column.Name}",
                    Type = "UQ",
                    TableName = table.Name,
                    Schema = table.Schema,
                    Columns = new List<string> { column.Name },
                    Metadata = new Dictionary<string, object>
                    {
                        ["entity_name"] = entity.Name,
                        ["property_name"] = property.Name,
                        ["generated"] = true,
                        ["constraint_type"] = "UNIQUE"
                    }
                };

                targetSchema.Constraints.Add(constraint);
            }
        }

        private async Task GenerateForeignKeyConstraintsAsync(DiscoveredEntity entity, SchemaTable table, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            foreach (var relationship in entity.Relationships)
            {
                if (!config.SchemaAnalysis.EnableCrossSchemaRefs &&
                    !string.IsNullOrEmpty(relationship.ReferencedTable) &&
                    !targetSchema.Tables.Any(t => t.Name.Equals(relationship.ReferencedTable, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Skipping foreign key constraint for {TableName}.{RelationshipName} - referenced table {ReferencedTable} not found and cross-schema references disabled",
                        table.Name, relationship.Name, relationship.ReferencedTable);
                    continue;
                }

                var foreignKeyColumns = relationship.ForeignKeyColumns
                    .Where(fkCol => table.Columns.Any(c => c.Name.Equals(fkCol, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (!foreignKeyColumns.Any()) continue;

                var constraint = new SchemaConstraint
                {
                    Name = $"FK_{table.Name}_{relationship.ReferencedTable}_{string.Join("_", foreignKeyColumns)}",
                    Type = "FK",
                    TableName = table.Name,
                    Schema = table.Schema,
                    Columns = foreignKeyColumns,
                    ReferencedTable = relationship.ReferencedTable,
                    ReferencedSchema = table.Schema,
                    ReferencedColumns = relationship.ReferencedColumns.Any() ? relationship.ReferencedColumns : new List<string> { "Id" },
                    OnDeleteAction = relationship.OnDeleteAction,
                    OnUpdateAction = relationship.OnUpdateAction,
                    Metadata = new Dictionary<string, object>
                    {
                        ["entity_name"] = entity.Name,
                        ["relationship_name"] = relationship.Name,
                        ["relationship_type"] = relationship.Type,
                        ["generated"] = true,
                        ["constraint_type"] = "FOREIGN_KEY"
                    }
                };

                targetSchema.Constraints.Add(constraint);
            }
        }

        private async Task GenerateCheckConstraintsAsync(DiscoveredEntity entity, SchemaTable table, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            foreach (var property in entity.Properties)
            {
                if (property.Attributes.ContainsKey("check_constraint"))
                {
                    var checkExpression = property.Attributes["check_constraint"].ToString();
                    if (string.IsNullOrEmpty(checkExpression)) continue;

                    var column = table.Columns.FirstOrDefault(c =>
                        c.Metadata.ContainsKey("property_name") &&
                        c.Metadata["property_name"].ToString()!.Equals(property.Name, StringComparison.OrdinalIgnoreCase));

                    if (column == null) continue;

                    var constraint = new SchemaConstraint
                    {
                        Name = $"CK_{table.Name}_{column.Name}",
                        Type = "CK",
                        TableName = table.Name,
                        Schema = table.Schema,
                        Columns = new List<string> { column.Name },
                        CheckExpression = checkExpression,
                        Metadata = new Dictionary<string, object>
                        {
                            ["entity_name"] = entity.Name,
                            ["property_name"] = property.Name,
                            ["generated"] = true,
                            ["constraint_type"] = "CHECK"
                        }
                    };

                    targetSchema.Constraints.Add(constraint);
                }
            }
        }

        private async Task GenerateIndexesAsync(EntityDiscoveryResult entities, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            if (!config.SchemaAnalysis.GenerateIndexes) return;

            _logger.LogDebug("Generating indexes for {EntityCount} entities", entities.Entities.Count);

            foreach (var entity in entities.Entities)
            {
                var table = targetSchema.Tables.FirstOrDefault(t =>
                    t.Name.Equals(entity.TableName, StringComparison.OrdinalIgnoreCase));

                if (table == null) continue;

                foreach (var discoveredIndex in entity.Indexes)
                {
                    var schemaIndex = new SchemaIndex
                    {
                        Name = discoveredIndex.Name,
                        TableName = table.Name,
                        Schema = table.Schema,
                        Columns = discoveredIndex.Columns,
                        IsUnique = discoveredIndex.IsUnique,
                        IsClustered = discoveredIndex.IsClustered,
                        FilterExpression = discoveredIndex.FilterExpression,
                        Metadata = new Dictionary<string, object>
                        {
                            ["entity_name"] = entity.Name,
                            ["generated"] = discoveredIndex.Attributes.ContainsKey("generated") && (bool)discoveredIndex.Attributes["generated"],
                            ["source_attributes"] = discoveredIndex.Attributes
                        }
                    };

                    targetSchema.Indexes.Add(schemaIndex);
                }

                if (config.SchemaAnalysis.GenerateFkIndexes)
                {
                    await GenerateForeignKeyIndexesAsync(entity, table, targetSchema, config);
                }
            }

            _logger.LogDebug("Generated {IndexCount} indexes", targetSchema.Indexes.Count);
        }

        private async Task GenerateForeignKeyIndexesAsync(DiscoveredEntity entity, SchemaTable table, DatabaseSchema targetSchema, SqlSchemaConfiguration config)
        {
            var foreignKeyConstraints = targetSchema.Constraints
                .Where(c => c.Type == "FK" && c.TableName.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var fkConstraint in foreignKeyConstraints)
            {
                var indexName = $"IX_{table.Name}_{string.Join("_", fkConstraint.Columns)}";

                if (targetSchema.Indexes.Any(i => i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var index = new SchemaIndex
                {
                    Name = indexName,
                    TableName = table.Name,
                    Schema = table.Schema,
                    Columns = fkConstraint.Columns,
                    IsUnique = false,
                    IsClustered = false,
                    Metadata = new Dictionary<string, object>
                    {
                        ["entity_name"] = entity.Name,
                        ["generated"] = true,
                        ["foreign_key_index"] = true,
                        ["constraint_name"] = fkConstraint.Name
                    }
                };

                targetSchema.Indexes.Add(index);
            }
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
                _ => "dbo"
            };
        }

        private HashSet<string> GetReservedWords(string provider)
        {
            return provider switch
            {
                "sqlserver" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION", "BACKUP", "BEGIN",
                    "BETWEEN", "BREAK", "BROWSE", "BULK", "BY", "CASCADE", "CASE", "CHECK", "CHECKPOINT",
                    "CLOSE", "CLUSTERED", "COALESCE", "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT",
                    "CONTAINS", "CONTAINSTABLE", "CONTINUE", "CONVERT", "CREATE", "CROSS", "CURRENT",
                    "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR",
                    "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY", "DESC",
                    "DISK", "DISTINCT", "DISTRIBUTED", "DOUBLE", "DROP", "DUMP", "ELSE", "END", "ERRLVL",
                    "ESCAPE", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "EXIT", "EXTERNAL", "FETCH", "FILE",
                    "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE", "FROM", "FULL",
                    "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", "HOLDLOCK", "IDENTITY",
                    "IDENTITY_INSERT", "IDENTITYCOL", "IF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT",
                    "INTO", "IS", "JOIN", "KEY", "KILL", "LEFT", "LIKE", "LINENO", "LOAD", "MERGE",
                    "NATIONAL", "NOCHECK", "NONCLUSTERED", "NOT", "NULL", "NULLIF", "OF", "OFF", "OFFSETS",
                    "ON", "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML", "OPTION", "OR",
                    "ORDER", "OUTER", "OVER", "PERCENT", "PIVOT", "PLAN", "PRECISION", "PRIMARY", "PRINT",
                    "PROC", "PROCEDURE", "PUBLIC", "RAISERROR", "READ", "READTEXT", "RECONFIGURE",
                    "REFERENCES", "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVERT", "REVOKE",
                    "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE", "SAVE", "SCHEMA", "SECURITYAUDIT",
                    "SELECT", "SEMANTICKEYPHRASETABLE", "SEMANTICSIMILARITYDETAILSTABLE",
                    "SEMANTICSIMILARITYTABLE", "SESSION_USER", "SET", "SETUSER", "SHUTDOWN", "SOME",
                    "STATISTICS", "SYSTEM_USER", "TABLE", "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP",
                    "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE", "TRY_CONVERT", "TSEQUAL", "UNION",
                    "UNIQUE", "UNPIVOT", "UPDATE", "UPDATETEXT", "USE", "USER", "VALUES", "VARYING", "VIEW",
                    "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH", "WITHIN", "WRITETEXT"
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
                    "EXISTS", "EXIT", "EXPLAIN", "FALSE", "FETCH", "FLOAT", "FLOAT4", "FLOAT8", "FOR",
                    "FORCE", "FOREIGN", "FROM", "FULLTEXT", "GRANT", "GROUP", "HAVING", "HIGH_PRIORITY",
                    "HOUR_MICROSECOND", "HOUR_MINUTE", "HOUR_SECOND", "IF", "IGNORE", "IN", "INDEX",
                    "INFILE", "INNER", "INOUT", "INSENSITIVE", "INSERT", "INT", "INT1", "INT2", "INT3",
                    "INT4", "INT8", "INTEGER", "INTERVAL", "INTO", "IS", "ITERATE", "JOIN", "KEY", "KEYS",
                    "KILL", "LEADING", "LEAVE", "LEFT", "LIKE", "LIMIT", "LINEAR", "LINES", "LOAD",
                    "LOCALTIME", "LOCALTIMESTAMP", "LOCK", "LONG", "LONGBLOB", "LONGTEXT", "LOOP",
                    "LOW_PRIORITY", "MATCH", "MEDIUMBLOB", "MEDIUMINT", "MEDIUMTEXT", "MIDDLEINT",
                    "MINUTE_MICROSECOND", "MINUTE_SECOND", "MOD", "MODIFIES", "NATURAL", "NOT",
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
                "oracle" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ACCESS", "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUDIT", "BETWEEN", "BY",
                    "CHAR", "CHECK", "CLUSTER", "COLUMN", "COLUMN_VALUE", "COMMENT", "COMPRESS", "CONNECT",
                    "CREATE", "CURRENT", "DATE", "DECIMAL", "DEFAULT", "DELETE", "DESC", "DISTINCT", "DROP",
                    "ELSE", "EXCLUSIVE", "EXISTS", "FILE", "FLOAT", "FOR", "FROM", "GRANT", "GROUP",
                    "HAVING", "IDENTIFIED", "IMMEDIATE", "IN", "INCREMENT", "INDEX", "INITIAL", "INSERT",
                    "INTEGER", "INTERSECT", "INTO", "IS", "LEVEL", "LIKE", "LOCK", "LONG", "MAXEXTENTS",
                    "MINUS", "MLSLABEL", "MODE", "MODIFY", "NESTED_TABLE_ID", "NOAUDIT", "NOCOMPRESS",
                    "NOT", "NOWAIT", "NULL", "NUMBER", "OF", "OFFLINE", "ON", "ONLINE", "OPTION", "OR",
                    "ORDER", "PCTFREE", "PRIOR", "PUBLIC", "RAW", "RENAME", "RESOURCE", "REVOKE", "ROW",
                    "ROWID", "ROWNUM", "ROWS", "SELECT", "SESSION", "SET", "SHARE", "SIZE", "SMALLINT",
                    "START", "SUCCESSFUL", "SYNONYM", "SYSDATE", "TABLE", "THEN", "TO", "TRIGGER", "UID",
                    "UNION", "UNIQUE", "UPDATE", "USER", "VALIDATE", "VALUES", "VARCHAR", "VARCHAR2",
                    "VIEW", "WHENEVER", "WHERE", "WITH"
                },
                "sqlite" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ABORT", "ACTION", "ADD", "AFTER", "ALL", "ALTER", "ANALYZE", "AND", "AS", "ASC",
                    "ATTACH", "AUTOINCREMENT", "BEFORE", "BEGIN", "BETWEEN", "BY", "CASCADE", "CASE",
                    "CAST", "CHECK", "COLLATE", "COLUMN", "COMMIT", "CONFLICT", "CONSTRAINT", "CREATE",
                    "CROSS", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "DATABASE", "DEFAULT",
                    "DEFERRABLE", "DEFERRED", "DELETE", "DESC", "DETACH", "DISTINCT", "DROP", "EACH",
                    "ELSE", "END", "ESCAPE", "EXCEPT", "EXCLUSIVE", "EXISTS", "EXPLAIN", "FAIL", "FOR",
                    "FOREIGN", "FROM", "FULL", "GLOB", "GROUP", "HAVING", "IF", "IGNORE", "IMMEDIATE",
                    "IN", "INDEX", "INDEXED", "INITIALLY", "INNER", "INSERT", "INSTEAD", "INTERSECT",
                    "INTO", "IS", "ISNULL", "JOIN", "KEY", "LEFT", "LIKE", "LIMIT", "MATCH", "NATURAL",
                    "NO", "NOT", "NOTNULL", "NULL", "OF", "OFFSET", "ON", "OR", "ORDER", "OUTER", "PLAN",
                    "PRAGMA", "PRIMARY", "QUERY", "RAISE", "RECURSIVE", "REFERENCES", "REGEXP", "REINDEX",
                    "RELEASE", "RENAME", "REPLACE", "RESTRICT", "RIGHT", "ROLLBACK", "ROW", "SAVEPOINT",
                    "SELECT", "SET", "TABLE", "TEMP", "TEMPORARY", "THEN", "TO", "TRANSACTION", "TRIGGER",
                    "UNION", "UNIQUE", "UPDATE", "USING", "VACUUM", "VALUES", "VIEW", "VIRTUAL", "WHEN",
                    "WHERE", "WITH", "WITHOUT"
                },
                _ => new HashSet<string>()
            };
        }

        private int GetMaxIdentifierLength(string provider)
        {
            return provider switch
            {
                "sqlserver" => 128,
                "postgresql" => 63,
                "mysql" => 64,
                "oracle" => 30,
                "sqlite" => 1000,
                _ => 128
            };
        }
    }
}