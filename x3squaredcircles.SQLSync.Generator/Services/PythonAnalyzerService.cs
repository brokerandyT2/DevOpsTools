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
    public interface IPythonAnalyzerService : ILanguageAnalyzer
    {
        Task<List<DiscoveredEntity>> ParsePythonFilesAsync(string sourcePath, string trackAttribute);
        Task<List<DiscoveredEntity>> ParsePythonFileAsync(string filePath, string trackAttribute);
    }

    public class PythonAnalyzerService : IPythonAnalyzerService
    {
        private readonly ILogger<PythonAnalyzerService> _logger;

        public PythonAnalyzerService(ILogger<PythonAnalyzerService> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredEntity>> DiscoverEntitiesAsync(string sourcePath, string trackAttribute)
        {
            _logger.LogInformation("Analyzing Python modules in: {SourcePath}", sourcePath);

            var entities = new List<DiscoveredEntity>();

            try
            {
                var pythonEntities = await ParsePythonFilesAsync(sourcePath, trackAttribute);
                entities.AddRange(pythonEntities);

                _logger.LogInformation("✓ Python analysis complete: {EntityCount} entities discovered", entities.Count);
                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Python entity discovery failed");
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure,
                    $"Python entity discovery failed: {ex.Message}", ex);
            }
        }

        public async Task<List<DiscoveredEntity>> ParsePythonFilesAsync(string sourcePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var pythonFiles = Directory.GetFiles(sourcePath, "*.py", SearchOption.AllDirectories)
                    .Where(f => !IsTestFile(f) && !IsInitFile(f))
                    .ToList();

                _logger.LogDebug("Found {FileCount} Python files", pythonFiles.Count);

                foreach (var file in pythonFiles)
                {
                    try
                    {
                        var fileEntities = await ParsePythonFileAsync(file, trackAttribute);
                        entities.AddRange(fileEntities);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse Python file: {FileName}", file);
                    }
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Python files from: {SourcePath}", sourcePath);
                throw;
            }
        }

        public async Task<List<DiscoveredEntity>> ParsePythonFileAsync(string filePath, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var content = await File.ReadAllTextAsync(filePath);

                // Find ALL classes with the tracking decorator
                var classMatches = ExtractClassesWithDecorator(content, trackAttribute);

                if (!classMatches.Any())
                {
                    return entities; // Return empty list instead of null
                }

                // Process ALL matching classes in the file
                foreach (var classMatch in classMatches)
                {
                    var entity = new DiscoveredEntity
                    {
                        Name = ExtractClassName(classMatch.ClassDefinition),
                        FullName = ExtractFullClassName(content, classMatch.ClassName),
                        Namespace = ExtractModuleName(content, filePath),
                        TableName = ExtractTableName(classMatch.Decorators, classMatch.ClassName),
                        SchemaName = ExtractSchemaName(classMatch.Decorators),
                        SourceFile = filePath,
                        SourceLine = GetClassLineNumber(content, classMatch.ClassName),
                        Properties = new List<DiscoveredProperty>(),
                        Indexes = new List<DiscoveredIndex>(),
                        Relationships = new List<DiscoveredRelationship>(),
                        Attributes = new Dictionary<string, object>
                        {
                            ["track_attribute"] = trackAttribute,
                            ["language"] = "python",
                            ["module"] = ExtractModuleName(content, filePath),
                            ["class_type"] = DetermineClassType(classMatch.ClassDefinition)
                        }
                    };

                    // Parse class fields with decorators/type hints
                    var fields = ExtractFieldsWithDecorators(classMatch.ClassBody);
                    foreach (var field in fields)
                    {
                        var property = ParseFieldDecorators(field, content);
                        if (property != null)
                        {
                            entity.Properties.Add(property);
                        }
                    }

                    entities.Add(entity);

                    _logger.LogTrace("Parsed Python entity: {EntityName} with {PropertyCount} properties",
                        entity.Name, entity.Properties.Count);
                }

                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Python file: {FilePath}", filePath);
                return entities; // Return empty list instead of null
            }
        }

        private string ConvertToPythonDecorator(string trackAttribute)
        {
            // Convert common C# attribute names to Python decorator convention
            if (trackAttribute.Equals("ExportToSQL", StringComparison.OrdinalIgnoreCase))
            {
                return "export_to_sql";
            }

            // For other attributes, convert PascalCase to snake_case
            return Regex.Replace(trackAttribute, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        }

        private List<PythonClassMatch> ExtractClassesWithDecorator(string content, string trackAttribute)
        {
            var matches = new List<PythonClassMatch>();

            // Use the actual tracking attribute - convert to snake_case if needed for Python convention
            var pythonDecorator = ConvertToPythonDecorator(trackAttribute);
            var decoratorPattern = $@"@{Regex.Escape(pythonDecorator)}(?:\([^)]*\))?\s*\n";
            var classPattern = @"class\s+(\w+)(?:\([^)]*\))?\s*:\s*\n((?:\s{4,}.*\n?)*)";

            // Find decorator positions
            var decoratorMatches = Regex.Matches(content, decoratorPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (Match decoratorMatch in decoratorMatches)
            {
                // Look for class definition after decorator
                var searchStart = decoratorMatch.Index + decoratorMatch.Length;
                var remainingContent = content.Substring(searchStart);

                var classMatch = Regex.Match(remainingContent, classPattern, RegexOptions.Multiline);
                if (classMatch.Success)
                {
                    // Extract decorators above the class
                    var decorators = ExtractDecoratorsAboveClass(content, decoratorMatch.Index);

                    matches.Add(new PythonClassMatch
                    {
                        ClassName = classMatch.Groups[1].Value,
                        ClassDefinition = classMatch.Value,
                        ClassBody = classMatch.Groups[2].Value,
                        Decorators = decorators,
                        StartIndex = decoratorMatch.Index
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
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    break;
                }
            }

            return decorators;
        }

        private List<PythonFieldMatch> ExtractFieldsWithDecorators(string classBody)
        {
            var fields = new List<PythonFieldMatch>();
            var lines = classBody.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Look for field decorators
                if (line.StartsWith("@"))
                {
                    var decorators = new List<string> { line };

                    // Collect consecutive decorators
                    while (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith("@"))
                    {
                        i++;
                        decorators.Add(lines[i].Trim());
                    }

                    // Next line should be field definition
                    if (i + 1 < lines.Length)
                    {
                        i++;
                        var fieldLine = lines[i].Trim();

                        // Match field definition: name: type = value
                        var fieldMatch = Regex.Match(fieldLine, @"(\w+)\s*:\s*([^=]+)(?:\s*=\s*(.+))?");
                        if (fieldMatch.Success)
                        {
                            fields.Add(new PythonFieldMatch
                            {
                                Name = fieldMatch.Groups[1].Value,
                                TypeHint = fieldMatch.Groups[2].Value.Trim(),
                                DefaultValue = fieldMatch.Groups[3].Success ? fieldMatch.Groups[3].Value.Trim() : null,
                                Decorators = decorators,
                                FullDefinition = string.Join("\n", decorators) + "\n" + fieldLine
                            });
                        }
                    }
                }
                // Also look for simple type-hinted fields without decorators
                else if (Regex.IsMatch(line, @"^\w+\s*:\s*[^=]+"))
                {
                    var fieldMatch = Regex.Match(line, @"(\w+)\s*:\s*([^=]+)(?:\s*=\s*(.+))?");
                    if (fieldMatch.Success)
                    {
                        fields.Add(new PythonFieldMatch
                        {
                            Name = fieldMatch.Groups[1].Value,
                            TypeHint = fieldMatch.Groups[2].Value.Trim(),
                            DefaultValue = fieldMatch.Groups[3].Success ? fieldMatch.Groups[3].Value.Trim() : null,
                            Decorators = new List<string>(),
                            FullDefinition = line
                        });
                    }
                }
            }

            return fields;
        }

        private DiscoveredProperty? ParseFieldDecorators(PythonFieldMatch field, string fullContent)
        {
            try
            {
                var property = new DiscoveredProperty
                {
                    Name = field.Name,
                    Type = ExtractPythonType(field.TypeHint),
                    SqlType = "NVARCHAR",
                    IsNullable = IsNullableType(field.TypeHint),
                    IsPrimaryKey = field.Name.ToLowerInvariant() == "id",
                    IsForeignKey = field.Name.ToLowerInvariant().EndsWith("_id") && field.Name.ToLowerInvariant() != "id",
                    IsUnique = false,
                    IsIndexed = false,
                    DefaultValue = field.DefaultValue,
                    Attributes = new Dictionary<string, object>()
                };

                // Parse decorators
                foreach (var decorator in field.Decorators)
                {
                    ParsePythonDecorator(decorator, property);
                }

                // Infer SQL type from Python type if not explicitly set
                if (property.SqlType == "NVARCHAR")
                {
                    property.SqlType = MapPythonTypeToSql(property.Type);
                }

                return property;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse field: {FieldName}", field.Name);
                return null;
            }
        }

        private void ParsePythonDecorator(string decorator, DiscoveredProperty property)
        {
            // @sql_type(SqlDataType.NVARCHAR, length=100)
            var sqlTypeMatch = Regex.Match(decorator, @"@sql_type\s*\(\s*SqlDataType\.(\w+)(?:\s*,\s*length\s*=\s*(\d+))?(?:\s*,\s*precision\s*=\s*(\d+))?(?:\s*,\s*scale\s*=\s*(\d+))?\s*\)", RegexOptions.IgnoreCase);
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

            // @sql_constraints(SqlConstraint.NOT_NULL, SqlConstraint.UNIQUE)
            var constraintsMatch = Regex.Match(decorator, @"@sql_constraints\s*\(\s*([^)]+)\s*\)", RegexOptions.IgnoreCase);
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

            // @sql_foreign_key_to(User, referenced_property='id', on_delete=ForeignKeyAction.CASCADE)
            var fkMatch = Regex.Match(decorator,
                @"@sql_foreign_key_to\s*\(\s*(\w+)(?:\s*,\s*referenced_property\s*=\s*[""']([^""']+)[""'])?(?:\s*,\s*on_delete\s*=\s*([^,)]+))?(?:\s*,\s*on_update\s*=\s*([^,)]+))?(?:\s*,\s*name\s*=\s*[""']([^""']+)[""'])?\s*\)",
                RegexOptions.IgnoreCase);

            if (fkMatch.Success)
            {
                property.IsForeignKey = true;
                property.Attributes["foreign_key_referenced_entity"] = fkMatch.Groups[1].Value;
                property.Attributes["foreign_key_referenced_column"] = fkMatch.Groups[2].Success ? fkMatch.Groups[2].Value : "id";

                if (fkMatch.Groups[3].Success)
                {
                    property.Attributes["foreign_key_on_delete"] = ExtractForeignKeyAction(fkMatch.Groups[3].Value);
                }

                if (fkMatch.Groups[4].Success)
                {
                    property.Attributes["foreign_key_on_update"] = ExtractForeignKeyAction(fkMatch.Groups[4].Value);
                }

                if (fkMatch.Groups[5].Success)
                {
                    property.Attributes["foreign_key_name"] = fkMatch.Groups[5].Value;
                }
            }
        }

        private string ExtractClassName(string classDefinition)
        {
            var match = Regex.Match(classDefinition, @"class\s+(\w+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "Unknown";
        }

        private string ExtractFullClassName(string content, string className)
        {
            var moduleName = ExtractModuleNameFromContent(content);
            return string.IsNullOrEmpty(moduleName) ? className : $"{moduleName}.{className}";
        }

        private string ExtractModuleName(string content, string filePath)
        {
            // Try to extract from file path structure
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var directory = Path.GetDirectoryName(filePath);

            // Look for __init__.py to determine package structure
            if (directory != null)
            {
                var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var packageParts = new List<string>();

                // Build package name from directory structure
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    var part = parts[i];
                    if (File.Exists(Path.Combine(string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(i + 1)), "__init__.py")))
                    {
                        packageParts.Insert(0, part);
                    }
                    else
                    {
                        break;
                    }
                }

                if (packageParts.Any())
                {
                    return string.Join(".", packageParts) + (fileName != "__init__" ? $".{fileName}" : "");
                }
            }

            return fileName;
        }

        private string ExtractModuleNameFromContent(string content)
        {
            // Look for module docstring or explicit module declarations
            var moduleMatch = Regex.Match(content, @"^[""']{3}\s*Module:\s*([^\n""']+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return moduleMatch.Success ? moduleMatch.Groups[1].Value.Trim() : string.Empty;
        }

        private string ExtractTableName(List<string> decorators, string className)
        {
            // Look for @sql_table("table_name") decorator
            foreach (var decorator in decorators)
            {
                var match = Regex.Match(decorator, @"@sql_table\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            // Default to snake_case class name
            return ToSnakeCase(className);
        }

        private string ExtractSchemaName(List<string> decorators)
        {
            // Look for @sql_schema("schema_name") decorator
            foreach (var decorator in decorators)
            {
                var match = Regex.Match(decorator, @"@sql_schema\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return "dbo"; // Default schema
        }

        private int GetClassLineNumber(string content, string className)
        {
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], $@"class\s+{Regex.Escape(className)}\b", RegexOptions.IgnoreCase))
                {
                    return i + 1;
                }
            }
            return 1;
        }

        private string DetermineClassType(string classDefinition)
        {
            if (classDefinition.Contains("(") && classDefinition.Contains(")"))
            {
                var match = Regex.Match(classDefinition, @"class\s+\w+\s*\(\s*([^)]+)\s*\)");
                if (match.Success)
                {
                    var baseClasses = match.Groups[1].Value;
                    if (baseClasses.Contains("dataclass") || baseClasses.Contains("BaseModel"))
                    {
                        return "dataclass";
                    }
                    if (baseClasses.Contains("SQLAlchemy") || baseClasses.Contains("Base"))
                    {
                        return "sqlalchemy";
                    }
                }
            }

            return "class";
        }

        private string ExtractPythonType(string typeHint)
        {
            // Clean up type hint
            var cleanType = typeHint.Replace("Optional[", "").Replace("]", "").Replace("Union[", "").Trim();

            // Handle generic types
            if (cleanType.Contains("["))
            {
                cleanType = cleanType.Substring(0, cleanType.IndexOf('['));
            }

            return cleanType.Split(',')[0].Trim();
        }

        private bool IsNullableType(string typeHint)
        {
            return typeHint.Contains("Optional[") ||
                   typeHint.Contains("Union[") && typeHint.Contains("None") ||
                   typeHint.Contains("| None");
        }

        private string MapPythonTypeToSql(string pythonType)
        {
            return pythonType.ToLowerInvariant() switch
            {
                "str" or "string" => "NVARCHAR",
                "int" or "integer" => "INT",
                "float" => "FLOAT",
                "decimal" => "DECIMAL",
                "bool" or "boolean" => "BIT",
                "datetime" => "DATETIME2",
                "date" => "DATE",
                "time" => "TIME",
                "uuid" => "UNIQUEIDENTIFIER",
                "bytes" => "VARBINARY",
                _ => "NVARCHAR"
            };
        }

        private string ExtractForeignKeyAction(string actionText)
        {
            if (actionText.Contains("CASCADE"))
                return "CASCADE";
            if (actionText.Contains("SET_NULL"))
                return "SET_NULL";
            if (actionText.Contains("SET_DEFAULT"))
                return "SET_DEFAULT";
            return "NO_ACTION";
        }

        private string ToSnakeCase(string pascalCase)
        {
            return Regex.Replace(pascalCase, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        }

        private bool IsTestFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            var directory = Path.GetDirectoryName(filePath)?.ToLowerInvariant() ?? "";

            return fileName.StartsWith("test_") ||
                   fileName.EndsWith("_test.py") ||
                   directory.Contains("test") ||
                   directory.Contains("tests");
        }

        private bool IsInitFile(string filePath)
        {
            return Path.GetFileName(filePath).ToLowerInvariant() == "__init__.py";
        }

        // Helper classes for parsing
        private class PythonClassMatch
        {
            public string ClassName { get; set; } = string.Empty;
            public string ClassDefinition { get; set; } = string.Empty;
            public string ClassBody { get; set; } = string.Empty;
            public List<string> Decorators { get; set; } = new();
            public int StartIndex { get; set; }
        }

        private class PythonFieldMatch
        {
            public string Name { get; set; } = string.Empty;
            public string TypeHint { get; set; } = string.Empty;
            public string? DefaultValue { get; set; }
            public List<string> Decorators { get; set; } = new();
            public string FullDefinition { get; set; } = string.Empty;
        }
    }
}