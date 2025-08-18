using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ICSharpAnalyzerService : ILanguageAnalyzer
    {
        Task<List<Assembly>> LoadAssembliesAsync(string sourcePath);
        Task<List<DiscoveredEntity>> AnalyzeAssemblyAsync(Assembly assembly, string trackAttribute);
    }

    public class CSharpAnalyzerService : ICSharpAnalyzerService
    {
        private readonly ILogger<CSharpAnalyzerService> _logger;

        public CSharpAnalyzerService(ILogger<CSharpAnalyzerService> logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredEntity>> DiscoverEntitiesAsync(string sourcePath, string trackAttribute)
        {
            _logger.LogInformation("Analyzing C# assemblies in: {SourcePath}", sourcePath);

            var entities = new List<DiscoveredEntity>();

            try
            {
                var assemblies = await LoadAssembliesAsync(sourcePath);
                _logger.LogDebug("Loaded {AssemblyCount} assemblies", assemblies.Count);

                foreach (var assembly in assemblies)
                {
                    var assemblyEntities = await AnalyzeAssemblyAsync(assembly, trackAttribute);
                    entities.AddRange(assemblyEntities);
                }

                _logger.LogInformation("✓ C# analysis complete: {EntityCount} entities discovered", entities.Count);
                return entities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "C# entity discovery failed");
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure,
                    $"C# entity discovery failed: {ex.Message}", ex);
            }
        }

        public async Task<List<Assembly>> LoadAssembliesAsync(string sourcePath)
        {
            var assemblies = new List<Assembly>();

            try
            {
                // Look for .dll and .exe files
                var assemblyFiles = Directory.GetFiles(sourcePath, "*.dll", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(sourcePath, "*.exe", SearchOption.AllDirectories))
                    .Where(f => !IsSystemAssembly(f))
                    .ToList();

                _logger.LogDebug("Found {FileCount} assembly files", assemblyFiles.Count);

                foreach (var file in assemblyFiles)
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(file);
                        assemblies.Add(assembly);
                        _logger.LogTrace("Loaded assembly: {AssemblyName}", assembly.GetName().Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load assembly: {FileName}", file);
                    }
                }

                return assemblies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load assemblies from: {SourcePath}", sourcePath);
                throw;
            }
        }

        public async Task<List<DiscoveredEntity>> AnalyzeAssemblyAsync(Assembly assembly, string trackAttribute)
        {
            var entities = new List<DiscoveredEntity>();

            try
            {
                var types = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && HasTrackingAttribute(t, trackAttribute))
                    .ToList();

                foreach (var type in types)
                {
                    var entity = await AnalyzeTypeAsync(type, trackAttribute);
                    if (entity != null)
                    {
                        entities.Add(entity);
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logger.LogWarning("Failed to load some types from assembly {AssemblyName}: {LoaderExceptions}",
                    assembly.GetName().Name, string.Join(", ", ex.LoaderExceptions.Select(e => e?.Message)));

                var validTypes = ex.Types.Where(t => t != null && HasTrackingAttribute(t, trackAttribute));
                foreach (var type in validTypes)
                {
                    var entity = await AnalyzeTypeAsync(type!, trackAttribute);
                    if (entity != null)
                    {
                        entities.Add(entity);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze assembly: {AssemblyName}", assembly.GetName().Name);
            }

            return entities;
        }

        private async Task<DiscoveredEntity?> AnalyzeTypeAsync(Type type, string trackAttribute)
        {
            try
            {
                var entity = new DiscoveredEntity
                {
                    Name = type.Name,
                    FullName = type.FullName ?? type.Name,
                    Namespace = type.Namespace ?? string.Empty,
                    TableName = GetTableName(type),
                    SchemaName = GetSchemaName(type),
                    SourceFile = GetSourceFileName(type),
                    Properties = new List<DiscoveredProperty>(),
                    Indexes = new List<DiscoveredIndex>(),
                    Relationships = new List<DiscoveredRelationship>(),
                    Attributes = new Dictionary<string, object>
                    {
                        ["track_attribute"] = trackAttribute,
                        ["language"] = "csharp",
                        ["assembly"] = type.Assembly.GetName().Name ?? "Unknown"
                    }
                };

                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var property in properties)
                {
                    var discoveredProperty = AnalyzeProperty(property);
                    if (discoveredProperty != null)
                    {
                        entity.Properties.Add(discoveredProperty);
                    }
                }

                AnalyzeRelationships(type, entity);
                AnalyzeIndexes(type, entity);

                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze type: {TypeName}", type.FullName);
                return null;
            }
        }

        private DiscoveredProperty? AnalyzeProperty(PropertyInfo property)
        {
            try
            {
                var propertyType = property.PropertyType;
                var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                var discoveredProperty = new DiscoveredProperty
                {
                    Name = property.Name,
                    Type = GetSimpleTypeName(underlyingType),
                    SqlType = MapCSharpTypeToSql(underlyingType),
                    IsNullable = propertyType != underlyingType || !propertyType.IsValueType,
                    IsPrimaryKey = HasAttribute(property, "Key") || HasAttribute(property, "KeyAttribute") ||
                                   property.Name.Equals("Id", StringComparison.OrdinalIgnoreCase),
                    IsForeignKey = property.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                                   !property.Name.Equals("Id", StringComparison.OrdinalIgnoreCase),
                    IsUnique = HasAttribute(property, "Unique") || HasAttribute(property, "UniqueAttribute"),
                    IsIndexed = HasAttribute(property, "Index") || HasAttribute(property, "IndexAttribute"),
                    Attributes = new Dictionary<string, object>()
                };

                // Enhanced attribute analysis for SQL-specific attributes
                AnalyzePropertyAttributes(property, discoveredProperty);
                AnalyzeForeignKeyAttributes(property, discoveredProperty);

                return discoveredProperty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to analyze property: {PropertyName}", property.Name);
                return null;
            }
        }

        private void AnalyzePropertyAttributes(PropertyInfo property, DiscoveredProperty discoveredProperty)
        {
            foreach (var attr in property.GetCustomAttributes(false))
            {
                var attrType = attr.GetType();

                // SqlType attribute
                if (attrType.Name.Contains("SqlType"))
                {
                    var dataTypeProperty = attrType.GetProperty("DataType");
                    if (dataTypeProperty != null)
                    {
                        var dataType = dataTypeProperty.GetValue(attr);
                        discoveredProperty.SqlType = dataType?.ToString() ?? discoveredProperty.SqlType;
                    }

                    var lengthProperty = attrType.GetProperty("Length");
                    if (lengthProperty != null)
                    {
                        discoveredProperty.MaxLength = lengthProperty.GetValue(attr) as int?;
                    }

                    var precisionProperty = attrType.GetProperty("Precision");
                    if (precisionProperty != null)
                    {
                        discoveredProperty.Precision = precisionProperty.GetValue(attr) as int?;
                    }

                    var scaleProperty = attrType.GetProperty("Scale");
                    if (scaleProperty != null)
                    {
                        discoveredProperty.Scale = scaleProperty.GetValue(attr) as int?;
                    }
                }

                // SqlConstraints attribute
                if (attrType.Name.Contains("SqlConstraints"))
                {
                    var constraintsProperty = attrType.GetProperty("Constraints");
                    if (constraintsProperty != null)
                    {
                        var constraints = constraintsProperty.GetValue(attr) as Array;
                        if (constraints != null)
                        {
                            foreach (var constraint in constraints)
                            {
                                var constraintName = constraint.ToString();
                                switch (constraintName)
                                {
                                    case "NotNull":
                                        discoveredProperty.IsNullable = false;
                                        break;
                                    case "Unique":
                                        discoveredProperty.IsUnique = true;
                                        break;
                                    case "PrimaryKey":
                                        discoveredProperty.IsPrimaryKey = true;
                                        break;
                                    case "Identity":
                                        discoveredProperty.Attributes["is_identity"] = true;
                                        break;
                                }
                            }
                        }
                    }
                }

                // Standard Entity Framework attributes
                if (attrType.Name.Contains("StringLength") || attrType.Name.Contains("MaxLength"))
                {
                    var lengthProperty = attrType.GetProperty("Length") ?? attrType.GetProperty("MaximumLength");
                    if (lengthProperty != null)
                    {
                        discoveredProperty.MaxLength = (int?)lengthProperty.GetValue(attr);
                    }
                }

                if (attrType.Name.Contains("Required"))
                {
                    discoveredProperty.IsNullable = false;
                }

                if (attrType.Name.Contains("Column"))
                {
                    var nameProperty = attrType.GetProperty("Name");
                    if (nameProperty != null)
                    {
                        var columnName = nameProperty.GetValue(attr) as string;
                        if (!string.IsNullOrEmpty(columnName))
                        {
                            discoveredProperty.Attributes["column_name"] = columnName;
                        }
                    }

                    var typeProperty = attrType.GetProperty("TypeName");
                    if (typeProperty != null)
                    {
                        var typeName = typeProperty.GetValue(attr) as string;
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            discoveredProperty.SqlType = typeName;
                        }
                    }
                }

                if (attrType.Name.Contains("DefaultValue"))
                {
                    var valueProperty = attrType.GetProperty("Value");
                    if (valueProperty != null)
                    {
                        discoveredProperty.DefaultValue = valueProperty.GetValue(attr)?.ToString();
                    }
                }
            }
        }

        private void AnalyzeForeignKeyAttributes(PropertyInfo property, DiscoveredProperty discoveredProperty)
        {
            foreach (var attr in property.GetCustomAttributes(false))
            {
                var attrType = attr.GetType();

                // Enhanced SqlForeignKeyTo<T> attribute
                if (attrType.Name.Contains("SqlForeignKeyTo") && attrType.IsGenericType)
                {
                    var referencedEntityType = attrType.GetGenericArguments().FirstOrDefault();
                    if (referencedEntityType != null)
                    {
                        discoveredProperty.IsForeignKey = true;
                        discoveredProperty.Attributes["foreign_key_referenced_entity"] = referencedEntityType.Name;
                        discoveredProperty.Attributes["foreign_key_referenced_table"] = GetTableName(referencedEntityType);

                        // Get referenced property expression
                        var referencedPropertyProperty = attrType.GetProperty("ReferencedPropertyExpression");
                        if (referencedPropertyProperty != null)
                        {
                            var referencedProperty = referencedPropertyProperty.GetValue(attr) as string;
                            discoveredProperty.Attributes["foreign_key_referenced_column"] = referencedProperty ?? "Id";
                        }

                        // Get cascade actions
                        var onDeleteProperty = attrType.GetProperty("OnDelete");
                        if (onDeleteProperty != null)
                        {
                            var onDelete = onDeleteProperty.GetValue(attr);
                            discoveredProperty.Attributes["foreign_key_on_delete"] = onDelete?.ToString() ?? "NoAction";
                        }

                        var onUpdateProperty = attrType.GetProperty("OnUpdate");
                        if (onUpdateProperty != null)
                        {
                            var onUpdate = onUpdateProperty.GetValue(attr);
                            discoveredProperty.Attributes["foreign_key_on_update"] = onUpdate?.ToString() ?? "NoAction";
                        }

                        var nameProperty = attrType.GetProperty("Name");
                        if (nameProperty != null)
                        {
                            var name = nameProperty.GetValue(attr) as string;
                            if (!string.IsNullOrEmpty(name))
                            {
                                discoveredProperty.Attributes["foreign_key_name"] = name;
                            }
                        }
                    }
                }

                // Standard ForeignKey attribute
                if (attrType.Name.Contains("ForeignKey"))
                {
                    var nameProperty = attrType.GetProperty("Name");
                    if (nameProperty != null)
                    {
                        var foreignKeyProperty = nameProperty.GetValue(attr) as string;
                        if (!string.IsNullOrEmpty(foreignKeyProperty))
                        {
                            discoveredProperty.IsForeignKey = true;
                            discoveredProperty.Attributes["foreign_key_property"] = foreignKeyProperty;
                        }
                    }
                }
            }
        }

        private void AnalyzeRelationships(Type type, DiscoveredEntity entity)
        {
            var navigationProperties = type.GetProperties()
                .Where(p => IsNavigationProperty(p))
                .ToList();

            foreach (var navProperty in navigationProperties)
            {
                var relationship = new DiscoveredRelationship
                {
                    Name = navProperty.Name,
                    Type = GetRelationshipType(navProperty),
                    ReferencedEntity = GetReferencedEntityName(navProperty),
                    ReferencedTable = GetReferencedEntityName(navProperty),
                    ForeignKeyColumns = GetForeignKeyColumns(navProperty),
                    Attributes = new Dictionary<string, object>
                    {
                        ["navigation_property"] = true,
                        ["property_type"] = navProperty.PropertyType.Name
                    }
                };

                entity.Relationships.Add(relationship);
            }
        }

        private void AnalyzeIndexes(Type type, DiscoveredEntity entity)
        {
            foreach (var indexAttr in type.GetCustomAttributes(false).Where(a => a.GetType().Name.Contains("Index")))
            {
                var attrType = indexAttr.GetType();
                var nameProperty = attrType.GetProperty("Name");
                var columnsProperty = attrType.GetProperty("ColumnNames") ?? attrType.GetProperty("PropertyNames");
                var isUniqueProperty = attrType.GetProperty("IsUnique");

                var index = new DiscoveredIndex
                {
                    Name = nameProperty?.GetValue(indexAttr) as string ?? $"IX_{entity.Name}",
                    Columns = ParseIndexColumns(columnsProperty?.GetValue(indexAttr)),
                    IsUnique = (bool?)isUniqueProperty?.GetValue(indexAttr) ?? false,
                    IsClustered = false,
                    Attributes = new Dictionary<string, object>
                    {
                        ["from_attribute"] = true
                    }
                };

                entity.Indexes.Add(index);
            }
        }

        private bool HasTrackingAttribute(Type type, string trackAttribute)
        {
            return type.GetCustomAttributes(false)
                .Any(attr =>
                    // Exact name match
                    attr.GetType().Name.Equals(trackAttribute, StringComparison.OrdinalIgnoreCase) ||
                    // Full name match (for fully qualified attributes)
                    attr.GetType().FullName?.Equals(trackAttribute, StringComparison.OrdinalIgnoreCase) == true ||
                    // Name contains match (for backward compatibility)
                    attr.GetType().Name.Equals($"{trackAttribute}Attribute", StringComparison.OrdinalIgnoreCase));
        }

        private bool HasAttribute(PropertyInfo property, string attributeName)
        {
            return property.GetCustomAttributes(false)
                .Any(attr => attr.GetType().Name.Contains(attributeName, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsNavigationProperty(PropertyInfo property)
        {
            var type = property.PropertyType;

            if (type.IsClass && type != typeof(string) && !type.IsPrimitive)
            {
                return true;
            }

            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                return genericTypeDefinition == typeof(ICollection<>) ||
                       genericTypeDefinition == typeof(IList<>) ||
                       genericTypeDefinition == typeof(List<>) ||
                       genericTypeDefinition == typeof(IEnumerable<>);
            }

            return false;
        }

        private bool IsSystemAssembly(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            return fileName.StartsWith("microsoft.") ||
                   fileName.StartsWith("system.") ||
                   fileName.StartsWith("mscorlib") ||
                   fileName.StartsWith("netstandard") ||
                   fileName.StartsWith("runtime.");
        }

        private string GetSimpleTypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(double)) return "double";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(DateTime)) return "DateTime";
            if (type == typeof(Guid)) return "Guid";
            if (type == typeof(byte[])) return "byte[]";

            return type.Name;
        }

        private string MapCSharpTypeToSql(Type type)
        {
            if (type == typeof(string)) return "NVARCHAR";
            if (type == typeof(int)) return "INT";
            if (type == typeof(long)) return "BIGINT";
            if (type == typeof(short)) return "SMALLINT";
            if (type == typeof(byte)) return "TINYINT";
            if (type == typeof(decimal)) return "DECIMAL";
            if (type == typeof(double)) return "FLOAT";
            if (type == typeof(float)) return "REAL";
            if (type == typeof(bool)) return "BIT";
            if (type == typeof(DateTime)) return "DATETIME2";
            if (type == typeof(DateTimeOffset)) return "DATETIMEOFFSET";
            if (type == typeof(Guid)) return "UNIQUEIDENTIFIER";
            if (type == typeof(byte[])) return "VARBINARY";

            return "NVARCHAR";
        }

        private string GetTableName(Type type)
        {
            var tableAttr = type.GetCustomAttributes(false)
                .FirstOrDefault(a => a.GetType().Name.Contains("Table") || a.GetType().Name.Contains("SqlTable"));

            if (tableAttr != null)
            {
                var nameProperty = tableAttr.GetType().GetProperty("Name") ?? tableAttr.GetType().GetProperty("TableName");
                if (nameProperty != null)
                {
                    var tableName = nameProperty.GetValue(tableAttr) as string;
                    if (!string.IsNullOrEmpty(tableName))
                        return tableName;
                }
            }

            return type.Name;
        }

        private string GetSchemaName(Type type)
        {
            var schemaAttr = type.GetCustomAttributes(false)
                .FirstOrDefault(a => a.GetType().Name.Contains("Schema") || a.GetType().Name.Contains("SqlSchema"));

            if (schemaAttr != null)
            {
                var nameProperty = schemaAttr.GetType().GetProperty("Name") ?? schemaAttr.GetType().GetProperty("SchemaName");
                if (nameProperty != null)
                {
                    var schemaName = nameProperty.GetValue(schemaAttr) as string;
                    if (!string.IsNullOrEmpty(schemaName))
                        return schemaName;
                }
            }

            return "dbo";
        }

        private string GetSourceFileName(Type type)
        {
            try
            {
                return type.Assembly.Location;
            }
            catch
            {
                return $"{type.Assembly.GetName().Name}.dll";
            }
        }

        private string GetRelationshipType(PropertyInfo property)
        {
            var type = property.PropertyType;

            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(ICollection<>) ||
                    genericTypeDefinition == typeof(IList<>) ||
                    genericTypeDefinition == typeof(List<>) ||
                    genericTypeDefinition == typeof(IEnumerable<>))
                {
                    return "OneToMany";
                }
            }

            return "ManyToOne";
        }

        private string GetReferencedEntityName(PropertyInfo property)
        {
            var type = property.PropertyType;

            if (type.IsGenericType)
            {
                return type.GetGenericArguments().FirstOrDefault()?.Name ?? "Unknown";
            }

            return type.Name;
        }

        private List<string> GetForeignKeyColumns(PropertyInfo property)
        {
            // This would be enhanced to analyze foreign key attributes
            return new List<string> { $"{GetReferencedEntityName(property)}Id" };
        }

        private List<string> ParseIndexColumns(object? columnsValue)
        {
            if (columnsValue == null) return new List<string>();

            if (columnsValue is string columnString)
            {
                return columnString.Split(',').Select(c => c.Trim()).ToList();
            }

            if (columnsValue is string[] columnArray)
            {
                return columnArray.ToList();
            }

            return new List<string>();
        }
    }
}