using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Discovery;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.TypeMapping
{
    /// <summary>
    /// Analyzes discovered types and maps them to their platform-specific equivalents for code generation.
    /// </summary>
    public class TypeMappingEngine
    {
        private readonly ILogger<TypeMappingEngine> _logger;
        private readonly Dictionary<string, Dictionary<string, string>> _builtInMappings;

        public TypeMappingEngine(ILogger<TypeMappingEngine> logger)
        {
            _logger = logger;
            _builtInMappings = InitializeBuiltInMappings();
        }

        /// <summary>
        /// Analyzes all unique types discovered in the source code and creates corresponding platform-specific type mappings.
        /// </summary>
        /// <param name="discoveredClasses">The list of classes discovered from the source code.</param>
        /// <param name="config">The application's configuration.</param>
        /// <returns>A dictionary mapping source type names to their detailed TypeMappingInfo.</returns>
        public async Task<Dictionary<string, TypeMappingInfo>> AnalyzeTypeMappingsAsync(
            List<DiscoveredClass> discoveredClasses,
            GeneratorConfiguration config)
        {
            var typeMappings = new Dictionary<string, TypeMappingInfo>();
            var targetPlatform = config.GetSelectedPlatform().ToString().ToLower();

            try
            {
                _logger.LogInformation("Analyzing type mappings for the '{Platform}' platform...", targetPlatform);

                var customMappings = LoadCustomTypeMappings(config.TypeMapping.CustomTypeMappings);
                var allTypes = CollectAllTypes(discoveredClasses);

                _logger.LogDebug("Found {TypeCount} unique types to map.", allTypes.Count);

                foreach (var sourceType in allTypes)
                {
                    var mappingInfo = await CreateTypeMappingAsync(sourceType, targetPlatform, customMappings, config);
                    if (mappingInfo != null)
                    {
                        typeMappings[sourceType] = mappingInfo;
                    }
                }

                ValidateTypeMappings(typeMappings);

                _logger.LogInformation("✓ Type mapping analysis completed. Mapped {MappingCount} types.", typeMappings.Count);
                return typeMappings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A critical error occurred during type mapping analysis.");
                throw new MobileAdapterException(MobileAdapterExitCode.TypeMappingFailure, "Type mapping analysis failed.", ex);
            }
        }

        #region Private Initialization and Collection Methods

        private Dictionary<string, Dictionary<string, string>> InitializeBuiltInMappings()
        {
            // Mappings are identical to the previous version and are omitted for brevity.
            // This structure allows for easy expansion to new platforms.
            return new Dictionary<string, Dictionary<string, string>>
            {
                ["android"] = new Dictionary<string, string>
                {
                    // C# to Kotlin
                    ["string"] = "String",
                    ["String"] = "String",
                    ["int"] = "Int",
                    ["Int32"] = "Int",
                    ["long"] = "Long",
                    ["Int64"] = "Long",
                    ["short"] = "Short",
                    ["Int16"] = "Short",
                    ["byte"] = "Byte",
                    ["Byte"] = "Byte",
                    ["bool"] = "Boolean",
                    ["Boolean"] = "Boolean",
                    ["float"] = "Float",
                    ["Single"] = "Float",
                    ["double"] = "Double",
                    ["Double"] = "Double",
                    ["decimal"] = "BigDecimal",
                    ["Decimal"] = "BigDecimal",
                    ["DateTime"] = "LocalDateTime",
                    ["DateTimeOffset"] = "OffsetDateTime",
                    ["TimeSpan"] = "Duration",
                    ["Guid"] = "UUID",
                    ["Uri"] = "Uri",
                    ["byte[]"] = "ByteArray",
                    ["char"] = "Char",
                    ["Char"] = "Char",
                    ["object"] = "Any",
                    ["Object"] = "Any",
                    ["void"] = "Unit",
                    ["Void"] = "Unit",
                    ["List<T>"] = "List<T>",
                    ["IList<T>"] = "MutableList<T>",
                    ["IEnumerable<T>"] = "List<T>",
                    ["ICollection<T>"] = "MutableList<T>",
                    ["Dictionary<TKey,TValue>"] = "Map<TKey, TValue>",
                    ["IDictionary<TKey,TValue>"] = "MutableMap<TKey, TValue>",
                    ["HashSet<T>"] = "Set<T>",
                    ["ISet<T>"] = "MutableSet<T>",
                    ["Queue<T>"] = "Queue<T>",
                    ["Stack<T>"] = "Stack<T>",
                    ["Task"] = "Unit",
                    ["Task<T>"] = "T",
                    // TypeScript/JS to Kotlin
                    ["number"] = "Double",
                    ["boolean"] = "Boolean",
                    ["any"] = "Any",
                    ["Array<T>"] = "List<T>",
                    ["Promise<T>"] = "T",
                    // Python to Kotlin
                    ["str"] = "String",
                    ["int"] = "Long",
                    ["float"] = "Double",
                    ["bool"] = "Boolean",
                    ["list"] = "List<Any>",
                    ["dict"] = "Map<String, Any>",
                    ["None"] = "Unit"
                },
                ["ios"] = new Dictionary<string, string>
                {
                    // C# to Swift
                    ["string"] = "String",
                    ["String"] = "String",
                    ["int"] = "Int32",
                    ["Int32"] = "Int32",
                    ["long"] = "Int64",
                    ["Int64"] = "Int64",
                    ["short"] = "Int16",
                    ["Int16"] = "Int16",
                    ["byte"] = "UInt8",
                    ["Byte"] = "UInt8",
                    ["bool"] = "Bool",
                    ["Boolean"] = "Bool",
                    ["float"] = "Float",
                    ["Single"] = "Float",
                    ["double"] = "Double",
                    ["Double"] = "Double",
                    ["decimal"] = "Decimal",
                    ["Decimal"] = "Decimal",
                    ["DateTime"] = "Date",
                    ["DateTimeOffset"] = "Date",
                    ["TimeSpan"] = "TimeInterval",
                    ["Guid"] = "UUID",
                    ["Uri"] = "URL",
                    ["byte[]"] = "Data",
                    ["char"] = "Character",
                    ["Char"] = "Character",
                    ["object"] = "Any",
                    ["Object"] = "Any",
                    ["void"] = "Void",
                    ["Void"] = "Void",
                    ["List<T>"] = "[T]",
                    ["IList<T>"] = "[T]",
                    ["IEnumerable<T>"] = "[T]",
                    ["ICollection<T>"] = "[T]",
                    ["Dictionary<TKey,TValue>"] = "[TKey: TValue]",
                    ["IDictionary<TKey,TValue>"] = "[TKey: TValue]",
                    ["HashSet<T>"] = "Set<T>",
                    ["ISet<T>"] = "Set<T>",
                    ["Task"] = "Void",
                    ["Task<T>"] = "T",
                    // TypeScript/JS to Swift
                    ["number"] = "Double",
                    ["boolean"] = "Bool",
                    ["any"] = "Any",
                    ["Array<T>"] = "[T]",
                    ["Promise<T>"] = "T",
                    // Python to Swift
                    ["str"] = "String",
                    ["int"] = "Int64",
                    ["float"] = "Double",
                    ["bool"] = "Bool",
                    ["list"] = "[Any]",
                    ["dict"] = "[String: Any]",
                    ["None"] = "Void"
                }
            };
        }

        private Dictionary<string, Dictionary<string, string>> LoadCustomTypeMappings(string customMappingsJson)
        {
            if (string.IsNullOrWhiteSpace(customMappingsJson)) return new Dictionary<string, Dictionary<string, string>>();

            try
            {
                var mappings = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(customMappingsJson);
                _logger.LogDebug("Loaded {MappingCount} custom type mappings.", mappings.Count);
                return mappings;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse custom type mappings JSON. It will be ignored.");
                return new Dictionary<string, Dictionary<string, string>>();
            }
        }

        private HashSet<string> CollectAllTypes(List<DiscoveredClass> discoveredClasses)
        {
            var types = new HashSet<string>();
            foreach (var cls in discoveredClasses)
            {
                foreach (var prop in cls.Properties)
                {
                    types.Add(prop.Type);
                    if (!string.IsNullOrEmpty(prop.CollectionElementType)) types.Add(prop.CollectionElementType);
                }
                foreach (var method in cls.Methods)
                {
                    types.Add(method.ReturnType);
                    foreach (var param in method.Parameters) types.Add(param.Type);
                }
            }
            types.RemoveWhere(string.IsNullOrWhiteSpace);
            return types;
        }

        #endregion

        #region Private Mapping Logic

        private async Task<TypeMappingInfo> CreateTypeMappingAsync(
            string sourceType,
            string targetPlatform,
            Dictionary<string, Dictionary<string, string>> customMappings,
            GeneratorConfiguration config)
        {
            var mappingInfo = new TypeMappingInfo
            {
                SourceType = sourceType,
                TargetPlatform = targetPlatform,
                IsNullable = IsNullableType(sourceType, config),
                IsCollection = IsCollectionType(sourceType)
            };

            var normalizedSourceType = NormalizeType(sourceType);

            // Resolution Order: Custom -> Built-in -> Generic -> Fallback
            if (customMappings.TryGetValue(sourceType, out var platformMappings) && platformMappings.TryGetValue(targetPlatform, out var customTarget))
            {
                mappingInfo.TargetType = customTarget;
                mappingInfo.MappingSource = TypeMappingSource.Custom;
            }
            else if (_builtInMappings.TryGetValue(targetPlatform, out var builtIn) && builtIn.TryGetValue(normalizedSourceType, out var builtInTarget))
            {
                mappingInfo.TargetType = builtInTarget;
                mappingInfo.MappingSource = TypeMappingSource.BuiltIn;
            }
            else if (TryMapGenericType(sourceType, targetPlatform, out var genericMapping))
            {
                mappingInfo.TargetType = genericMapping;
                mappingInfo.MappingSource = TypeMappingSource.Generic;
            }
            else
            {
                mappingInfo.TargetType = GetFallbackMapping(sourceType, targetPlatform);
                mappingInfo.MappingSource = TypeMappingSource.Fallback;
            }

            if (mappingInfo.IsNullable && config.TypeMapping.PreserveNullableTypes)
            {
                mappingInfo.TargetType = ApplyNullableWrapper(mappingInfo.TargetType, targetPlatform);
            }

            // This is an async method in the original file, so we keep the signature for future-proofing,
            // even though the current logic is synchronous.
            await Task.CompletedTask;

            return mappingInfo;
        }

        private string NormalizeType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return string.Empty;
            var normalized = type.TrimEnd('?').Replace("[]", "");
            if (normalized.Contains("<") && normalized.Contains(">"))
            {
                var baseName = normalized[..normalized.IndexOf('<')];
                var paramCount = normalized.Count(c => c == ',') + 1;
                var genericParams = string.Join(",", Enumerable.Repeat("T", paramCount).Select((t, i) => paramCount > 1 ? $"T{i + 1}" : t));
                return $"{baseName}<{genericParams}>";
            }
            return normalized;
        }

        private bool IsNullableType(string type, GeneratorConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(type) || type.EndsWith("?")) return true;
            if (config.GetSelectedLanguage() == SourceLanguage.CSharp)
            {
                var nonNullableValueTypes = new[] { "int", "long", "short", "byte", "float", "double", "decimal", "bool", "char", "DateTime", "Guid" };
                return !nonNullableValueTypes.Contains(type.Replace("System.", ""));
            }
            return true; // Default to nullable for other languages
        }

        private bool IsCollectionType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            var collectionIndicators = new[] { "List", "Array", "Collection", "Set", "Map", "Dictionary", "IEnumerable", "[]" };
            return collectionIndicators.Any(type.Contains);
        }

        private bool TryMapGenericType(string sourceType, string targetPlatform, out string targetType)
        {
            targetType = sourceType;
            if (!sourceType.Contains('<') || !sourceType.Contains('>')) return false;

            try
            {
                var genericStart = sourceType.IndexOf('<');
                var baseName = sourceType[..genericStart];
                var genericPart = sourceType[(genericStart + 1)..sourceType.LastIndexOf('>')];

                var normalizedBase = NormalizeType($"{baseName}<T>");
                if (!_builtInMappings.TryGetValue(targetPlatform, out var platformMap) || !platformMap.TryGetValue(normalizedBase, out var baseMapping))
                {
                    return false;
                }

                var typeParams = genericPart.Split(',').Select(t => t.Trim()).ToList();
                var mappedParams = typeParams.Select(p => _builtInMappings[targetPlatform].GetValueOrDefault(p, p)).ToList();

                // This simple replacement works for single generic parameters like <T>.
                // For multiple parameters like <TKey, TValue>, a more robust replacement is needed.
                targetType = baseMapping.Replace("<T>", $"<{string.Join(", ", mappedParams)}>");

                // Handle multi-param generics
                if (baseMapping.Contains("TKey") && baseMapping.Contains("TValue") && mappedParams.Count == 2)
                {
                    targetType = baseMapping.Replace("TKey", mappedParams[0]).Replace("TValue", mappedParams[1]);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private string GetFallbackMapping(string sourceType, string targetPlatform)
        {
            if (!string.IsNullOrEmpty(sourceType) && char.IsUpper(sourceType[0]) && !sourceType.Contains('<'))
            {
                _logger.LogDebug("Using passthrough mapping for type '{SourceType}' as it appears to be a custom class.", sourceType);
                return sourceType;
            }
            _logger.LogWarning("No mapping found for source type '{SourceType}'. Using platform-specific fallback 'Any'.", sourceType);
            return targetPlatform == "ios" ? "Any" : "Any";
        }

        private string ApplyNullableWrapper(string targetType, string targetPlatform)
        {
            if (string.IsNullOrWhiteSpace(targetType) || targetType.EndsWith("?")) return targetType;
            return $"{targetType}?";
        }

        private void ValidateTypeMappings(Dictionary<string, TypeMappingInfo> typeMappings)
        {
            foreach (var mapping in typeMappings.Values)
            {
                if (string.IsNullOrWhiteSpace(mapping.TargetType))
                {
                    _logger.LogWarning("Type mapping validation failed: No target type was mapped for source type '{SourceType}'.", mapping.SourceType);
                }
                if (mapping.MappingSource == TypeMappingSource.Fallback)
                {
                    _logger.LogWarning("A fallback mapping was used for '{SourceType}' -> '{TargetType}'. Consider adding a custom mapping.", mapping.SourceType, mapping.TargetType);
                }
            }
        }
        #endregion
    }
}