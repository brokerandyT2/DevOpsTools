using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface ILanguageAnalysisService
    {
        Task<LanguageAnalysisResult> AnalyzeCodeAsync(VersionDetectiveConfiguration config, GitAnalysisResult gitAnalysis);
    }

    public class LanguageAnalysisService : ILanguageAnalysisService
    {
        private readonly ILogger<LanguageAnalysisService> _logger;
        private readonly string _workingDirectory = "/src";

        public LanguageAnalysisService(ILogger<LanguageAnalysisService> logger)
        {
            _logger = logger;
        }

        public async Task<LanguageAnalysisResult> AnalyzeCodeAsync(VersionDetectiveConfiguration config, GitAnalysisResult gitAnalysis)
        {
            try
            {
                var language = config.Language.GetSelectedLanguage();
                _logger.LogInformation("Analyzing {Language} code for tracked entities with attribute: {TrackAttribute}",
                    language.ToUpperInvariant(), config.TrackAttribute);

                var result = new LanguageAnalysisResult();

                // Get current tracked entities
                result.Entities = await GetCurrentTrackedEntitiesAsync(config, language);
                _logger.LogInformation("Found {Count} current tracked entities", result.Entities.Count);

                // Get baseline tracked entities if we have a baseline commit
                if (!string.IsNullOrEmpty(gitAnalysis.BaselineCommit))
                {
                    _logger.LogInformation("Comparing against baseline commit: {BaselineCommit}", gitAnalysis.BaselineCommit[..8]);
                    result.BaselineEntities = await GetBaselineTrackedEntitiesAsync(config, gitAnalysis, language);
                    _logger.LogInformation("Found {Count} baseline tracked entities", result.BaselineEntities.Count);

                    // Compare entities to find changes
                    result.EntityChanges = CompareEntities(result.BaselineEntities, result.Entities);
                    _logger.LogInformation("Detected {Count} entity changes", result.EntityChanges.Count);
                }
                else
                {
                    _logger.LogInformation("No baseline commit - treating as 'day 1 to now' analysis");
                    // No baseline - all current entities are new (day 1 scenario)
                    result.EntityChanges = result.Entities.Select(entity => new EntityChange
                    {
                        Type = "NewEntity",
                        Description = $"Initial tracked entity: {entity.Name}",
                        EntityName = entity.Name,
                        IsMajorChange = false // Day 1 entities don't cause major version bump
                    }).ToList();
                }

                // Analyze quantitative changes from git file changes
                result.QuantitativeChanges = await AnalyzeQuantitativeChangesAsync(config, gitAnalysis, language);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Language analysis failed for {Language}", config.Language.GetSelectedLanguage());
                throw new VersionDetectiveException(VersionDetectiveExitCode.InvalidConfiguration,
                    $"Language analysis failed: {ex.Message}", ex);
            }
        }

        private async Task<List<TrackedEntity>> GetCurrentTrackedEntitiesAsync(VersionDetectiveConfiguration config, string language)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => await AnalyzeCSharpEntitiesAsync(config),
                "java" => await AnalyzeJavaEntitiesAsync(config),
                "python" => await AnalyzePythonEntitiesAsync(config),
                "javascript" => await AnalyzeJavaScriptEntitiesAsync(config),
                "typescript" => await AnalyzeTypeScriptEntitiesAsync(config),
                "go" => await AnalyzeGoEntitiesAsync(config),
                _ => throw new VersionDetectiveException(VersionDetectiveExitCode.InvalidConfiguration,
                    $"Unsupported language: {language}")
            };
        }

        private async Task<List<TrackedEntity>> GetBaselineTrackedEntitiesAsync(
            VersionDetectiveConfiguration config,
            GitAnalysisResult gitAnalysis,
            string language)
        {
            var baselineEntities = new List<TrackedEntity>();

            try
            {
                // Get list of files that existed at baseline and are relevant to our language
                var baselineFiles = await GetBaselineRelevantFilesAsync(gitAnalysis.BaselineCommit!, language);

                foreach (var filePath in baselineFiles)
                {
                    try
                    {
                        // Get file content as it existed at baseline commit
                        var baselineContent = await GetFileContentAtCommitAsync(gitAnalysis.BaselineCommit!, filePath);
                        if (!string.IsNullOrEmpty(baselineContent))
                        {
                            var entities = await ParseEntitiesFromContentAsync(baselineContent, filePath, config.TrackAttribute, language);
                            baselineEntities.AddRange(entities);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to analyze baseline file: {FilePath}", filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze baseline entities");
            }

            return baselineEntities;
        }

        private async Task<List<string>> GetBaselineRelevantFilesAsync(string baselineCommit, string language)
        {
            try
            {
                var fileExtensions = GetFileExtensionsForLanguage(language);
                var allFiles = new List<string>();

                foreach (var extension in fileExtensions)
                {
                    var result = await ExecuteGitCommandAsync($"ls-tree -r --name-only {baselineCommit} -- '*.{extension}'");
                    if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
                    {
                        var files = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        allFiles.AddRange(files);
                    }
                }

                return allFiles;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get baseline files for {Language}", language);
                return new List<string>();
            }
        }

        private async Task<string> GetFileContentAtCommitAsync(string commit, string filePath)
        {
            try
            {
                var result = await ExecuteGitCommandAsync($"show {commit}:{filePath}");
                return result.ExitCode == 0 ? result.Output : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get file content at commit {Commit}: {FilePath}", commit[..8], filePath);
                return string.Empty;
            }
        }

        private async Task<List<TrackedEntity>> AnalyzeCSharpEntitiesAsync(VersionDetectiveConfiguration config)
        {
            var entities = new List<TrackedEntity>();

            try
            {
                // Method 1: Try to analyze compiled assemblies first (more accurate)
                if (config.Analysis.DllPaths.Any() || !string.IsNullOrEmpty(config.Analysis.BuildOutputPath))
                {
                    entities.AddRange(await AnalyzeCSharpAssembliesAsync(config));
                }

                // Method 2: Fallback to source code analysis
                if (!entities.Any())
                {
                    entities.AddRange(await AnalyzeCSharpSourceFilesAsync(config));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "C# assembly analysis failed, falling back to source analysis");
                entities.AddRange(await AnalyzeCSharpSourceFilesAsync(config));
            }

            return entities;
        }

        private async Task<List<TrackedEntity>> AnalyzeCSharpAssembliesAsync(VersionDetectiveConfiguration config)
        {
            var entities = new List<TrackedEntity>();

            // Get all potential assembly paths
            var assemblyPaths = new List<string>();

            if (!string.IsNullOrEmpty(config.Analysis.BuildOutputPath))
            {
                var buildPath = Path.IsPathRooted(config.Analysis.BuildOutputPath)
                    ? config.Analysis.BuildOutputPath
                    : Path.Combine(_workingDirectory, config.Analysis.BuildOutputPath);

                if (Directory.Exists(buildPath))
                {
                    assemblyPaths.AddRange(Directory.GetFiles(buildPath, "*.dll", SearchOption.AllDirectories));
                }
            }

            foreach (var dllPath in config.Analysis.DllPaths)
            {
                var fullPath = Path.IsPathRooted(dllPath) ? dllPath : Path.Combine(_workingDirectory, dllPath);
                if (Directory.Exists(fullPath))
                {
                    assemblyPaths.AddRange(Directory.GetFiles(fullPath, "*.dll", SearchOption.AllDirectories));
                }
                else if (File.Exists(fullPath) && fullPath.EndsWith(".dll"))
                {
                    assemblyPaths.Add(fullPath);
                }
            }

            foreach (var assemblyPath in assemblyPaths.Distinct())
            {
                try
                {
                    var assembly = Assembly.LoadFrom(assemblyPath);
                    var assemblyEntities = await AnalyzeAssemblyForTrackedEntitiesAsync(assembly, config.TrackAttribute);
                    entities.AddRange(assemblyEntities);
                    _logger.LogDebug("Analyzed assembly: {AssemblyPath} - found {Count} tracked entities",
                        Path.GetFileName(assemblyPath), assemblyEntities.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze assembly: {AssemblyPath}", assemblyPath);
                }
            }

            return entities;
        }

        private async Task<List<TrackedEntity>> AnalyzeAssemblyForTrackedEntitiesAsync(Assembly assembly, string trackAttribute)
        {
            var entities = new List<TrackedEntity>();

            try
            {
                var types = assembly.GetTypes()
                    .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract)
                    .Where(t => HasTrackingAttribute(t, trackAttribute))
                    .ToList();

                foreach (var type in types)
                {
                    var entity = new TrackedEntity
                    {
                        Name = type.Name,
                        FullName = type.FullName ?? type.Name,
                        FilePath = "assembly", // We don't have source file path from assembly
                        Properties = GetEntityProperties(type),
                        Methods = GetEntityMethods(type)
                    };

                    entities.Add(entity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze assembly: {AssemblyName}", assembly.FullName);
            }

            return entities;
        }

        private bool HasTrackingAttribute(Type type, string attributeName)
        {
            return type.GetCustomAttributes(false)
                .Any(attr => attr.GetType().Name.Equals($"{attributeName}Attribute", StringComparison.OrdinalIgnoreCase) ||
                            attr.GetType().Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
        }

        private List<EntityProperty> GetEntityProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetMethod?.IsPublic == true)
                .Select(p => new EntityProperty
                {
                    Name = p.Name,
                    Type = GetSimpleTypeName(p.PropertyType),
                    IsRequired = !IsNullableType(p.PropertyType)
                })
                .ToList();
        }

        private List<string> GetEntityMethods(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName && m.DeclaringType == type) // Exclude inherited and property accessors
                .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => GetSimpleTypeName(p.ParameterType)))})")
                .ToList();
        }

        private string GetSimpleTypeName(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType.Name;
        }

        private bool IsNullableType(Type type)
        {
            return Nullable.GetUnderlyingType(type) != null || !type.IsValueType;
        }

        private async Task<List<TrackedEntity>> AnalyzeCSharpSourceFilesAsync(VersionDetectiveConfiguration config)
        {
            var entities = new List<TrackedEntity>();
            var csFiles = Directory.GetFiles(_workingDirectory, "*.cs", SearchOption.AllDirectories);

            foreach (var filePath in csFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var fileEntities = await ParseEntitiesFromContentAsync(content, filePath, config.TrackAttribute, "csharp");
                    entities.AddRange(fileEntities);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze C# source file: {FilePath}", filePath);
                }
            }

            return entities;
        }

        private async Task<List<TrackedEntity>> AnalyzeJavaEntitiesAsync(VersionDetectiveConfiguration config)
        {
            var entities = new List<TrackedEntity>();
            var javaFiles = Directory.GetFiles(_workingDirectory, "*.java", SearchOption.AllDirectories);

            foreach (var filePath in javaFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var fileEntities = await ParseEntitiesFromContentAsync(content, filePath, config.TrackAttribute, "java");
                    entities.AddRange(fileEntities);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze Java source file: {FilePath}", filePath);
                }
            }

            return entities;
        }

        private async Task<List<TrackedEntity>> AnalyzePythonEntitiesAsync(VersionDetectiveConfiguration config)
        {
            var entities = new List<TrackedEntity>();
            var pyFiles = Directory.GetFiles(_workingDirectory, "*.py", SearchOption.AllDirectories);

            foreach (var filePath in pyFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var fileEntities = await ParseEntitiesFromContentAsync(content, filePath, config.TrackAttribute, "python");
                    entities.AddRange(fileEntities);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze Python source file: {FilePath}", filePath);
                }
            }

            return entities;
        }

        private async Task<List<TrackedEntity>> AnalyzeJavaScriptEntitiesAsync(VersionDetectiveConfiguration config)
        {
            var entities = new List<TrackedEntity>();
            var jsFiles = Directory.GetFiles(_workingDirectory, "*.js", SearchOption.AllDirectories);

            foreach (var filePath in jsFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var fileEntities = await ParseEntitiesFromContentAsync(content, filePath, config.TrackAttribute, "javascript");
                    entities.AddRange(fileEntities);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze JavaScript source file: {FilePath}", filePath);
                }
            }

            return entities;
        }

        private async Task<List<TrackedEntity>> AnalyzeTypeScriptEntitiesAsync(VersionDetectiveConfiguration config)
        {
            var entities = new List<TrackedEntity>();
            var tsFiles = Directory.GetFiles(_workingDirectory, "*.ts", SearchOption.AllDirectories);

            foreach (var filePath in tsFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var fileEntities = await ParseEntitiesFromContentAsync(content, filePath, config.TrackAttribute, "typescript");
                    entities.AddRange(fileEntities);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze TypeScript source file: {FilePath}", filePath);
                }
            }

            return entities;
        }

        private async Task<List<TrackedEntity>> AnalyzeGoEntitiesAsync(VersionDetectiveConfiguration config)
        {
            var entities = new List<TrackedEntity>();
            var goFiles = Directory.GetFiles(_workingDirectory, "*.go", SearchOption.AllDirectories);

            foreach (var filePath in goFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var fileEntities = await ParseEntitiesFromContentAsync(content, filePath, config.TrackAttribute, "go");
                    entities.AddRange(fileEntities);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze Go source file: {FilePath}", filePath);
                }
            }

            return entities;
        }

        private async Task<List<TrackedEntity>> ParseEntitiesFromContentAsync(string content, string filePath, string trackAttribute, string language)
        {
            var entities = new List<TrackedEntity>();

            try
            {
                var patterns = GetEntityPatternsForLanguage(language, trackAttribute);

                foreach (var pattern in patterns)
                {
                    var matches = Regex.Matches(content, pattern.Pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);

                    foreach (Match match in matches)
                    {
                        if (match.Success && match.Groups.Count > 1)
                        {
                            var entityName = match.Groups[1].Value;
                            var entityBody = match.Groups.Count > 2 ? match.Groups[2].Value : string.Empty;

                            var entity = new TrackedEntity
                            {
                                Name = entityName,
                                FullName = $"{Path.GetFileNameWithoutExtension(filePath)}.{entityName}",
                                FilePath = filePath,
                                Properties = ParsePropertiesFromEntityBody(entityBody, language),
                                Methods = ParseMethodsFromEntityBody(entityBody, language)
                            };

                            entities.Add(entity);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse entities from content in file: {FilePath}", filePath);
            }

            return entities;
        }

        private List<EntityPattern> GetEntityPatternsForLanguage(string language, string trackAttribute)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => new List<EntityPattern>
                {
                    new() { Pattern = $@"\[{trackAttribute}\][\s\S]*?(?:public\s+)?(?:class|record)\s+(\w+)[\s\S]*?\{{([\s\S]*?)\}}" },
                    new() { Pattern = $@"@{trackAttribute}[\s\S]*?(?:public\s+)?(?:class|record)\s+(\w+)[\s\S]*?\{{([\s\S]*?)\}}" }
                },
                "java" => new List<EntityPattern>
                {
                    new() { Pattern = $@"@{trackAttribute}[\s\S]*?(?:public\s+)?class\s+(\w+)[\s\S]*?\{{([\s\S]*?)\}}" }
                },
                "python" => new List<EntityPattern>
                {
                    new() { Pattern = $@"@{trackAttribute}[\s\S]*?class\s+(\w+)[\s\S]*?:([\s\S]*?)(?=\nclass|\n@|\Z)" }
                },
                "javascript" => new List<EntityPattern>
                {
                    new() { Pattern = $@"@{trackAttribute}[\s\S]*?class\s+(\w+)[\s\S]*?\{{([\s\S]*?)\}}" }
                },
                "typescript" => new List<EntityPattern>
                {
                    new() { Pattern = $@"@{trackAttribute}[\s\S]*?(?:export\s+)?class\s+(\w+)[\s\S]*?\{{([\s\S]*?)\}}" }
                },
                "go" => new List<EntityPattern>
                {
                    new() { Pattern = $@"\/\/ {trackAttribute}[\s\S]*?type\s+(\w+)\s+struct\s*\{{([\s\S]*?)\}}" }
                },
                _ => new List<EntityPattern>()
            };
        }

        private List<EntityProperty> ParsePropertiesFromEntityBody(string entityBody, string language)
        {
            var properties = new List<EntityProperty>();

            try
            {
                var propertyPatterns = GetPropertyPatternsForLanguage(language);

                foreach (var pattern in propertyPatterns)
                {
                    var matches = Regex.Matches(entityBody, pattern, RegexOptions.Multiline);

                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count >= 3)
                        {
                            properties.Add(new EntityProperty
                            {
                                Name = match.Groups[2].Value,
                                Type = match.Groups[1].Value,
                                IsRequired = !match.Groups[1].Value.Contains("?") && !match.Groups[1].Value.ToLowerInvariant().Contains("optional")
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse properties for {Language}", language);
            }

            return properties;
        }

        private List<string> ParseMethodsFromEntityBody(string entityBody, string language)
        {
            var methods = new List<string>();

            try
            {
                var methodPatterns = GetMethodPatternsForLanguage(language);

                foreach (var pattern in methodPatterns)
                {
                    var matches = Regex.Matches(entityBody, pattern, RegexOptions.Multiline);

                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count >= 2)
                        {
                            methods.Add(match.Groups[1].Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse methods for {Language}", language);
            }

            return methods;
        }

        private List<string> GetPropertyPatternsForLanguage(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => new List<string> { @"public\s+(\w+\??)\s+(\w+)\s*\{\s*get;\s*set;\s*\}" },
                "java" => new List<string> { @"(?:public|private|protected)?\s*(\w+)\s+(\w+);" },
                "python" => new List<string> { @"(\w+)\s*:\s*(\w+)" },
                "javascript" => new List<string> { @"(\w+)\s*:\s*(\w+)" },
                "typescript" => new List<string> { @"(\w+)\s*:\s*(\w+)" },
                "go" => new List<string> { @"(\w+)\s+(\w+)" },
                _ => new List<string>()
            };
        }

        private List<string> GetMethodPatternsForLanguage(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => new List<string> { @"public\s+\w+\s+(\w+)\s*\([^)]*\)" },
                "java" => new List<string> { @"(?:public|private|protected)\s+\w+\s+(\w+)\s*\([^)]*\)" },
                "python" => new List<string> { @"def\s+(\w+)\s*\([^)]*\)" },
                "javascript" => new List<string> { @"(\w+)\s*\([^)]*\)\s*\{" },
                "typescript" => new List<string> { @"(\w+)\s*\([^)]*\)\s*:" },
                "go" => new List<string> { @"func\s+(\w+)\s*\([^)]*\)" },
                _ => new List<string>()
            };
        }

        private List<string> GetFileExtensionsForLanguage(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => new List<string> { "cs" },
                "java" => new List<string> { "java" },
                "python" => new List<string> { "py" },
                "javascript" => new List<string> { "js" },
                "typescript" => new List<string> { "ts" },
                "go" => new List<string> { "go" },
                _ => new List<string>()
            };
        }

        private List<EntityChange> CompareEntities(List<TrackedEntity> baseline, List<TrackedEntity> current)
        {
            var changes = new List<EntityChange>();

            var baselineDict = baseline.ToDictionary(e => e.Name, e => e);
            var currentDict = current.ToDictionary(e => e.Name, e => e);

            // Check for new entities
            foreach (var entity in current.Where(e => !baselineDict.ContainsKey(e.Name)))
            {
                changes.Add(new EntityChange
                {
                    Type = "NewEntity",
                    Description = $"New tracked entity: {entity.Name}",
                    EntityName = entity.Name,
                    IsMajorChange = true
                });
            }

            // Check for removed entities
            foreach (var entity in baseline.Where(e => !currentDict.ContainsKey(e.Name)))
            {
                changes.Add(new EntityChange
                {
                    Type = "RemovedEntity",
                    Description = $"Removed tracked entity: {entity.Name}",
                    EntityName = entity.Name,
                    IsMajorChange = true
                });
            }

            // Check for modified entities
            foreach (var entity in current.Where(e => baselineDict.ContainsKey(e.Name)))
            {
                var baselineEntity = baselineDict[entity.Name];
                var entityChanges = CompareEntityProperties(baselineEntity, entity);
                changes.AddRange(entityChanges);
            }

            return changes;
        }

        private List<EntityChange> CompareEntityProperties(TrackedEntity baseline, TrackedEntity current)
        {
            var changes = new List<EntityChange>();

            var baselineProps = baseline.Properties.ToDictionary(p => p.Name, p => p);
            var currentProps = current.Properties.ToDictionary(p => p.Name, p => p);

            // Check for new properties
            foreach (var prop in current.Properties.Where(p => !baselineProps.ContainsKey(p.Name)))
            {
                changes.Add(new EntityChange
                {
                    Type = "NewProperty",
                    Description = $"New property: {current.Name}.{prop.Name} ({prop.Type})",
                    EntityName = current.Name,
                    IsMajorChange = true
                });
            }

            // Check for removed properties
            foreach (var prop in baseline.Properties.Where(p => !currentProps.ContainsKey(p.Name)))
            {
                changes.Add(new EntityChange
                {
                    Type = "RemovedProperty",
                    Description = $"Removed property: {current.Name}.{prop.Name}",
                    EntityName = current.Name,
                    IsMajorChange = true
                });
            }

            // Check for type changes
            foreach (var prop in current.Properties.Where(p => baselineProps.ContainsKey(p.Name)))
            {
                var baselineProp = baselineProps[prop.Name];
                if (baselineProp.Type != prop.Type)
                {
                    changes.Add(new EntityChange
                    {
                        Type = "PropertyTypeChange",
                        Description = $"Property type changed: {current.Name}.{prop.Name} ({baselineProp.Type} → {prop.Type})",
                        EntityName = current.Name,
                        IsMajorChange = true
                    });
                }
            }

            return changes;
        }

        private async Task<QuantitativeChanges> AnalyzeQuantitativeChangesAsync(VersionDetectiveConfiguration config, GitAnalysisResult gitAnalysis, string language)
        {
            var changes = new QuantitativeChanges();

            foreach (var gitChange in gitAnalysis.Changes)
            {
                if (!IsRelevantFileForLanguage(gitChange.FilePath, language))
                    continue;

                try
                {
                    await AnalyzeFileForQuantitativeChangesAsync(gitChange, changes, language);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze quantitative changes for file: {FilePath}", gitChange.FilePath);
                }
            }

            return changes;
        }

        private async Task AnalyzeFileForQuantitativeChangesAsync(GitFileChange gitChange, QuantitativeChanges changes, string language)
        {
            if (gitChange.ChangeType == "Added")
            {
                // New file - count new classes/methods/properties
                var newClasses = CountClassesInContent(gitChange.Content, language);
                changes.NewClasses += newClasses;

                var newMethods = CountMethodsInContent(gitChange.Content, language);
                changes.NewMethods += newMethods;

                var newProperties = CountPropertiesInContent(gitChange.Content, language);
                changes.NewProperties += newProperties;
            }
            else if (gitChange.ChangeType == "Modified")
            {
                // Modified file - analyze for bug fixes, performance improvements, etc.
                if (IsBugFixFile(gitChange.Content, gitChange.FilePath))
                {
                    changes.BugFixes++;
                }

                if (IsPerformanceImprovement(gitChange.Content))
                {
                    changes.PerformanceImprovements++;
                }
            }

            if (IsDocumentationFile(gitChange.FilePath))
            {
                changes.DocumentationUpdates++;
            }
        }

        private bool IsRelevantFileForLanguage(string filePath, string language)
        {
            var extensions = GetFileExtensionsForLanguage(language);
            var fileExtension = Path.GetExtension(filePath).TrimStart('.');
            return extensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase);
        }

        private int CountClassesInContent(string content, string language)
        {
            var patterns = language.ToLowerInvariant() switch
            {
                "csharp" => new[] { @"(?:public\s+)?(?:class|record|interface)\s+\w+" },
                "java" => new[] { @"(?:public\s+)?class\s+\w+" },
                "python" => new[] { @"class\s+\w+" },
                "javascript" => new[] { @"class\s+\w+" },
                "typescript" => new[] { @"(?:export\s+)?class\s+\w+" },
                "go" => new[] { @"type\s+\w+\s+(?:struct|interface)" },
                _ => Array.Empty<string>()
            };

            return patterns.Sum(pattern => Regex.Matches(content, pattern).Count);
        }

        private int CountMethodsInContent(string content, string language)
        {
            var patterns = language.ToLowerInvariant() switch
            {
                "csharp" => new[] { @"(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?\w+\s+\w+\s*\([^)]*\)" },
                "java" => new[] { @"(?:public|private|protected)\s+(?:static\s+)?\w+\s+\w+\s*\([^)]*\)" },
                "python" => new[] { @"def\s+\w+\s*\([^)]*\)" },
                "javascript" => new[] { @"\w+\s*\([^)]*\)\s*\{", @"function\s+\w+\s*\([^)]*\)" },
                "typescript" => new[] { @"\w+\s*\([^)]*\)\s*:", @"function\s+\w+\s*\([^)]*\)" },
                "go" => new[] { @"func\s+\w+\s*\([^)]*\)" },
                _ => Array.Empty<string>()
            };

            return patterns.Sum(pattern => Regex.Matches(content, pattern).Count);
        }

        private int CountPropertiesInContent(string content, string language)
        {
            var patterns = language.ToLowerInvariant() switch
            {
                "csharp" => new[] { @"(?:public|private|protected|internal)\s+\w+\s+\w+\s*\{\s*get;\s*set;\s*\}" },
                "java" => new[] { @"(?:public|private|protected)\s+\w+\s+\w+;" },
                "python" => new[] { @"\w+\s*:\s*\w+" },
                "javascript" => new[] { @"\w+\s*:" },
                "typescript" => new[] { @"\w+\s*:\s*\w+" },
                "go" => new[] { @"\w+\s+\w+" },
                _ => Array.Empty<string>()
            };

            return patterns.Sum(pattern => Regex.Matches(content, pattern).Count);
        }

        private bool IsBugFixFile(string content, string filePath)
        {
            var bugFixIndicators = new[]
            {
                @"\bfix\b", @"\bbug\b", @"\bissue\b", @"\berror\b", @"\bexception\b",
                @"try\s*\{.*?\}\s*catch", @"throw\s+new\s+\w*Exception",
                @"\bnull\s*check\b", @"\bvalidation\b"
            };

            return bugFixIndicators.Any(pattern =>
                Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase) ||
                Regex.IsMatch(filePath, pattern, RegexOptions.IgnoreCase));
        }

        private bool IsPerformanceImprovement(string content)
        {
            var performanceIndicators = new[]
            {
                @"\bperformance\b", @"\boptimiz\w+", @"\bcache\b", @"\basync\b.*\bawait\b",
                @"\bparallel\b", @"\bStringBuilder\b", @"\bIAsyncEnumerable\b",
                @"\bConfigureAwait\(false\)", @"\bmemory\s+leak\b", @"\befficiency\b"
            };

            return performanceIndicators.Any(pattern =>
                Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase));
        }

        private bool IsDocumentationFile(string filePath)
        {
            var docExtensions = new[] { ".md", ".txt", ".rst", ".adoc" };
            var docNames = new[] { "readme", "changelog", "docs", "documentation" };

            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            return docExtensions.Contains(extension) ||
                   docNames.Any(name => fileName.Contains(name)) ||
                   filePath.Contains("/docs/", StringComparison.OrdinalIgnoreCase) ||
                   filePath.Contains("\\docs\\", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<GitCommandResult> ExecuteGitCommandAsync(string arguments)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            return new GitCommandResult
            {
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString().Trim(),
                Error = errorBuilder.ToString().Trim()
            };
        }

        private class GitCommandResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }

        private class EntityPattern
        {
            public string Pattern { get; set; } = string.Empty;
        }
    }
}