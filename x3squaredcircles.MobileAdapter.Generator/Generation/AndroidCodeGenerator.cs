using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Discovery;
using x3squaredcircles.MobileAdapter.Generator.Models;
using x3squaredcircles.MobileAdapter.Generator.TypeMapping;

namespace x3squaredcircles.MobileAdapter.Generator.Generation
{
    /// <summary>
    /// Generates Kotlin data classes for the Android platform.
    /// </summary>
    public class AndroidCodeGenerator : ICodeGenerator
    {
        private readonly ILogger<AndroidCodeGenerator> _logger;

        public AndroidCodeGenerator(ILogger<AndroidCodeGenerator> logger)
        {
            _logger = logger;
        }

        public async Task<List<string>> GenerateAdaptersAsync(
            List<DiscoveredClass> discoveredClasses,
            Dictionary<string, TypeMappingInfo> typeMappings,
            GeneratorConfiguration config)
        {
            var generatedFiles = new List<string>();
            var outputDir = Path.Combine(config.Output.OutputDir, config.Output.AndroidOutputDir);
            var packageName = config.Output.AndroidPackageName;

            if (string.IsNullOrWhiteSpace(packageName))
            {
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "ANDROID_PACKAGE_NAME must be set for Android code generation.");
            }

            Directory.CreateDirectory(outputDir);

            foreach (var cls in discoveredClasses)
            {
                try
                {
                    var className = GetTargetClassName(cls);
                    var fileContent = GenerateKotlinClass(cls, className, typeMappings, packageName, config);
                    var filePath = Path.Combine(outputDir, $"{className}.kt");
                    await File.WriteAllTextAsync(filePath, fileContent);
                    generatedFiles.Add(filePath);
                    _logger.LogInformation("✓ Generated Android adapter: {FilePath}", filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate Kotlin adapter for class {ClassName}", cls.Name);
                }
            }

            return generatedFiles;
        }

        private string GetTargetClassName(DiscoveredClass cls)
        {
            if (cls.Metadata.TryGetValue("TargetName", out var targetName) && targetName is string name && !string.IsNullOrWhiteSpace(name))
            {
                _logger.LogDebug("Class '{OriginalName}' is being renamed to '{TargetName}' based on DSL metadata.", cls.Name, name);
                return name;
            }
            return cls.Name;
        }

        private string GenerateKotlinClass(
            DiscoveredClass cls,
            string targetClassName,
            Dictionary<string, TypeMappingInfo> typeMappings,
            string packageName,
            GeneratorConfiguration config)
        {
            var sb = new StringBuilder();

            // Package and Imports
            sb.AppendLine($"package {packageName}");
            sb.AppendLine();
            // Add imports based on used types (e.g., BigDecimal, UUID)
            var imports = GetRequiredImports(cls, typeMappings);
            foreach (var import in imports.OrderBy(i => i))
            {
                sb.AppendLine($"import {import}");
            }
            if (imports.Any()) sb.AppendLine();

            // Class definition
            sb.AppendLine($"data class {targetClassName}(");

            // Properties
            var propertyStrings = new List<string>();
            foreach (var prop in cls.Properties)
            {
                var mapping = typeMappings.GetValueOrDefault(prop.Type, new TypeMappingInfo { TargetType = "Any" });
                var propertyType = mapping.TargetType;

                // Handle collections
                if (mapping.IsCollection && !string.IsNullOrEmpty(prop.CollectionElementType))
                {
                    var elementMapping = typeMappings.GetValueOrDefault(prop.CollectionElementType, new TypeMappingInfo { TargetType = "Any" });
                    propertyType = propertyType.Replace("<T>", $"<{elementMapping.TargetType}>");
                }

                propertyStrings.Add($"    val {prop.Name}: {propertyType}");
            }
            sb.AppendLine(string.Join(",\n", propertyStrings));

            sb.AppendLine(")");

            return sb.ToString();
        }

        private HashSet<string> GetRequiredImports(DiscoveredClass cls, Dictionary<string, TypeMappingInfo> typeMappings)
        {
            var imports = new HashSet<string>();
            var typesUsed = cls.Properties.Select(p => p.Type)
                               .Concat(cls.Properties.Select(p => p.CollectionElementType))
                               .Where(t => !string.IsNullOrEmpty(t))
                               .Distinct();

            foreach (var type in typesUsed)
            {
                var mapping = typeMappings.GetValueOrDefault(type);
                if (mapping != null)
                {
                    switch (mapping.TargetType)
                    {
                        case "BigDecimal":
                            imports.Add("java.math.BigDecimal");
                            break;
                        case "UUID":
                            imports.Add("java.util.UUID");
                            break;
                        case "LocalDateTime":
                            imports.Add("java.time.LocalDateTime");
                            break;
                        case "OffsetDateTime":
                            imports.Add("java.time.OffsetDateTime");
                            break;
                        case "Duration":
                            imports.Add("java.time.Duration");
                            break;
                    }
                }
            }
            return imports;
        }
    }
}