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
    public interface ITypeScriptAnalyzerService : ILanguageAnalyzer
    {
        Task<List<DiscoveredEntity>> ParseTypeScriptFilesAsync(string sourcePath, string trackAttribute);
        Task<List<DiscoveredEntity>> ParseTypeScriptFileAsync(string filePath, string trackAttribute);
    }

    public class TypeScriptAnalyzerService : ITypeScriptAnalyzerService
    {
        private readonly ILogger<TypeScriptAnalyzerService> _logger;

        public TypeScriptAnalyzerService(ILogger<TypeScriptAnalyzerService> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredEntity>> DiscoverEntitiesAsync(string sourcePath, string trackAttribute)
        {
            _logger.LogInformation("Analyzing TypeScript files in: {SourcePath}", sourcePath);

            var entities = new List<DiscoveredEntity>();

            try
            {
                var tsEntities = await ParseTypeScriptFilesAsync(sourcePath, trackAttribute);
                entities.AddRange(tsEntities);

                _logger.LogInformation("✓ TypeScript analysis complete: {EntityCount} entities discovered", entities.Count);
                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TypeScript entity discovery failed");
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure,
                    $"TypeScript entity discovery failed: {ex.Message}", ex);
            }
        }

        public async Task<List<DiscoveredEntity>> ParseTypeScriptFilesAsync(string sourcePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var tsFiles = Directory.GetFiles(sourcePath, "*.ts", SearchOption.AllDirectories)
                    .Where(f => !IsTestFile(f) && !IsDeclarationFile(f))
                    .ToList();

                _logger.LogDebug("Found {FileCount} TypeScript files", tsFiles.Count);

                foreach (var file in tsFiles)
                {
                    try
                    {
                        var fileEntities = await ParseTypeScriptFileAsync(file, trackAttribute);
                        entities.AddRange(fileEntities);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse TypeScript file: {FileName}", file);
                    }
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse TypeScript files from: {SourcePath}", sourcePath);
                throw;
            }
        }

        public async Task<List<DiscoveredEntity>> ParseTypeScriptFileAsync(string filePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var content = await File.ReadAllTextAsync(filePath);

                // Find ALL classes with the tracking decorator
                var classMatches = ExtractClassesWithDecorator(content, trackAttribute);

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
                        Namespace = ExtractNamespace(content, filePath),
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
                            ["language"] = "typescript",
                            ["module"] = ExtractModuleName(content, filePath),
                            ["export_type"] = DetermineExportType(classMatch.ClassDefinition)
                        }
                    };

                    // Parse class properties with decorators/type annotations
                    var properties = ExtractPropertiesWithDecorators(classMatch.ClassBody);
                    foreach (var property in properties)
                    {
                        var discoveredProperty = ParsePropertyDecorators(property, content);
                        if (discoveredProperty != null)
                        {
                            entity.Properties.Add(discoveredProperty);
                        }
                    }

                    // Parse class-level decorators for additional metadata
                    ParseClassDecorators(classMatch.Decorators, entity);

                    entities.Add(entity);

                    _logger.LogTrace("Parsed TypeScript entity: {EntityName} with {PropertyCount} properties",
                        entity.Name, entity.Properties.Count);
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse TypeScript file: {FilePath}", filePath);
                return entities;
            }
        }

        private List<TypeScriptClassMatch> ExtractClassesWithDecorator(string content, string trackAttribute)
        {
            var matches = new List<TypeScriptClassMatch>();

            // Use the actual tracking attribute passed in - don't transform it
            var decoratorPattern = $@"@{Regex.Escape(trackAttribute)}(?:\([^)]*\))?\s*\n";
            var classPattern = @"(?:export\s+)?(?:abstract\s+)?class\s+(\w+)(?:\s+extends\s+[\w<>]+)?(?:\s+implements\s+[\w\s,<>]+)?\s*\{";

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

                    // Extract decorators above the class
                    var decorators = ExtractDecoratorsAboveClass(content, decoratorMatch.Index);
                    var classBody = ExtractClassBody(content, classStartIndex);
                    var lineNumber = GetLineNumber(content, classStartIndex);

                    matches.Add(new TypeScriptClassMatch
                    {
                        ClassName = className,
                        ClassDefinition = classMatch.Value,
                        ClassBody = classBody,
                        Decorators = decorators,
                        LineNumber = lineNumber,
                        StartIndex = classStartIndex
                    });
                }
            }

            return matches;
        }

        private List<string> ExtractDecoratorsAboveClass(string content, int classStartIndex)
        {
            var decorators = new List<string>();
            var lines = content.Substring(0, classStartIndex).Split('\n');

            // Look backwards for decorators
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("@"))
                {
                    decorators.Insert(0, line);
                }
                else if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("//") && !line.StartsWith("/*"))
                {
                    break;
                }
            }

            return decorators;
        }

        private string ExtractClassBody(string content, int classStartIndex)
        {
            // Find the opening brace
            var openBraceIndex = content.IndexOf('{', classStartIndex);
            if (openBraceIndex == -1) return string.Empty;

            // Find the matching closing brace
            var braceCount = 0;
            var index = openBraceIndex;
            var inString = false;
            var inComment = false;
            var escapeNext = false;

            while (index < content.Length)
            {
                var current = content[index];
                var next = index + 1 < content.Length ? content[index + 1] : '\0';

                if (escapeNext)
                {
                    escapeNext = false;
                    index++;
                    continue;
                }

                if (current == '\\' && inString)
                {
                    escapeNext = true;
                    index++;
                    continue;
                }

                if (!inComment && (current == '"' || current == '\'' || current == '`'))
                {
                    inString = !inString;
                }
                else if (!inString && current == '/' && next == '/')
                {
                    inComment = true;
                }
                else if (!inString && current == '/' && next == '*')
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
                else if (!inString && !inComment)
                {
                    if (current == '{') braceCount++;
                    if (current == '}') braceCount--;

                    if (braceCount == 0)
                    {
                        return content.Substring(classStartIndex, index - classStartIndex + 1);
                    }
                }

                index++;
            }

            return content.Substring(classStartIndex);
        }

        private List<TypeScriptPropertyMatch> ExtractPropertiesWithDecorators(string classBody)
        {
            var properties = new List<TypeScriptPropertyMatch>();
            var lines = classBody.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Look for property decorators
                if (line.StartsWith("@"))
                {
                    var decorators = new List<string> { line };

                    // Collect consecutive decorators
                    while (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith("@"))
                    {
                        i++;
                        decorators.Add(lines[i].Trim());
                    }

                    // Next line should be property definition
                    if (i + 1 < lines.Length)
                    {
                        i++;
                        var propertyLine = lines[i].Trim();

                        // Match property definition: [access] name: type = value; or [access] name?: type;
                        var propertyMatch = Regex.Match(propertyLine, @"(?:(public|private|protected|readonly)\s+)?(\w+)(\?)?\s*:\s*([^=;]+)(?:\s*=\s*([^;]+))?\s*;?");
                        if (propertyMatch.Success)
                        {
                            properties.Add(new TypeScriptPropertyMatch
                            {
                                Name = propertyMatch.Groups[2].Value,
                                AccessModifier = propertyMatch.Groups[1].Success ? propertyMatch.Groups[1].Value : "public",
                                IsOptional = propertyMatch.Groups[3].Success,
                                TypeAnnotation = propertyMatch.Groups[4].Value.Trim(),
                                DefaultValue = propertyMatch.Groups[5].Success ? propertyMatch.Groups[5].Value.Trim() : null,
                                Decorators = decorators,
                                FullDefinition = string.Join("\n", decorators) + "\n" + propertyLine
                            });
                        }
                    }
                }
                // Also look for simple properties without decorators
                else if (Regex.IsMatch(line, @"(?:public|private|protected|readonly\s+)?\w+\s*\??\s*:\s*[^=;]+"))
                {
                    var propertyMatch = Regex.Match(line, @"(?:(public|private|protected|readonly)\s+)?(\w+)(\?)?\s*:\s*([^=;]+)(?:\s*=\s*([^;]+))?\s*;?");
                    if (propertyMatch.Success)
                    {
                        properties.Add(new TypeScriptPropertyMatch
                        {
                            Name = propertyMatch.Groups[2].Value,
                            AccessModifier = propertyMatch.Groups[1].Success ? propertyMatch.Groups[1].Value : "public",
                            IsOptional = propertyMatch.Groups[3].Success,
                            TypeAnnotation = propertyMatch.Groups[4].Value.Trim(),
                            DefaultValue = propertyMatch.Groups[5].Success ? propertyMatch.Groups[5].Value.Trim() : null,
                            Decorators = new List<string>(),
                            FullDefinition = line
                        });
                    }
                }
            }

            return properties;
        }

        private DiscoveredProperty? ParsePropertyDecorators(TypeScriptPropertyMatch property, string fullContent)
        {
            try
            {
                var discoveredProperty = new DiscoveredProperty
                {
                    Name = property.Name,
                    Type = ExtractTypeScriptType(property.TypeAnnotation),
                    SqlType = "NVARCHAR",
                    IsNullable = property.IsOptional || IsNullableType(property.TypeAnnotation),
                    IsPrimaryKey = property.Name.ToLowerInvariant() == "id",
                    IsForeignKey = property.Name.ToLowerInvariant().EndsWith("id") && property.Name.ToLowerInvariant() != "id",
                    IsUnique = false,
                    IsIndexed = false,
                    DefaultValue = property.DefaultValue,
                    Attributes = new Dictionary<string, object>
                    {
                        ["access_modifier"] = property.AccessModifier,
                        ["is_optional"] = property.IsOptional
                    }
                };

                // Parse decorators
                foreach (var decorator in property.Decorators)
                {
                    ParseTypeScriptDecorator(decorator, discoveredProperty);
                }

                // Infer SQL type from TypeScript type if not explicitly set
                if (discoveredProperty.SqlType == "NVARCHAR")
                {
                    discoveredProperty.SqlType = MapTypeScriptTypeToSql(discoveredProperty.Type);
                }

                return discoveredProperty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse property: {PropertyName}", property.Name);
                return null;
            }
        }

        private void ParseTypeScriptDecorator(string decorator, DiscoveredProperty property)
        {
            // @SqlType(SqlDataType.NVarChar, { length: 100 })
            var sqlTypeMatch = Regex.Match(decorator, @"@SqlType\s*\(\s*SqlDataType\.(\w+)(?:\s*,\s*\{\s*length\s*:\s*(\d+)(?:\s*,\s*precision\s*:\s*(\d+))?(?:\s*,\s*scale\s*:\s*(\d+))?\s*\})?\s*\)", RegexOptions.IgnoreCase);
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

            // @SqlConstraints(SqlConstraint.NotNull, SqlConstraint.Unique)
            var constraintsMatch = Regex.Match(decorator, @"@SqlConstraints\s*\(\s*([^)]+)\s*\)", RegexOptions.IgnoreCase);
            if (constraintsMatch.Success)
            {
                var constraintsText = constraintsMatch.Groups[1].Value;
                var constraints = Regex.Matches(constraintsText, @"SqlConstraint\.(\w+)", RegexOptions.IgnoreCase);

                foreach (Match constraint in constraints)
                {
                    var constraintName = constraint.Groups[1].Value;
                    switch (constraintName)
                    {
                        case "NotNull":
                            property.IsNullable = false;
                            break;
                        case "Unique":
                            property.IsUnique = true;
                            break;
                        case "PrimaryKey":
                            property.IsPrimaryKey = true;
                            break;
                        case "Identity":
                            property.Attributes["is_identity"] = true;
                            break;
                    }
                }
            }

            // @SqlForeignKeyTo<User>({ referencedProperty: 'id', onDelete: 'CASCADE' })
            var fkMatch = Regex.Match(decorator,
                @"@SqlForeignKeyTo<(\w+)>\s*\(\s*\{(?:\s*referencedProperty\s*:\s*[""']([^""']+)[""'])?(?:\s*,\s*onDelete\s*:\s*[""']([^""']+)[""'])?(?:\s*,\s*onUpdate\s*:\s*[""']([^""']+)[""'])?(?:\s*,\s*name\s*:\s*[""']([^""']+)[""'])?\s*\}\s*\)",
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

        private void ParseClassDecorators(List<string> decorators, DiscoveredEntity entity)
        {
            // Parse additional class-level decorators for indexes, etc.
            foreach (var decorator in decorators)
            {
                // @Index({ name: 'idx_name', columns: ['col1', 'col2'], unique: true })
                var indexMatch = Regex.Match(decorator, @"@Index\s*\(\s*\{(?:\s*name\s*:\s*[""']([^""']+)[""'])?(?:\s*,\s*columns\s*:\s*\[([^\]]+)\])?(?:\s*,\s*unique\s*:\s*(true|false))?\s*\}\s*\)", RegexOptions.IgnoreCase);
                if (indexMatch.Success)
                {
                    var index = new DiscoveredIndex
                    {
                        Name = indexMatch.Groups[1].Success ? indexMatch.Groups[1].Value : $"IX_{entity.Name}",
                        Columns = indexMatch.Groups[2].Success ? ParseColumnList(indexMatch.Groups[2].Value) : new List<string>(),
                        IsUnique = indexMatch.Groups[3].Success && bool.Parse(indexMatch.Groups[3].Value),
                        IsClustered = false,
                        Attributes = new Dictionary<string, object>
                        {
                            ["from_decorator"] = true
                        }
                    };

                    entity.Indexes.Add(index);
                }
            }
        }

        private List<string> ParseColumnList(string columnList)
        {
            return Regex.Matches(columnList, @"[""']([^""']+)[""']")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();
        }

        private string ExtractFullClassName(string content, string className)
        {
            var namespaceName = ExtractNamespaceFromContent(content);
            return string.IsNullOrEmpty(namespaceName) ? className : $"{namespaceName}.{className}";
        }

        private string ExtractNamespace(string content, string filePath)
        {
            // Try to extract from namespace declaration or module structure
            var namespaceMatch = Regex.Match(content, @"namespace\s+([\w\.]+)", RegexOptions.IgnoreCase);
            if (namespaceMatch.Success)
            {
                return namespaceMatch.Groups[1].Value;
            }

            // Extract from file path structure
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null)
            {
                var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Join(".", parts.Where(p => !string.IsNullOrEmpty(p)));
            }

            return string.Empty;
        }

        private string ExtractNamespaceFromContent(string content)
        {
            var namespaceMatch = Regex.Match(content, @"namespace\s+([\w\.]+)", RegexOptions.IgnoreCase);
            return namespaceMatch.Success ? namespaceMatch.Groups[1].Value : string.Empty;
        }

        private string ExtractModuleName(string content, string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            return fileName;
        }

        private string ExtractTableName(List<string> decorators, string className)
        {
            // Look for @Table("table_name") decorator
            foreach (var decorator in decorators)
            {
                var match = Regex.Match(decorator, @"@Table\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            // Default to camelCase to snake_case conversion
            return ToSnakeCase(className);
        }

        private string ExtractSchemaName(List<string> decorators)
        {
            // Look for @Schema("schema_name") decorator
            foreach (var decorator in decorators)
            {
                var match = Regex.Match(decorator, @"@Schema\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase);
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

        private string DetermineExportType(string classDefinition)
        {
            if (classDefinition.Contains("export default"))
                return "default";
            if (classDefinition.Contains("export"))
                return "named";
            return "none";
        }

        private string ExtractTypeScriptType(string typeAnnotation)
        {
            // Clean up type annotation
            var cleanType = typeAnnotation.Replace("undefined", "").Replace("null", "").Trim();

            // Handle union types
            if (cleanType.Contains("|"))
            {
                var types = cleanType.Split('|').Select(t => t.Trim()).Where(t => t != "null" && t != "undefined");
                cleanType = types.FirstOrDefault() ?? "any";
            }

            // Handle generic types
            if (cleanType.Contains("<"))
            {
                cleanType = cleanType.Substring(0, cleanType.IndexOf('<'));
            }

            return cleanType;
        }

        private bool IsNullableType(string typeAnnotation)
        {
            return typeAnnotation.Contains("| null") ||
                   typeAnnotation.Contains("| undefined") ||
                   typeAnnotation.Contains("?");
        }

        private string MapTypeScriptTypeToSql(string tsType)
        {
            return tsType.ToLowerInvariant() switch
            {
                "string" => "NVARCHAR",
                "number" => "INT",
                "bigint" => "BIGINT",
                "boolean" => "BIT",
                "date" => "DATETIME2",
                "buffer" or "uint8array" => "VARBINARY",
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
                   fileName.EndsWith("test.ts") ||
                   fileName.EndsWith("spec.ts") ||
                   directory.Contains("test") ||
                   directory.Contains("tests") ||
                   directory.Contains("spec");
        }

        private bool IsDeclarationFile(string filePath)
        {
            return Path.GetFileName(filePath).ToLowerInvariant().EndsWith(".d.ts");
        }

        // Helper classes for parsing
        private class TypeScriptClassMatch
        {
            public string ClassName { get; set; } = string.Empty;
            public string ClassDefinition { get; set; } = string.Empty;
            public string ClassBody { get; set; } = string.Empty;
            public List<string> Decorators { get; set; } = new();
            public int LineNumber { get; set; }
            public int StartIndex { get; set; }
        }

        private class TypeScriptPropertyMatch
        {
            public string Name { get; set; } = string.Empty;
            public string AccessModifier { get; set; } = "public";
            public bool IsOptional { get; set; }
            public string TypeAnnotation { get; set; } = string.Empty;
            public string? DefaultValue { get; set; }
            public List<string> Decorators { get; set; } = new();
            public string FullDefinition { get; set; } = string.Empty;
        }
    }
}