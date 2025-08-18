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
    public interface IJavaAnalyzerService : ILanguageAnalyzer
    {
        Task<List<DiscoveredEntity>> ParseJavaFilesAsync(string sourcePath, string trackAttribute);
        Task<List<DiscoveredEntity>> ParseJavaFileAsync(string filePath, string trackAttribute);
    }

    public class JavaAnalyzerService : IJavaAnalyzerService
    {
        private readonly ILogger<JavaAnalyzerService> _logger;

        public JavaAnalyzerService(ILogger<JavaAnalyzerService> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredEntity>> DiscoverEntitiesAsync(string sourcePath, string trackAttribute)
        {
            _logger.LogInformation("Analyzing Java classes in: {SourcePath}", sourcePath);

            var entities = new List<DiscoveredEntity>();

            try
            {
                var javaEntities = await ParseJavaFilesAsync(sourcePath, trackAttribute);
                entities.AddRange(javaEntities);

                _logger.LogInformation("✓ Java analysis complete: {EntityCount} entities discovered", entities.Count);
                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Java entity discovery failed");
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure,
                    $"Java entity discovery failed: {ex.Message}", ex);
            }
        }

        public async Task<List<DiscoveredEntity>> ParseJavaFilesAsync(string sourcePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var javaFiles = Directory.GetFiles(sourcePath, "*.java", SearchOption.AllDirectories)
                    .Where(f => !IsTestFile(f))
                    .ToList();

                _logger.LogDebug("Found {FileCount} Java files", javaFiles.Count);

                foreach (var file in javaFiles)
                {
                    try
                    {
                        var fileEntities = await ParseJavaFileAsync(file, trackAttribute);
                        entities.AddRange(fileEntities);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse Java file: {FileName}", file);
                    }
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Java files from: {SourcePath}", sourcePath);
                throw;
            }
        }

        public async Task<List<DiscoveredEntity>> ParseJavaFileAsync(string filePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var content = await File.ReadAllTextAsync(filePath);

                // Find ALL classes with the tracking attribute in this file
                var classMatches = ExtractClassesWithAnnotation(content, trackAttribute);

                if (!classMatches.Any())
                {
                    return entities; // Return empty list instead of null
                }

                // Process ALL matching classes in the file
                foreach (var classMatch in classMatches)
                {
                    var entity = new DiscoveredEntity
                    {
                        Name = classMatch.ClassName,
                        FullName = ExtractFullClassName(content, classMatch.ClassName),
                        Namespace = ExtractPackageName(content),
                        TableName = ExtractTableNameFromClass(classMatch.ClassContent),
                        SchemaName = ExtractSchemaNameFromClass(classMatch.ClassContent),
                        SourceFile = filePath,
                        SourceLine = classMatch.LineNumber,
                        Properties = new List<DiscoveredProperty>(),
                        Indexes = new List<DiscoveredIndex>(),
                        Relationships = new List<DiscoveredRelationship>(),
                        Attributes = new Dictionary<string, object>
                        {
                            ["track_attribute"] = trackAttribute,
                            ["language"] = "java",
                            ["package"] = ExtractPackageName(content)
                        }
                    };

                    // Parse fields/properties
                    var fields = ExtractFieldsWithAnnotations(classMatch.ClassContent);
                    foreach (var field in fields)
                    {
                        var property = ParseFieldAnnotations(field, content);
                        if (property != null)
                        {
                            entity.Properties.Add(property);
                        }
                    }

                    // Parse class-level annotations for indexes
                    ParseClassAnnotations(classMatch.ClassContent, entity);

                    entities.Add(entity);

                    _logger.LogTrace("Parsed Java entity: {EntityName} with {PropertyCount} properties",
                        entity.Name, entity.Properties.Count);
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Java file: {FilePath}", filePath);
                return entities; // Return empty list instead of null
            }
        }

        private List<JavaClassMatch> ExtractClassesWithAnnotation(string content, string trackAttribute)
        {
            var matches = new List<JavaClassMatch>();

            // Use the actual tracking attribute passed in
            var annotationPattern = $@"@{Regex.Escape(trackAttribute)}\b";
            var classPattern = @"public\s+class\s+(\w+)(?:\s+extends\s+\w+)?(?:\s+implements\s+[\w\s,]+)?\s*\{";

            // Find annotation positions
            var annotationMatches = Regex.Matches(content, annotationPattern, RegexOptions.IgnoreCase);

            foreach (Match annotationMatch in annotationMatches)
            {
                // Look for class definition after annotation
                var searchStart = annotationMatch.Index;
                var remainingContent = content.Substring(searchStart);

                var classMatch = Regex.Match(remainingContent, classPattern, RegexOptions.IgnoreCase);
                if (classMatch.Success)
                {
                    var className = classMatch.Groups[1].Value;
                    var classStartIndex = searchStart + classMatch.Index;
                    var classContent = ExtractClassBody(content, classStartIndex);
                    var lineNumber = GetLineNumber(content, classStartIndex);

                    matches.Add(new JavaClassMatch
                    {
                        ClassName = className,
                        ClassContent = classContent,
                        LineNumber = lineNumber,
                        StartIndex = classStartIndex
                    });
                }
            }

            return matches;
        }

        private string ExtractClassBody(string content, int classStartIndex)
        {
            // Find the opening brace
            var openBraceIndex = content.IndexOf('{', classStartIndex);
            if (openBraceIndex == -1) return string.Empty;

            // Find the matching closing brace
            var braceCount = 0;
            var index = openBraceIndex;

            while (index < content.Length)
            {
                if (content[index] == '{') braceCount++;
                if (content[index] == '}') braceCount--;

                if (braceCount == 0)
                {
                    return content.Substring(classStartIndex, index - classStartIndex + 1);
                }
                index++;
            }

            return content.Substring(classStartIndex);
        }

        private int GetLineNumber(string content, int characterIndex)
        {
            return content.Substring(0, characterIndex).Count(c => c == '\n') + 1;
        }

        private string ExtractTableNameFromClass(string classContent)
        {
            // Look for @Table(name = "tablename") or @SqlTable("tablename")
            var tableMatch = Regex.Match(classContent, @"@(?:Table|SqlTable)\s*\(\s*(?:name\s*=\s*)?[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (tableMatch.Success)
            {
                return tableMatch.Groups[1].Value;
            }

            // Extract class name from class content
            var classMatch = Regex.Match(classContent, @"class\s+(\w+)", RegexOptions.IgnoreCase);
            return classMatch.Success ? classMatch.Groups[1].Value : "Unknown";
        }

        private string ExtractSchemaNameFromClass(string classContent)
        {
            // Look for schema in @Table or @SqlSchema
            var schemaMatch = Regex.Match(classContent, @"@(?:Table|SqlSchema)\s*\([^)]*schema\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (schemaMatch.Success)
            {
                return schemaMatch.Groups[1].Value;
            }

            return "dbo"; // Default schema
        }

        private bool HasTrackingAttribute(string content, string trackAttribute)
        {
            var pattern = $@"@{Regex.Escape(trackAttribute)}\b";
            return Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase);
        }

        private string ExtractClassName(string content)
        {
            var match = Regex.Match(content, @"public\s+class\s+(\w+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "Unknown";
        }

        private string ExtractFullClassName(string content)
        {
            var packageName = ExtractPackageName(content);
            var className = ExtractClassName(content);
            return string.IsNullOrEmpty(packageName) ? className : $"{packageName}.{className}";
        }

        private string ExtractPackageName(string content)
        {
            var match = Regex.Match(content, @"package\s+([\w\.]+)\s*;");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private string ExtractTableName(string content)
        {
            // Look for @Table(name = "tablename") or @SqlTable("tablename")
            var tableMatch = Regex.Match(content, @"@(?:Table|SqlTable)\s*\(\s*(?:name\s*=\s*)?[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (tableMatch.Success)
            {
                return tableMatch.Groups[1].Value;
            }

            // Default to class name
            return ExtractClassName(content);
        }

        private string ExtractSchemaName(string content)
        {
            // Look for schema in @Table or @SqlSchema
            var schemaMatch = Regex.Match(content, @"@(?:Table|SqlSchema)\s*\([^)]*schema\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (schemaMatch.Success)
            {
                return schemaMatch.Groups[1].Value;
            }

            return "dbo"; // Default schema
        }

        private int GetClassLineNumber(string content)
        {
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], @"public\s+class\s+\w+", RegexOptions.IgnoreCase))
                {
                    return i + 1;
                }
            }
            return 1;
        }

        private List<string> ExtractFieldsWithAnnotations(string content)
        {
            var fields = new List<string>();

            // Remove comments and strings to avoid false matches
            var cleanContent = RemoveCommentsAndStrings(content);

            // Find field declarations with annotations
            var fieldPattern = @"(?:@\w+(?:\([^)]*\))?[\s\n]*)*(?:private|protected|public)?\s+(?:static\s+)?(?:final\s+)?[\w<>\[\],\s]+\s+(\w+)\s*[;=]";
            var matches = Regex.Matches(cleanContent, fieldPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                // Get the full field declaration including annotations
                var fieldStart = FindFieldStart(content, match.Index);
                var fieldEnd = match.Index + match.Length;
                var fieldDeclaration = content.Substring(fieldStart, fieldEnd - fieldStart);

                fields.Add(fieldDeclaration);
            }

            return fields;
        }

        private DiscoveredProperty? ParseFieldAnnotations(string fieldText, string fullContent)
        {
            try
            {
                var property = new DiscoveredProperty
                {
                    Name = ExtractFieldName(fieldText),
                    Type = ExtractFieldType(fieldText),
                    SqlType = "NVARCHAR",
                    IsNullable = true,
                    IsPrimaryKey = false,
                    IsForeignKey = false,
                    IsUnique = false,
                    IsIndexed = false,
                    Attributes = new Dictionary<string, object>()
                };

                // Parse @SqlType annotation
                ParseSqlTypeAnnotation(fieldText, property);

                // Parse @SqlConstraints annotation
                ParseSqlConstraintsAnnotation(fieldText, property);

                // Parse @SqlForeignKeyTo annotation
                ParseSqlForeignKeyAnnotation(fieldText, property);

                // Parse standard JPA annotations
                ParseJpaAnnotations(fieldText, property);

                // Infer SQL type from Java type if not explicitly set
                if (property.SqlType == "NVARCHAR")
                {
                    property.SqlType = MapJavaTypeToSql(property.Type);
                }

                return property;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse field: {FieldText}", fieldText);
                return null;
            }
        }

        private void ParseSqlTypeAnnotation(string fieldText, DiscoveredProperty property)
        {
            // @SqlType(SqlDataType.NVARCHAR, length = 100)
            var sqlTypeMatch = Regex.Match(fieldText, @"@SqlType\s*\(\s*SqlDataType\.(\w+)(?:\s*,\s*length\s*=\s*(\d+))?(?:\s*,\s*precision\s*=\s*(\d+))?(?:\s*,\s*scale\s*=\s*(\d+))?\s*\)", RegexOptions.IgnoreCase);
            if (sqlTypeMatch.Success)
            {
                property.SqlType = sqlTypeMatch.Groups[1].Value.ToUpperInvariant();

                if (sqlTypeMatch.Groups[2].Success && int.TryParse(sqlTypeMatch.Groups[2].Value, out var length))
                {
                    property.MaxLength = length;
                }

                if (sqlTypeMatch.Groups[3].Success && int.TryParse(sqlTypeMatch.Groups[3].Value, out var precision))
                {
                    property.Precision = precision;
                }

                if (sqlTypeMatch.Groups[4].Success && int.TryParse(sqlTypeMatch.Groups[4].Value, out var scale))
                {
                    property.Scale = scale;
                }
            }
        }

        private void ParseSqlConstraintsAnnotation(string fieldText, DiscoveredProperty property)
        {
            // @SqlConstraints({SqlConstraint.NOT_NULL, SqlConstraint.UNIQUE})
            var constraintsMatch = Regex.Match(fieldText, @"@SqlConstraints\s*\(\s*\{([^}]+)\}\s*\)", RegexOptions.IgnoreCase);
            if (constraintsMatch.Success)
            {
                var constraintsText = constraintsMatch.Groups[1].Value;
                var constraints = Regex.Matches(constraintsText, @"SqlConstraint\.(\w+)", RegexOptions.IgnoreCase);

                foreach (Match constraint in constraints)
                {
                    var constraintName = constraint.Groups[1].Value.ToUpperInvariant();
                    switch (constraintName)
                    {
                        case "NOT_NULL":
                            property.IsNullable = false;
                            break;
                        case "UNIQUE":
                            property.IsUnique = true;
                            break;
                        case "PRIMARY_KEY":
                            property.IsPrimaryKey = true;
                            break;
                        case "IDENTITY":
                            property.Attributes["is_identity"] = true;
                            break;
                    }
                }
            }
        }

        private void ParseSqlForeignKeyAnnotation(string fieldText, DiscoveredProperty property)
        {
            // @SqlForeignKeyTo(referencedEntity = User.class, referencedProperty = "id", onDelete = ForeignKeyAction.CASCADE)
            var fkMatch = Regex.Match(fieldText,
                @"@SqlForeignKeyTo\s*\(\s*referencedEntity\s*=\s*(\w+)\.class(?:\s*,\s*referencedProperty\s*=\s*[""']([^""']+)[""'])?(?:\s*,\s*onDelete\s*=\s*ForeignKeyAction\.(\w+))?(?:\s*,\s*onUpdate\s*=\s*ForeignKeyAction\.(\w+))?(?:\s*,\s*name\s*=\s*[""']([^""']+)[""'])?\s*\)",
                RegexOptions.IgnoreCase);

            if (fkMatch.Success)
            {
                property.IsForeignKey = true;
                property.Attributes["foreign_key_referenced_entity"] = fkMatch.Groups[1].Value;
                property.Attributes["foreign_key_referenced_column"] = fkMatch.Groups[2].Success ? fkMatch.Groups[2].Value : "Id";

                if (fkMatch.Groups[3].Success)
                {
                    property.Attributes["foreign_key_on_delete"] = fkMatch.Groups[3].Value;
                }

                if (fkMatch.Groups[4].Success)
                {
                    property.Attributes["foreign_key_on_update"] = fkMatch.Groups[4].Value;
                }

                if (fkMatch.Groups[5].Success)
                {
                    property.Attributes["foreign_key_name"] = fkMatch.Groups[5].Value;
                }
            }
        }

        private void ParseJpaAnnotations(string fieldText, DiscoveredProperty property)
        {
            // @Id
            if (Regex.IsMatch(fieldText, @"@Id\b", RegexOptions.IgnoreCase))
            {
                property.IsPrimaryKey = true;
            }

            // @GeneratedValue
            if (Regex.IsMatch(fieldText, @"@GeneratedValue", RegexOptions.IgnoreCase))
            {
                property.Attributes["is_identity"] = true;
            }

            // @Column(name = "column_name", nullable = false, length = 100)
            var columnMatch = Regex.Match(fieldText, @"@Column\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
            if (columnMatch.Success)
            {
                var columnParams = columnMatch.Groups[1].Value;

                var nameMatch = Regex.Match(columnParams, @"name\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                {
                    property.Attributes["column_name"] = nameMatch.Groups[1].Value;
                }

                var nullableMatch = Regex.Match(columnParams, @"nullable\s*=\s*(true|false)", RegexOptions.IgnoreCase);
                if (nullableMatch.Success)
                {
                    property.IsNullable = bool.Parse(nullableMatch.Groups[1].Value);
                }

                var lengthMatch = Regex.Match(columnParams, @"length\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                if (lengthMatch.Success && int.TryParse(lengthMatch.Groups[1].Value, out var length))
                {
                    property.MaxLength = length;
                }
            }

            // @JoinColumn (for foreign keys)
            var joinColumnMatch = Regex.Match(fieldText, @"@JoinColumn\s*\(\s*name\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (joinColumnMatch.Success)
            {
                property.IsForeignKey = true;
                property.Attributes["foreign_key_column"] = joinColumnMatch.Groups[1].Value;
            }
        }

        private void ParseClassAnnotations(string content, DiscoveredEntity entity)
        {
            // Parse @Index annotations at class level
            var indexMatches = Regex.Matches(content, @"@Index\s*\(\s*name\s*=\s*[""']([^""']+)[""'](?:\s*,\s*columnList\s*=\s*[""']([^""']+)[""'])?(?:\s*,\s*unique\s*=\s*(true|false))?\s*\)", RegexOptions.IgnoreCase);

            foreach (Match match in indexMatches)
            {
                var index = new DiscoveredIndex
                {
                    Name = match.Groups[1].Value,
                    Columns = match.Groups[2].Success ? match.Groups[2].Value.Split(',').Select(c => c.Trim()).ToList() : new List<string>(),
                    IsUnique = match.Groups[3].Success && bool.Parse(match.Groups[3].Value),
                    IsClustered = false,
                    Attributes = new Dictionary<string, object>
                    {
                        ["from_annotation"] = true
                    }
                };

                entity.Indexes.Add(index);
            }
        }

        private string ExtractFieldName(string fieldText)
        {
            var match = Regex.Match(fieldText, @"[\w<>\[\],\s]+\s+(\w+)\s*[;=]", RegexOptions.RightToLeft);
            return match.Success ? match.Groups[1].Value : "Unknown";
        }

        private string ExtractFieldType(string fieldText)
        {
            // Extract the type declaration
            var match = Regex.Match(fieldText, @"(?:private|protected|public)?\s+(?:static\s+)?(?:final\s+)?([\w<>\[\],\s]+)\s+\w+\s*[;=]");
            if (match.Success)
            {
                var type = match.Groups[1].Value.Trim();
                // Simplify generic types
                if (type.Contains("<"))
                {
                    type = type.Substring(0, type.IndexOf('<'));
                }
                return type;
            }
            return "String";
        }

        private string MapJavaTypeToSql(string javaType)
        {
            return javaType.ToLowerInvariant() switch
            {
                "string" => "NVARCHAR",
                "int" or "integer" => "INT",
                "long" => "BIGINT",
                "short" => "SMALLINT",
                "byte" => "TINYINT",
                "bigdecimal" => "DECIMAL",
                "double" => "FLOAT",
                "float" => "REAL",
                "boolean" => "BIT",
                "date" or "localdatetime" => "DATETIME2",
                "localdate" => "DATE",
                "localtime" => "TIME",
                "offsetdatetime" => "DATETIMEOFFSET",
                "uuid" => "UNIQUEIDENTIFIER",
                "byte[]" => "VARBINARY",
                _ => "NVARCHAR"
            };
        }

        private bool IsTestFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            var directory = Path.GetDirectoryName(filePath)?.ToLowerInvariant() ?? "";

            return fileName.Contains("test") ||
                   directory.Contains("test") ||
                   directory.Contains("spec");
        }

        private string RemoveCommentsAndStrings(string content)
        {
            // Remove single-line comments
            content = Regex.Replace(content, @"//.*$", "", RegexOptions.Multiline);

            // Remove multi-line comments
            content = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);

            // Remove string literals
            content = Regex.Replace(content, @"""(?:[^""\\]|\\.)*""", "\"\"", RegexOptions.Singleline);

            return content;
        }

        private int FindFieldStart(string content, int fieldIndex)
        {
            // Look backwards for the start of annotations
            var lines = content.Substring(0, fieldIndex).Split('\n');
            var startLineIndex = lines.Length - 1;

            // Look for annotation lines above the field
            while (startLineIndex > 0)
            {
                var line = lines[startLineIndex - 1].Trim();
                if (line.StartsWith("@") || string.IsNullOrWhiteSpace(line))
                {
                    startLineIndex--;
                }
                else
                {
                    break;
                }
            }

            // Calculate character position
            var startPosition = 0;
            for (int i = 0; i < startLineIndex; i++)
            {
                startPosition += lines[i].Length + 1; // +1 for newline
            }

            return Math.Max(0, startPosition);
        }

        // Helper class for parsing multiple classes
        private class JavaClassMatch
        {
            public string ClassName { get; set; } = string.Empty;
            public string ClassContent { get; set; } = string.Empty;
            public int LineNumber { get; set; }
            public int StartIndex { get; set; }
        }
    }
}