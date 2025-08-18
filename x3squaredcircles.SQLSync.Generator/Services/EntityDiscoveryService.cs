using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IEntityDiscoveryService
    {
        Task<EntityDiscoveryResult> DiscoverEntitiesAsync(SqlSchemaConfiguration config);
    }

    public class EntityDiscoveryService : IEntityDiscoveryService
    {
        private readonly ILanguageAnalyzerFactory _languageAnalyzerFactory;
        private readonly ILogger<EntityDiscoveryService> _logger;
        private readonly string _workingDirectory = "/src";

        public EntityDiscoveryService(
            ILanguageAnalyzerFactory languageAnalyzerFactory,
            ILogger<EntityDiscoveryService> logger)
        {
            _languageAnalyzerFactory = languageAnalyzerFactory;
            _logger = logger;
        }

        public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogInformation("Starting entity discovery for language: {Language}, attribute: {TrackAttribute}",
                    config.Language.GetSelectedLanguage().ToUpperInvariant(), config.TrackAttribute);

                var language = config.Language.GetSelectedLanguage();
                var analyzer = await _languageAnalyzerFactory.GetAnalyzerAsync(language);

                // Determine source paths to analyze
                var sourcePaths = await DetermineSourcePathsAsync(config);
                _logger.LogInformation("Analyzing {PathCount} source paths", sourcePaths.Count);

                var allEntities = new List<DiscoveredEntity>();

                foreach (var sourcePath in sourcePaths)
                {
                    _logger.LogDebug("Analyzing path: {SourcePath}", sourcePath);

                    var entities = await analyzer.DiscoverEntitiesAsync(sourcePath, config.TrackAttribute);
                    allEntities.AddRange(entities);

                    _logger.LogDebug("Found {EntityCount} entities in {SourcePath}", entities.Count, sourcePath);
                }

                // Post-process and validate entities
                var processedEntities = await PostProcessEntitiesAsync(allEntities, config);

                var result = new EntityDiscoveryResult
                {
                    Entities = processedEntities,
                    Language = language,
                    TrackAttribute = config.TrackAttribute,
                    DiscoveryTime = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object>
                    {
                        ["source_paths"] = sourcePaths,
                        ["total_entities"] = processedEntities.Count,
                        ["language"] = language,
                        ["track_attribute"] = config.TrackAttribute,
                        ["ignore_export_attribute"] = config.SchemaAnalysis.IgnoreExportAttribute
                    }
                };

                if (processedEntities.Count == 0)
                {
                    if (config.SchemaAnalysis.IgnoreExportAttribute)
                    {
                        _logger.LogWarning("No entities found - this may be expected if IGNORE_EXPORT_ATTRIBUTE=true");
                    }
                    else
                    {
                        var errorMessage = $"No entities found with attribute '{config.TrackAttribute}'. Ensure entities are marked with the tracking attribute and assemblies are built.";
                        throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure, errorMessage);
                    }
                }

                _logger.LogInformation("✓ Entity discovery completed: {EntityCount} entities discovered", processedEntities.Count);

                // Log summary by type
                var entitySummary = processedEntities
                    .GroupBy(e => GetEntityType(e))
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var summary in entitySummary)
                {
                    _logger.LogInformation("  - {EntityType}: {Count}", summary.Key, summary.Value);
                }

                return result;
            }
            catch (SqlSchemaException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Entity discovery failed");
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure,
                    $"Entity discovery failed: {ex.Message}", ex);
            }
        }

        private async Task<List<string>> DetermineSourcePathsAsync(SqlSchemaConfiguration config)
        {
            var sourcePaths = new List<string>();

            try
            {
                // 1. Check for explicitly configured assembly paths
                if (!string.IsNullOrEmpty(config.SchemaAnalysis.AssemblyPaths))
                {
                    var configuredPaths = config.SchemaAnalysis.AssemblyPaths.Split(':', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var path in configuredPaths)
                    {
                        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_workingDirectory, path);
                        if (Directory.Exists(fullPath) || File.Exists(fullPath))
                        {
                            sourcePaths.Add(fullPath);
                        }
                        else
                        {
                            _logger.LogWarning("Configured assembly path not found: {Path}", fullPath);
                        }
                    }
                }

                // 2. Check for build output path
                if (!string.IsNullOrEmpty(config.SchemaAnalysis.BuildOutputPath))
                {
                    var buildPath = Path.IsPathRooted(config.SchemaAnalysis.BuildOutputPath)
                        ? config.SchemaAnalysis.BuildOutputPath
                        : Path.Combine(_workingDirectory, config.SchemaAnalysis.BuildOutputPath);

                    if (Directory.Exists(buildPath))
                    {
                        sourcePaths.Add(buildPath);
                    }
                    else
                    {
                        _logger.LogWarning("Build output path not found: {Path}", buildPath);
                    }
                }

                // 3. Fallback to common build directories if no paths configured
                if (!sourcePaths.Any())
                {
                    var commonPaths = await GetCommonBuildPathsAsync(config.Language.GetSelectedLanguage());
                    foreach (var path in commonPaths)
                    {
                        var fullPath = Path.Combine(_workingDirectory, path);
                        if (Directory.Exists(fullPath))
                        {
                            sourcePaths.Add(fullPath);
                        }
                    }
                }

                // 4. Ultimate fallback to working directory
                if (!sourcePaths.Any())
                {
                    _logger.LogWarning("No specific build paths found, falling back to working directory");
                    sourcePaths.Add(_workingDirectory);
                }

                return sourcePaths.Distinct().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to determine source paths");
                throw;
            }
        }

        private async Task<List<string>> GetCommonBuildPathsAsync(string language)
        {
            return language switch
            {
                "csharp" => new List<string>
                {
                    "bin/Release",
                    "bin/Debug",
                    "bin/Release/net8.0",
                    "bin/Release/net9.0",
                    "bin/Debug/net8.0",
                    "bin/Debug/net9.0"
                },
                "java" => new List<string>
                {
                    "target/classes",
                    "build/classes/java/main",
                    "out/production/classes",
                    "build/libs"
                },
                "python" => new List<string>
                {
                    ".",
                    "src",
                    "app",
                    "python"
                },
                "javascript" => new List<string>
                {
                    ".",
                    "src",
                    "app",
                    "lib",
                    "dist"
                },
                "typescript" => new List<string>
                {
                    "dist",
                    "build",
                    "out",
                    "src",
                    "lib"
                },
                "go" => new List<string>
                {
                    ".",
                    "cmd",
                    "internal",
                    "pkg"
                },
                _ => new List<string> { "." }
            };
        }

        private async Task<List<DiscoveredEntity>> PostProcessEntitiesAsync(List<DiscoveredEntity> entities, SqlSchemaConfiguration config)
        {
            var processedEntities = new List<DiscoveredEntity>();

            foreach (var entity in entities)
            {
                try
                {
                    // Validate entity has required properties
                    if (string.IsNullOrEmpty(entity.Name))
                    {
                        _logger.LogWarning("Skipping entity with empty name from {SourceFile}:{SourceLine}",
                            entity.SourceFile, entity.SourceLine);
                        continue;
                    }

                    // Ensure table name is set
                    if (string.IsNullOrEmpty(entity.TableName))
                    {
                        entity.TableName = GenerateTableName(entity.Name);
                    }

                    // Ensure schema name is set if needed
                    if (string.IsNullOrEmpty(entity.SchemaName) && !string.IsNullOrEmpty(config.Database.Schema))
                    {
                        entity.SchemaName = config.Database.Schema;
                    }

                    // Validate properties
                    await ValidateEntityPropertiesAsync(entity, config);

                    // Generate indexes if enabled
                    if (config.SchemaAnalysis.GenerateIndexes)
                    {
                        await GenerateIndexesAsync(entity, config);
                    }

                    // Validate relationships
                    await ValidateRelationshipsAsync(entity, config);

                    processedEntities.Add(entity);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process entity {EntityName}, skipping", entity.Name);
                }
            }

            // Check for duplicate table names - create custom comparer for anonymous type
            var duplicates = processedEntities
                .GroupBy(e => $"{e.SchemaName ?? ""}.{e.TableName}", StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Any())
            {
                var duplicateNames = string.Join(", ", duplicates.Select(g => g.Key));
                _logger.LogWarning("Duplicate table names detected: {DuplicateNames}", duplicateNames);
            }

            return processedEntities;
        }

        private string GenerateTableName(string entityName)
        {
            // Convert PascalCase to snake_case or keep as-is based on convention
            // For now, keep the entity name as table name
            return entityName;
        }

        private async Task ValidateEntityPropertiesAsync(DiscoveredEntity entity, SqlSchemaConfiguration config)
        {
            var validProperties = new List<DiscoveredProperty>();

            foreach (var property in entity.Properties)
            {
                try
                {
                    // Validate property has required fields
                    if (string.IsNullOrEmpty(property.Name) || string.IsNullOrEmpty(property.Type))
                    {
                        _logger.LogWarning("Skipping invalid property in entity {EntityName}: missing name or type", entity.Name);
                        continue;
                    }

                    // Ensure SQL type is mapped
                    if (string.IsNullOrEmpty(property.SqlType))
                    {
                        property.SqlType = MapToSqlType(property.Type, config.Database.GetSelectedProvider());
                    }

                    validProperties.Add(property);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate property {PropertyName} in entity {EntityName}",
                        property.Name, entity.Name);
                }
            }

            entity.Properties = validProperties;

            // Ensure entity has at least one property
            if (!entity.Properties.Any())
            {
                throw new InvalidOperationException($"Entity {entity.Name} has no valid properties");
            }

            // Ensure entity has a primary key or add one
            if (!entity.Properties.Any(p => p.IsPrimaryKey))
            {
                _logger.LogDebug("Adding default primary key 'Id' to entity {EntityName}", entity.Name);
                entity.Properties.Insert(0, new DiscoveredProperty
                {
                    Name = "Id",
                    Type = "int",
                    SqlType = MapToSqlType("int", config.Database.GetSelectedProvider()),
                    IsPrimaryKey = true,
                    IsNullable = false,
                    Attributes = new Dictionary<string, object>
                    {
                        ["generated"] = true,
                        ["auto_increment"] = true
                    }
                });
            }
        }

        private string MapToSqlType(string codeType, string databaseProvider)
        {
            return databaseProvider switch
            {
                "sqlserver" => MapToSqlServerType(codeType),
                "postgresql" => MapToPostgreSqlType(codeType),
                "mysql" => MapToMySqlType(codeType),
                "oracle" => MapToOracleType(codeType),
                "sqlite" => MapToSqliteType(codeType),
                _ => "VARCHAR(255)"
            };
        }

        private string MapToSqlServerType(string codeType)
        {
            return codeType.ToLowerInvariant() switch
            {
                "int" or "int32" => "INT",
                "long" or "int64" => "BIGINT",
                "short" or "int16" => "SMALLINT",
                "byte" => "TINYINT",
                "bool" or "boolean" => "BIT",
                "string" => "NVARCHAR(255)",
                "datetime" => "DATETIME2",
                "decimal" => "DECIMAL(18,2)",
                "double" => "FLOAT",
                "float" => "REAL",
                "guid" or "uuid" => "UNIQUEIDENTIFIER",
                _ => "NVARCHAR(255)"
            };
        }

        private string MapToPostgreSqlType(string codeType)
        {
            return codeType.ToLowerInvariant() switch
            {
                "int" or "int32" => "INTEGER",
                "long" or "int64" => "BIGINT",
                "short" or "int16" => "SMALLINT",
                "byte" => "SMALLINT",
                "bool" or "boolean" => "BOOLEAN",
                "string" => "VARCHAR(255)",
                "datetime" => "TIMESTAMP",
                "decimal" => "DECIMAL(18,2)",
                "double" => "DOUBLE PRECISION",
                "float" => "REAL",
                "guid" or "uuid" => "UUID",
                _ => "VARCHAR(255)"
            };
        }

        private string MapToMySqlType(string codeType)
        {
            return codeType.ToLowerInvariant() switch
            {
                "int" or "int32" => "INT",
                "long" or "int64" => "BIGINT",
                "short" or "int16" => "SMALLINT",
                "byte" => "TINYINT",
                "bool" or "boolean" => "BOOLEAN",
                "string" => "VARCHAR(255)",
                "datetime" => "DATETIME",
                "decimal" => "DECIMAL(18,2)",
                "double" => "DOUBLE",
                "float" => "FLOAT",
                "guid" or "uuid" => "CHAR(36)",
                _ => "VARCHAR(255)"
            };
        }

        private string MapToOracleType(string codeType)
        {
            return codeType.ToLowerInvariant() switch
            {
                "int" or "int32" => "NUMBER(10,0)",
                "long" or "int64" => "NUMBER(19,0)",
                "short" or "int16" => "NUMBER(5,0)",
                "byte" => "NUMBER(3,0)",
                "bool" or "boolean" => "NUMBER(1,0)",
                "string" => "VARCHAR2(255)",
                "datetime" => "TIMESTAMP",
                "decimal" => "NUMBER(18,2)",
                "double" => "BINARY_DOUBLE",
                "float" => "BINARY_FLOAT",
                "guid" or "uuid" => "RAW(16)",
                _ => "VARCHAR2(255)"
            };
        }

        private string MapToSqliteType(string codeType)
        {
            return codeType.ToLowerInvariant() switch
            {
                "int" or "int32" or "long" or "int64" or "short" or "int16" or "byte" => "INTEGER",
                "bool" or "boolean" => "INTEGER",
                "string" => "TEXT",
                "datetime" => "TEXT",
                "decimal" or "double" or "float" => "REAL",
                "guid" or "uuid" => "TEXT",
                _ => "TEXT"
            };
        }

        private async Task GenerateIndexesAsync(DiscoveredEntity entity, SqlSchemaConfiguration config)
        {
            // Generate indexes for foreign keys if enabled
            if (config.SchemaAnalysis.GenerateFkIndexes)
            {
                foreach (var property in entity.Properties.Where(p => p.IsForeignKey))
                {
                    var indexName = $"IX_{entity.TableName}_{property.Name}";
                    if (!entity.Indexes.Any(i => i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase)))
                    {
                        entity.Indexes.Add(new DiscoveredIndex
                        {
                            Name = indexName,
                            Columns = new List<string> { property.Name },
                            IsUnique = false,
                            IsClustered = false,
                            Attributes = new Dictionary<string, object>
                            {
                                ["generated"] = true,
                                ["foreign_key_index"] = true
                            }
                        });
                    }
                }
            }

            // Generate indexes for unique properties
            foreach (var property in entity.Properties.Where(p => p.IsUnique && !p.IsPrimaryKey))
            {
                var indexName = $"UX_{entity.TableName}_{property.Name}";
                if (!entity.Indexes.Any(i => i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase)))
                {
                    entity.Indexes.Add(new DiscoveredIndex
                    {
                        Name = indexName,
                        Columns = new List<string> { property.Name },
                        IsUnique = true,
                        IsClustered = false,
                        Attributes = new Dictionary<string, object>
                        {
                            ["generated"] = true,
                            ["unique_index"] = true
                        }
                    });
                }
            }
        }

        private async Task ValidateRelationshipsAsync(DiscoveredEntity entity, SqlSchemaConfiguration config)
        {
            var validRelationships = new List<DiscoveredRelationship>();

            foreach (var relationship in entity.Relationships)
            {
                try
                {
                    // Validate relationship has required fields
                    if (string.IsNullOrEmpty(relationship.Name) ||
                        string.IsNullOrEmpty(relationship.ReferencedEntity) ||
                        !relationship.ForeignKeyColumns.Any())
                    {
                        _logger.LogWarning("Skipping invalid relationship in entity {EntityName}: missing required fields", entity.Name);
                        continue;
                    }

                    // Set referenced table name if not set
                    if (string.IsNullOrEmpty(relationship.ReferencedTable))
                    {
                        relationship.ReferencedTable = relationship.ReferencedEntity;
                    }

                    // Validate foreign key columns exist in entity
                    var missingColumns = relationship.ForeignKeyColumns
                        .Where(col => !entity.Properties.Any(p => p.Name.Equals(col, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (missingColumns.Any())
                    {
                        _logger.LogWarning("Relationship {RelationshipName} in entity {EntityName} references non-existent columns: {MissingColumns}",
                            relationship.Name, entity.Name, string.Join(", ", missingColumns));
                        continue;
                    }

                    validRelationships.Add(relationship);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate relationship {RelationshipName} in entity {EntityName}",
                        relationship.Name, entity.Name);
                }
            }

            entity.Relationships = validRelationships;
        }

        private string GetEntityType(DiscoveredEntity entity)
        {
            if (entity.Attributes.ContainsKey("entity_type"))
            {
                return entity.Attributes["entity_type"].ToString() ?? "Entity";
            }

            // Infer type from entity name or properties
            if (entity.Name.EndsWith("View", StringComparison.OrdinalIgnoreCase))
            {
                return "View";
            }

            if (entity.Name.Contains("Junction", StringComparison.OrdinalIgnoreCase) ||
                entity.Name.Contains("Link", StringComparison.OrdinalIgnoreCase))
            {
                return "Junction Table";
            }

            return "Entity";
        }
    }
}