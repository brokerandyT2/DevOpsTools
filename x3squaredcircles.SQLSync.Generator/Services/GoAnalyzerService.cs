using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IGoAnalyzerService : ILanguageAnalyzer
    {
        Task<List<DiscoveredEntity>> ParseGoFilesAsync(string sourcePath, string trackAttribute);
        Task<List<DiscoveredEntity>> ParseGoFileAsync(string filePath, string trackAttribute);
    }

    public class GoAnalyzerService : IGoAnalyzerService
    {
        private readonly ILogger<GoAnalyzerService> _logger;

        public GoAnalyzerService(ILogger<GoAnalyzerService> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredEntity>> DiscoverEntitiesAsync(string sourcePath, string trackAttribute)
        {
            _logger.LogInformation("Analyzing Go packages in: {SourcePath}", sourcePath);

            var entities = new List<DiscoveredEntity>();

            try
            {
                var goEntities = await ParseGoFilesAsync(sourcePath, trackAttribute);
                entities.AddRange(goEntities);

                _logger.LogInformation("✓ Go analysis complete: {EntityCount} entities discovered", entities.Count);
                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Go entity discovery failed");
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure,
                    $"Go entity discovery failed: {ex.Message}", ex);
            }
        }

        public async Task<List<DiscoveredEntity>> ParseGoFilesAsync(string sourcePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var goFiles = Directory.GetFiles(sourcePath, "*.go", SearchOption.AllDirectories)
                    .Where(f => !IsTestFile(f))
                    .ToList();

                _logger.LogDebug("Found {FileCount} Go files", goFiles.Count);

                foreach (var file in goFiles)
                {
                    try
                    {
                        var fileEntities = await ParseGoFileAsync(file, trackAttribute);
                        entities.AddRange(fileEntities);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse Go file: {FileName}", file);
                    }
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Go files from: {SourcePath}", sourcePath);
                throw;
            }
        }

        public async Task<List<DiscoveredEntity>> ParseGoFileAsync(string filePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var content = await File.ReadAllTextAsync(filePath);

                // Find ALL structs with the tracking tag
                var structMatches = ExtractStructsWithTrackingTag(content, trackAttribute);

                if (!structMatches.Any())
                {
                    return entities;
                }

                // Process ALL matching structs in the file
                foreach (var structMatch in structMatches)
                {
                    var entity = new DiscoveredEntity
                    {
                        Name = structMatch.StructName,
                        FullName = ExtractFullStructName(content, structMatch.StructName),
                        Namespace = ExtractPackageName(content),
                        TableName = ExtractTableName(structMatch.StructTag, structMatch.StructName),
                        SchemaName = ExtractSchemaName(structMatch.StructTag),
                        SourceFile = filePath,
                        SourceLine = structMatch.LineNumber,
                        Properties = new List<DiscoveredProperty>(),
                        Indexes = new List<DiscoveredIndex>(),
                        Relationships = new List<DiscoveredRelationship>(),
                        Attributes = new Dictionary<string, object>
                        {
                            ["track_attribute"] = trackAttribute,
                            ["language"] = "go",
                            ["package"] = ExtractPackageName(content),
                            ["struct_tag"] = structMatch.StructTag
                        }
                    };

                    // Parse struct fields with tags
                    var fields = ExtractFieldsWithTags(structMatch.StructBody);
                    foreach (var field in fields)
                    {
                        var properties = ParseFieldTags(field, content);
                        entity.Properties.AddRange(properties);
                    }

                    entities.Add(entity);

                    _logger.LogTrace("Parsed Go entity: {EntityName} with {PropertyCount} properties",
                        entity.Name, entity.Properties.Count);
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Go file: {FilePath}", filePath);
                return entities;
            }
        }

        private List<GoStructMatch> ExtractStructsWithTrackingTag(string content, string trackAttribute)
        {
            var matches = new List<GoStructMatch>();

            // Convert tracking attribute to Go tag format
            var goTag = ConvertToGoTag(trackAttribute);

            // Find struct definitions with the tracking tag
            // Pattern: type StructName struct { ... } `sql:"...export..."`
            var structPattern = @"type\s+(\w+)\s+struct\s*\{([^}]*)\}\s*`([^`]*)`";
            var structMatches = Regex.Matches(content, structPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match structMatch in structMatches)
            {
                var structName = structMatch.Groups[1].Value;
                var structBody = structMatch.Groups[2].Value;
                var structTag = structMatch.Groups[3].Value;

                // Check if the struct tag contains the tracking indicator
                if (HasTrackingTag(structTag, goTag))
                {
                    var lineNumber = GetLineNumber(content, structMatch.Index);

                    matches.Add(new GoStructMatch
                    {
                        StructName = structName,
                        StructBody = structBody,
                        StructTag = structTag,
                        LineNumber = lineNumber,
                        StartIndex = structMatch.Index
                    });
                }
            }

            return matches;
        }

        private string ConvertToGoTag(string trackAttribute)
        {
            // Convert tracking attribute to Go tag convention
            if (trackAttribute.Equals("ExportToSQL", StringComparison.OrdinalIgnoreCase))
            {
                return "export";
            }

            // Convert PascalCase to lowercase for Go tags
            return trackAttribute.ToLowerInvariant();
        }

        private bool HasTrackingTag(string structTag, string goTag)
        {
            // Check if the sql tag contains the tracking indicator
            // Examples: `sql:"table:users;export"` or `sql:"export;table:users"`
            var sqlTagPattern = @"sql\s*:\s*""([^""]+)""";
            var sqlTagMatch = Regex.Match(structTag, sqlTagPattern, RegexOptions.IgnoreCase);

            if (sqlTagMatch.Success)
            {
                var sqlTagValue = sqlTagMatch.Groups[1].Value;
                return sqlTagValue.Split(';').Any(part => part.Trim().Equals(goTag, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        private List<GoFieldMatch> ExtractFieldsWithTags(string structBody)
        {
            var fields = new List<GoFieldMatch>();
            var lines = structBody.Split('\n');

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                    continue;

                // Match field definition: FieldName Type `tag:"value"`
                var fieldMatch = Regex.Match(trimmedLine, @"(\w+)\s+([\w\[\]\*]+)(?:\s*`([^`]+)`)?");
                if (fieldMatch.Success)
                {
                    fields.Add(new GoFieldMatch
                    {
                        Name = fieldMatch.Groups[1].Value,
                        Type = fieldMatch.Groups[2].Value,
                        Tag = fieldMatch.Groups[3].Success ? fieldMatch.Groups[3].Value : string.Empty,
                        FullDefinition = trimmedLine
                    });
                }
            }

            return fields;
        }

        private List<DiscoveredProperty> ParseFieldTags(GoFieldMatch field, string fullContent)
        {
            var properties = new List<DiscoveredProperty>();

            try
            {
                // Parse the sql tag to extract field configuration
                var sqlTagMatch = Regex.Match(field.Tag, @"sql\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);

                if (sqlTagMatch.Success)
                {
                    var sqlTagValue = sqlTagMatch.Groups[1].Value;
                    var tagParts = sqlTagValue.Split(';').Select(p => p.Trim()).ToList();

                    var property = new DiscoveredProperty
                    {
                        Name = field.Name,
                        Type = ExtractGoType(field.Type),
                        SqlType = "NVARCHAR",
                        IsNullable = IsNullableGoType(field.Type),
                        IsPrimaryKey = field.Name.Equals("ID", StringComparison.OrdinalIgnoreCase),
                        IsForeignKey = field.Name.EndsWith("ID", StringComparison.OrdinalIgnoreCase) &&
                                       !field.Name.Equals("ID", StringComparison.OrdinalIgnoreCase),
                        IsUnique = false,
                        IsIndexed = false,
                        Attributes = new Dictionary<string, object>
                        {
                            ["go_type"] = field.Type,
                            ["struct_tag"] = field.Tag
                        }
                    };

                    // Parse individual tag parts
                    foreach (var tagPart in tagParts)
                    {
                        ParseGoTagPart(tagPart, property);
                    }

                    // Infer SQL type from Go type if not explicitly set
                    if (property.SqlType == "NVARCHAR")
                    {
                        property.SqlType = MapGoTypeToSql(property.Type);
                    }

                    properties.Add(property);
                }
                else
                {
                    // Field without sql tag - create basic property
                    var property = new DiscoveredProperty
                    {
                        Name = field.Name,
                        Type = ExtractGoType(field.Type),
                        SqlType = MapGoTypeToSql(ExtractGoType(field.Type)),
                        IsNullable = IsNullableGoType(field.Type),
                        IsPrimaryKey = field.Name.Equals("ID", StringComparison.OrdinalIgnoreCase),
                        IsForeignKey = field.Name.EndsWith("ID", StringComparison.OrdinalIgnoreCase) &&
                                       !field.Name.Equals("ID", StringComparison.OrdinalIgnoreCase),
                        IsUnique = false,
                        IsIndexed = false,
                        Attributes = new Dictionary<string, object>
                        {
                            ["go_type"] = field.Type,
                            ["struct_tag"] = field.Tag
                        }
                    };

                    properties.Add(property);
                }

                return properties;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse field: {FieldName}", field.Name);
                return properties;
            }
        }

        private void ParseGoTagPart(string tagPart, DiscoveredProperty property)
        {
            // Parse different tag formats:
            // type:NVARCHAR(100)
            // constraints:PRIMARY_KEY,NOT_NULL
            // foreign_key:User
            // column:user_name

            if (tagPart.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                var typeValue = tagPart.Substring(5);
                ParseSqlType(typeValue, property);
            }
            else if (tagPart.StartsWith("constraints:", StringComparison.OrdinalIgnoreCase))
            {
                var constraintsValue = tagPart.Substring(12);
                ParseConstraints(constraintsValue, property);
            }
            else if (tagPart.StartsWith("foreign_key:", StringComparison.OrdinalIgnoreCase))
            {
                var foreignKeyValue = tagPart.Substring(12);
                ParseForeignKey(foreignKeyValue, property);
            }
            else if (tagPart.StartsWith("column:", StringComparison.OrdinalIgnoreCase))
            {
                var columnValue = tagPart.Substring(7);
                property.Attributes["column_name"] = columnValue;
            }
            else if (tagPart.StartsWith("table:", StringComparison.OrdinalIgnoreCase))
            {
                // Table name is handled at struct level
            }
            else if (tagPart.StartsWith("schema:", StringComparison.OrdinalIgnoreCase))
            {
                // Schema name is handled at struct level
            }
            else if (tagPart.Equals("export", StringComparison.OrdinalIgnoreCase))
            {
                // Export marker is handled at struct level
            }
        }

        private void ParseSqlType(string typeValue, DiscoveredProperty property)
        {
            // Parse type:NVARCHAR(100) or type:DECIMAL(10,2)
            var typeMatch = Regex.Match(typeValue, @"(\w+)(?:\((\d+)(?:,(\d+))?\))?", RegexOptions.IgnoreCase);
            if (typeMatch.Success)
            {
                property.SqlType = typeMatch.Groups[1].Value.ToUpperInvariant();

                if (typeMatch.Groups[2].Success && int.TryParse(typeMatch.Groups[2].Value, out var length))
                {
                    if (property.SqlType == "DECIMAL" || property.SqlType == "NUMERIC")
                    {
                        property.Precision = length;
                        if (typeMatch.Groups[3].Success && int.TryParse(typeMatch.Groups[3].Value, out var scale))
                        {
                            property.Scale = scale;
                        }
                    }
                    else
                    {
                        property.MaxLength = length;
                    }
                }
            }
        }

        private void ParseConstraints(string constraintsValue, DiscoveredProperty property)
        {
            // Parse constraints:PRIMARY_KEY,NOT_NULL,UNIQUE
            var constraints = constraintsValue.Split(',').Select(c => c.Trim()).ToList();

            foreach (var constraint in constraints)
            {
                switch (constraint.ToUpperInvariant())
                {
                    case "PRIMARY_KEY":
                        property.IsPrimaryKey = true;
                        break;
                    case "NOT_NULL":
                        property.IsNullable = false;
                        break;
                    case "UNIQUE":
                        property.IsUnique = true;
                        break;
                    case "IDENTITY":
                        property.Attributes["is_identity"] = true;
                        break;
                }
            }
        }

        private void ParseForeignKey(string foreignKeyValue, DiscoveredProperty property)
        {
            // Parse foreign_key:User or foreign_key:User.Id
            var parts = foreignKeyValue.Split('.');

            property.IsForeignKey = true;
            property.Attributes["foreign_key_referenced_entity"] = parts[0];
            property.Attributes["foreign_key_referenced_column"] = parts.Length > 1 ? parts[1] : "ID";
        }

        private string ExtractPackageName(string content)
        {
            var packageMatch = Regex.Match(content, @"package\s+(\w+)", RegexOptions.IgnoreCase);
            return packageMatch.Success ? packageMatch.Groups[1].Value : "main";
        }

        private string ExtractFullStructName(string content, string structName)
        {
            var packageName = ExtractPackageName(content);
            return $"{packageName}.{structName}";
        }

        private string ExtractTableName(string structTag, string structName)
        {
            // Look for table:table_name in struct tag
            var tableMatch = Regex.Match(structTag, @"table\s*:\s*(\w+)", RegexOptions.IgnoreCase);
            if (tableMatch.Success)
            {
                return tableMatch.Groups[1].Value;
            }

            // Default to snake_case struct name
            return ToSnakeCase(structName);
        }

        private string ExtractSchemaName(string structTag)
        {
            // Look for schema:schema_name in struct tag
            var schemaMatch = Regex.Match(structTag, @"schema\s*:\s*(\w+)", RegexOptions.IgnoreCase);
            if (schemaMatch.Success)
            {
                return schemaMatch.Groups[1].Value;
            }

            return "dbo"; // Default schema
        }

        private int GetLineNumber(string content, int characterIndex)
        {
            return content.Substring(0, characterIndex).Count(c => c == '\n') + 1;
        }

        private string ExtractGoType(string goType)
        {
            // Handle pointers and slices
            var cleanType = goType.Replace("*", "").Replace("[]", "").Trim();

            // Handle package qualified types
            if (cleanType.Contains("."))
            {
                var parts = cleanType.Split('.');
                cleanType = parts.Last();
            }

            return cleanType;
        }

        private bool IsNullableGoType(string goType)
        {
            // Pointer types are nullable in Go
            return goType.StartsWith("*") || goType.Contains("interface{}");
        }

        private string MapGoTypeToSql(string goType)
        {
            return goType.ToLowerInvariant() switch
            {
                "string" => "NVARCHAR",
                "int" or "int32" => "INT",
                "int64" => "BIGINT",
                "int16" => "SMALLINT",
                "int8" => "TINYINT",
                "uint" or "uint32" => "INT",
                "uint64" => "BIGINT",
                "uint16" => "SMALLINT",
                "uint8" or "byte" => "TINYINT",
                "float32" => "REAL",
                "float64" => "FLOAT",
                "bool" => "BIT",
                "time" => "DATETIME2",
                "[]byte" => "VARBINARY",
                "uuid" => "UNIQUEIDENTIFIER",
                _ => "NVARCHAR"
            };
        }

        private string ToSnakeCase(string pascalCase)
        {
            return Regex.Replace(pascalCase, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        }

        private bool IsTestFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            return fileName.EndsWith("_test.go");
        }

        // Helper classes for parsing
        private class GoStructMatch
        {
            public string StructName { get; set; } = string.Empty;
            public string StructBody { get; set; } = string.Empty;
            public string StructTag { get; set; } = string.Empty;
            public int LineNumber { get; set; }
            public int StartIndex { get; set; }
        }

        private class GoFieldMatch
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Tag { get; set; } = string.Empty;
            public string FullDefinition { get; set; } = string.Empty;
        }
    }
}