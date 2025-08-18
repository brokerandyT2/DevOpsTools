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
    public interface IJavaScriptAnalyzerService : ILanguageAnalyzer
    {
        Task<List<DiscoveredEntity>> ParseJavaScriptFilesAsync(string sourcePath, string trackAttribute);
        Task<List<DiscoveredEntity>> ParseJavaScriptFileAsync(string filePath, string trackAttribute);
    }

    public class JavaScriptAnalyzerService : IJavaScriptAnalyzerService
    {
        private readonly ILogger<JavaScriptAnalyzerService> _logger;

        public JavaScriptAnalyzerService(ILogger<JavaScriptAnalyzerService> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredEntity>> DiscoverEntitiesAsync(string sourcePath, string trackAttribute)
        {
            _logger.LogInformation("Analyzing JavaScript files in: {SourcePath}", sourcePath);

            var entities = new List<DiscoveredEntity>();

            try
            {
                var jsEntities = await ParseJavaScriptFilesAsync(sourcePath, trackAttribute);
                entities.AddRange(jsEntities);

                _logger.LogInformation("✓ JavaScript analysis complete: {EntityCount} entities discovered", entities.Count);
                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JavaScript entity discovery failed");
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure,
                    $"JavaScript entity discovery failed: {ex.Message}", ex);
            }
        }

        public async Task<List<DiscoveredEntity>> ParseJavaScriptFilesAsync(string sourcePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var jsFiles = Directory.GetFiles(sourcePath, "*.js", SearchOption.AllDirectories)
                    .Where(f => !IsTestFile(f) && !IsMinifiedFile(f))
                    .ToList();

                _logger.LogDebug("Found {FileCount} JavaScript files", jsFiles.Count);

                foreach (var file in jsFiles)
                {
                    try
                    {
                        var fileEntities = await ParseJavaScriptFileAsync(file, trackAttribute);
                        entities.AddRange(fileEntities);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse JavaScript file: {FileName}", file);
                    }
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse JavaScript files from: {SourcePath}", sourcePath);
                throw;
            }
        }

        public async Task<List<DiscoveredEntity>> ParseJavaScriptFileAsync(string filePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var content = await File.ReadAllTextAsync(filePath);

                // Find ALL classes with the tracking decorator/comment
                var classMatches = ExtractClassesWithTracking(content, trackAttribute);

                if (!classMatches.Any())
                {
                    return entities;
                }

                // Process ALL matching classes in the file
                foreach (var classMatch in classMatches)
                {
                    var entity = new DiscoveredEntity
                    {
                        Name = classMatch.ClassName,
                        FullName = ExtractFullClassName(content, classMatch.ClassName),
                        Namespace = ExtractModuleName(content, filePath),
                        TableName = ExtractTableName(classMatch.Decorators, classMatch.ClassName),
                        SchemaName = ExtractSchemaName(classMatch.Decorators),
                        SourceFile = filePath,
                        SourceLine = classMatch.LineNumber,
                        Properties = new List<DiscoveredProperty>(),
                        Indexes = new List<DiscoveredIndex>(),
                        Relationships = new List<DiscoveredRelationship>(),
                        Attributes = new Dictionary<string, object>
                        {
                            ["track_attribute"] = trackAttribute,
                            ["language"] = "javascript",
                            ["module"] = ExtractModuleName(content, filePath),
                            ["class_type"] = DetermineClassType(classMatch.ClassDefinition)
                        }
                    };

                    // Parse class properties with decorators/comments
                    var properties = ExtractPropertiesWithMetadata(classMatch.ClassBody);
                    foreach (var property in properties)
                    {
                        var discoveredProperty = ParsePropertyMetadata(property, content);
                        if (discoveredProperty != null)
                        {
                            entity.Properties.Add(discoveredProperty);
                        }
                    }

                    // Parse class-level comments/decorators for additional metadata
                    ParseClassMetadata(classMatch.Decorators, entity);

                    entities.Add(entity);

                    _logger.LogTrace("Parsed JavaScript entity: {EntityName} with {PropertyCount} properties",
                        entity.Name, entity.Properties.Count);
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse JavaScript file: {FilePath}", filePath);
                return entities;
            }
        }

        private List<JavaScriptClassMatch> ExtractClassesWithTracking(string content, string trackAttribute)
        {
            var matches = new List<JavaScriptClassMatch>();

            // Look for both decorator and comment-based tracking
            var decoratorMatches = ExtractClassesWithDecorator(content, trackAttribute);
            var commentMatches = ExtractClassesWithComment(content, trackAttribute);

            matches.AddRange(decoratorMatches);
            matches.AddRange(commentMatches);

            return matches.GroupBy(m => m.ClassName).Select(g => g.First()).ToList(); // Remove duplicates
        }

        private List<JavaScriptClassMatch> ExtractClassesWithDecorator(string content, string trackAttribute)
        {
            var matches = new List<JavaScriptClassMatch>();

            // @ExportToSQL or @trackAttribute decorator pattern
            var decoratorPattern = $@"@{Regex.Escape(trackAttribute)}(?:\([^)]*\))?\s*\n";
            var classPattern = @"(?:export\s+)?(?:default\s+)?class\s+(\w+)(?:\s+extends\s+\w+)?\s*\{";

            // Find decorator positions
            var decoratorMatches = Regex.Matches(content, decoratorPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (Match decoratorMatch in decoratorMatches)
            {
                // Look for class definition after decorator
                var searchStart = decoratorMatch.Index;
                var remainingContent = content.Substring(searchStart);

                var classMatch = Regex.Match(remainingContent, classPattern, RegexOptions.IgnoreCase);
                if (classMatch.Success)
                {
                    var className = classMatch.Groups[1].Value;
                    var classStartIndex = searchStart + classMatch.Index;

                    // Extract decorators/comments above the class
                    var decorators = ExtractMetadataAboveClass(content, decoratorMatch.Index);
                    var classBody = ExtractClassBody(content, classStartIndex);
                    var lineNumber = GetLineNumber(content, classStartIndex);

                    matches.Add(new JavaScriptClassMatch
                    {
                        ClassName = className,
                        ClassDefinition = classMatch.Value,
                        ClassBody = classBody,
                        Decorators = decorators,
                        LineNumber = lineNumber,
                        StartIndex = classStartIndex,
                        TrackingMethod = "decorator"
                    });
                }
            }

            return matches;
        }

        private List<JavaScriptClassMatch> ExtractClassesWithComment(string content, string trackAttribute)
        {
            var matches = new List<JavaScriptClassMatch>();

            // Comment-based tracking: /** @ExportToSQL */ or // @ExportToSQL
            var commentPattern = $@"(?://\s*@{Regex.Escape(trackAttribute)}|/\*\*?\s*@{Regex.Escape(trackAttribute)}[^*]*\*/)";
            var classPattern = @"(?:export\s+)?(?:default\s+)?class\s+(\w+)(?:\s+extends\s+\w+)?\s*\{";

            // Find comment positions
            var commentMatches = Regex.Matches(content, commentPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (Match commentMatch in commentMatches)
            {
                // Look for class definition after comment (allowing some whitespace)
                var searchStart = commentMatch.Index + commentMatch.Length;
                var searchContent = content.Substring(searchStart);

                // Allow some whitespace/newlines between comment and class
                var classMatch = Regex.Match(searchContent, @"^\s*" + classPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (classMatch.Success)
                {
                    var className = classMatch.Groups[1].Value;
                    var classStartIndex = searchStart + classMatch.Index;

                    // Extract comments above the class
                    var comments = ExtractCommentsAboveClass(content, commentMatch.Index);
                    var classBody = ExtractClassBody(content, classStartIndex);
                    var lineNumber = GetLineNumber(content, classStartIndex);

                    matches.Add(new JavaScriptClassMatch
                    {
                        ClassName = className,
                        ClassDefinition = classMatch.Value,
                        ClassBody = classBody,
                        Decorators = comments,
                        LineNumber = lineNumber,
                        StartIndex = classStartIndex,
                        TrackingMethod = "comment"
                    });
                }
            }

            return matches;
        }

        private List<string> ExtractMetadataAboveClass(string content, int classStartIndex)
        {
            var metadata = new List<string>();
            var lines = content.Substring(0, classStartIndex).Split('\n');

            // Look backwards for decorators and comments
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("@") || line.StartsWith("//") || line.StartsWith("/*"))
                {
                    metadata.Insert(0, line);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    break;
                }
            }

            return metadata;
        }

        private List<string> ExtractCommentsAboveClass(string content, int commentStartIndex)
        {
            var comments = new List<string>();
            var lines = content.Substring(0, commentStartIndex).Split('\n');

            // Look backwards for related comments
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//") || line.StartsWith("/*"))
                {
                    comments.Insert(0, line);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    break;
                }
            }

            return comments;
        }

        private string ExtractClassBody(string content, int classStartIndex)
        {
            // Find the opening brace
            var openBraceIndex = content.IndexOf('{', classStartIndex);
            if (openBraceIndex == -1) return string.Empty;

            // Find the matching closing brace using a simple brace counter
            var braceCount = 0;
            var index = openBraceIndex;
            var inString = false;
            var inComment = false;
            var stringChar = '\0';

            while (index < content.Length)
            {
                var current = content[index];
                var next = index + 1 < content.Length ? content[index + 1] : '\0';

                // Handle string literals
                if (!inComment && (current == '"' || current == '\'' || current == '`'))
                {
                    if (!inString)
                    {
                        inString = true;
                        stringChar = current;
                    }
                    else if (current == stringChar && (index == 0 || content[index - 1] != '\\'))
                    {
                        inString = false;
                    }
                }
                // Handle comments
                else if (!inString)
                {
                    if (current == '/' && next == '/')
                    {
                        inComment = true;
                    }
                    else if (current == '/' && next == '*')
                    {
                        inComment = true;
                        index++; // Skip next character
                    }
                    else if (inComment && current == '*' && next == '/')
                    {
                        inComment = false;
                        index++; // Skip next character
                    }
                    else if (inComment && current == '\n')
                    {
                        inComment = false; // End of line comment
                    }
                    else if (!inComment)
                    {
                        if (current == '{') braceCount++;
                        if (current == '}') braceCount--;

                        if (braceCount == 0)
                        {
                            return content.Substring(classStartIndex, index - classStartIndex + 1);
                        }
                    }
                }

                index++;
            }

            return content.Substring(classStartIndex);
        }

        private List<JavaScriptPropertyMatch> ExtractPropertiesWithMetadata(string classBody)
        {
            var properties = new List<JavaScriptPropertyMatch>();
            var lines = classBody.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Look for property decorators or comments
                if (line.StartsWith("@") || line.StartsWith("//") || line.StartsWith("/*"))
                {
                    var metadata = new List<string> { line };

                    // Collect consecutive metadata lines
                    while (i + 1 < lines.Length)
                    {
                        var nextLine = lines[i + 1].Trim();
                        if (nextLine.StartsWith("@") || nextLine.StartsWith("//") || nextLine.StartsWith("/*"))
                        {
                            i++;
                            metadata.Add(nextLine);
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Next line should be property definition
                    if (i + 1 < lines.Length)
                    {
                        i++;
                        var propertyLine = lines[i].Trim();

                        // Match property definition: this.name = value; or name = value; or name;
                        var propertyMatch = Regex.Match(propertyLine, @"(?:this\.)?(\w+)\s*(?:=\s*([^;]+))?\s*;?");
                        if (propertyMatch.Success)
                        {
                            properties.Add(new JavaScriptPropertyMatch
                            {
                                Name = propertyMatch.Groups[1].Value,
                                DefaultValue = propertyMatch.Groups[2].Success ? propertyMatch.Groups[2].Value.Trim() : null,
                                Metadata = metadata,
                                FullDefinition = string.Join("\n", metadata) + "\n" + propertyLine
                            });
                        }
                    }
                }
                // Also look for simple properties without metadata
                else if (Regex.IsMatch(line, @"(?:this\.)?\w+\s*(?:=|;)"))
                {
                    var propertyMatch = Regex.Match(line, @"(?:this\.)?(\w+)\s*(?:=\s*([^;]+))?\s*;?");
                    if (propertyMatch.Success)
                    {
                        properties.Add(new JavaScriptPropertyMatch
                        {
                            Name = propertyMatch.Groups[1].Value,
                            DefaultValue = propertyMatch.Groups[2].Success ? propertyMatch.Groups[2].Value.Trim() : null,
                            Metadata = new List<string>(),
                            FullDefinition = line
                        });
                    }
                }
            }

            return properties;
        }

        private DiscoveredProperty? ParsePropertyMetadata(JavaScriptPropertyMatch property, string fullContent)
        {
            try
            {
                var discoveredProperty = new DiscoveredProperty
                {
                    Name = property.Name,
                    Type = InferJavaScriptType(property.DefaultValue),
                    SqlType = "NVARCHAR",
                    IsNullable = true, // JavaScript properties are nullable by default
                    IsPrimaryKey = property.Name.ToLowerInvariant() == "id",
                    IsForeignKey = property.Name.ToLowerInvariant().EndsWith("id") && property.Name.ToLowerInvariant() != "id",
                    IsUnique = false,
                    IsIndexed = false,
                    DefaultValue = property.DefaultValue,
                    Attributes = new Dictionary<string, object>()
                };

                // Parse metadata (decorators or comments)
                foreach (var metadata in property.Metadata)
                {
                    ParseJavaScriptMetadata(metadata, discoveredProperty);
                }

                // Infer SQL type from JavaScript type if not explicitly set
                if (discoveredProperty.SqlType == "NVARCHAR")
                {
                    discoveredProperty.SqlType = MapJavaScriptTypeToSql(discoveredProperty.Type);
                }

                return discoveredProperty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse property: {PropertyName}", property.Name);
                return null;
            }
        }

        private void ParseJavaScriptMetadata(string metadata, DiscoveredProperty property)
        {
            // Handle both decorator and comment formats
            var cleanMetadata = metadata.Replace("//", "").Replace("/*", "").Replace("*/", "").Trim();

            // @SqlType(SqlDataType.NVARCHAR, { length: 100 })
            var sqlTypeMatch = Regex.Match(cleanMetadata, @"@SqlType\s*\(\s*SqlDataType\.(\w+)(?:\s*,\s*\{\s*length\s*:\s*(\d+)(?:\s*,\s*precision\s*:\s*(\d+))?(?:\s*,\s*scale\s*:\s*(\d+))?\s*\})?\s*\)", RegexOptions.IgnoreCase);
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

            // @SqlConstraints(SqlConstraint.NOT_NULL, SqlConstraint.UNIQUE)
            var constraintsMatch = Regex.Match(cleanMetadata, @"@SqlConstraints\s*\(\s*([^)]+)\s*\)", RegexOptions.IgnoreCase);
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

            // @SqlForeignKeyTo(User, { referencedProperty: 'id', onDelete: 'CASCADE' })
            var fkMatch = Regex.Match(cleanMetadata,
                @"@SqlForeignKeyTo\s*\(\s*(\w+)(?:\s*,\s*\{(?:\s*referencedProperty\s*:\s*[""']([^""']+)[""'])?(?:\s*,\s*onDelete\s*:\s*[""']([^""']+)[""'])?(?:\s*,\s*onUpdate\s*:\s*[""']([^""']+)[""'])?(?:\s*,\s*name\s*:\s*[""']([^""']+)[""'])?\s*\})?\s*\)",
                RegexOptions.IgnoreCase);

            if (fkMatch.Success)
            {
                property.IsForeignKey = true;
                property.Attributes["foreign_key_referenced_entity"] = fkMatch.Groups[1].Value;
                property.Attributes["foreign_key_referenced_column"] = fkMatch.Groups[2].Success ? fkMatch.Groups[2].Value : "id";

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

        private void ParseClassMetadata(List<string> metadata, DiscoveredEntity entity)
        {
            // Parse class-level metadata for tables, schemas, etc.
            foreach (var meta in metadata)
            {
                var cleanMeta = meta.Replace("//", "").Replace("/*", "").Replace("*/", "").Trim();

                // @Table('table_name')
                var tableMatch = Regex.Match(cleanMeta, @"@Table\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase);
                if (tableMatch.Success)
                {
                    entity.TableName = tableMatch.Groups[1].Value;
                }

                // @Schema('schema_name')
                var schemaMatch = Regex.Match(cleanMeta, @"@Schema\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase);
                if (schemaMatch.Success)
                {
                    entity.SchemaName = schemaMatch.Groups[1].Value;
                }
            }
        }

        private string ExtractFullClassName(string content, string className)
        {
            var moduleName = ExtractModuleName(content, "");
            return string.IsNullOrEmpty(moduleName) ? className : $"{moduleName}.{className}";
        }

        private string ExtractModuleName(string content, string filePath)
        {
            // Try to extract from file path
            if (!string.IsNullOrEmpty(filePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                return fileName;
            }

            // Try to extract from module exports
            var moduleMatch = Regex.Match(content, @"module\.exports\s*=\s*(\w+)", RegexOptions.IgnoreCase);
            if (moduleMatch.Success)
            {
                return moduleMatch.Groups[1].Value;
            }

            return "module";
        }

        private string ExtractTableName(List<string> metadata, string className)
        {
            // Look for @Table decorator in metadata
            foreach (var meta in metadata)
            {
                var cleanMeta = meta.Replace("//", "").Replace("/*", "").Replace("*/", "").Trim();
                var match = Regex.Match(cleanMeta, @"@Table\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            // Default to snake_case class name
            return ToSnakeCase(className);
        }

        private string ExtractSchemaName(List<string> metadata)
        {
            // Look for @Schema decorator in metadata
            foreach (var meta in metadata)
            {
                var cleanMeta = meta.Replace("//", "").Replace("/*", "").Replace("*/", "").Trim();
                var match = Regex.Match(cleanMeta, @"@Schema\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return "dbo"; // Default schema
        }

        private int GetLineNumber(string content, int characterIndex)
        {
            return content.Substring(0, characterIndex).Count(c => c == '\n') + 1;
        }

        private string DetermineClassType(string classDefinition)
        {
            if (classDefinition.Contains("export default"))
                return "default_export";
            if (classDefinition.Contains("export"))
                return "named_export";
            return "class";
        }

        private string InferJavaScriptType(string? defaultValue)
        {
            if (string.IsNullOrEmpty(defaultValue))
                return "any";

            defaultValue = defaultValue.Trim();

            if (defaultValue == "null" || defaultValue == "undefined")
                return "any";
            if (defaultValue == "true" || defaultValue == "false")
                return "boolean";
            if (Regex.IsMatch(defaultValue, @"^\d+$"))
                return "number";
            if (Regex.IsMatch(defaultValue, @"^\d+\.\d+$"))
                return "number";
            if (defaultValue.StartsWith('"') || defaultValue.StartsWith('\'') || defaultValue.StartsWith('`'))
                return "string";
            if (defaultValue.StartsWith('['))
                return "array";
            if (defaultValue.StartsWith('{'))
                return "object";
            if (defaultValue.Contains("new Date"))
                return "Date";

            return "any";
        }

        private string MapJavaScriptTypeToSql(string jsType)
        {
            return jsType.ToLowerInvariant() switch
            {
                "string" => "NVARCHAR",
                "number" => "FLOAT",
                "boolean" => "BIT",
                "date" => "DATETIME2",
                "array" => "NVARCHAR", // JSON as string
                "object" => "NVARCHAR", // JSON as string
                _ => "NVARCHAR"
            };
        }

        private string ToSnakeCase(string camelCase)
        {
            return Regex.Replace(camelCase, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        }

        private bool IsTestFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            var directory = Path.GetDirectoryName(filePath)?.ToLowerInvariant() ?? "";

            return fileName.Contains(".test.") ||
                   fileName.Contains(".spec.") ||
                   fileName.EndsWith("test.js") ||
                   fileName.EndsWith("spec.js") ||
                   directory.Contains("test") ||
                   directory.Contains("tests") ||
                   directory.Contains("spec");
        }

        private bool IsMinifiedFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            return fileName.Contains(".min.") || fileName.EndsWith(".min.js");
        }

        // Helper classes for parsing
        private class JavaScriptClassMatch
        {
            public string ClassName { get; set; } = string.Empty;
            public string ClassDefinition { get; set; } = string.Empty;
            public string ClassBody { get; set; } = string.Empty;
            public List<string> Decorators { get; set; } = new();
            public int LineNumber { get; set; }
            public int StartIndex { get; set; }
            public string TrackingMethod { get; set; } = string.Empty; // "decorator" or "comment"
        }

        private class JavaScriptPropertyMatch
        {
            public string Name { get; set; } = string.Empty;
            public string? DefaultValue { get; set; }
            public List<string> Metadata { get; set; } = new();
            public string FullDefinition { get; set; } = string.Empty;
        }
    }
}