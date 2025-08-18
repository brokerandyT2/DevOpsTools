using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for a service that infers dependencies from a developer's existing project files.
    /// </summary>
    public interface IDependencyInferenceService
    {
        Task<Dictionary<string, string>> InferDependenciesAsync(List<JsonElement> apisForGroup);
    }

    /// <summary>
    /// Implements the "Hybrid Dependency Management" model by interrogating project files (.csproj)
    /// to discover existing dependencies and their versions.
    /// </summary>
    public class DependencyInferenceService : IDependencyInferenceService
    {
        private readonly ILogger<DependencyInferenceService> _logger;

        public DependencyInferenceService(ILogger<DependencyInferenceService> logger)
        {
            _logger = logger;
        }

        public async Task<Dictionary<string, string>> InferDependenciesAsync(List<JsonElement> apisForGroup)
        {
            _logger.LogInformation("Inferring dependencies from source business logic projects...");
            var allDependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var projectPaths = apisForGroup
                .Select(api => api.GetProperty("projectPath").GetString())
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .ToList();

            if (!projectPaths.Any())
            {
                _logger.LogWarning("No source project paths were discovered. Cannot infer any dependencies.");
                return allDependencies;
            }

            foreach (var projectPath in projectPaths)
            {
                if (!File.Exists(projectPath))
                {
                    _logger.LogWarning("Project file specified in discovered API metadata not found, cannot infer dependencies: {Path}", projectPath);
                    continue;
                }

                try
                {
                    var dependencies = await ParseProjectFileAsync(projectPath);
                    foreach (var dep in dependencies)
                    {
                        if (allDependencies.TryGetValue(dep.Key, out var existingVersion) && existingVersion != dep.Value)
                        {
                            _logger.LogWarning("Dependency conflict detected for package '{Package}'. Version '{Existing}' from one project and '{New}' from another. Using the latter.",
                                dep.Key, existingVersion, dep.Value);
                        }
                        allDependencies[dep.Key] = dep.Value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse dependencies from project file: {Path}", projectPath);
                    // Continue to the next project file, don't fail the whole run.
                }
            }

            _logger.LogInformation("✓ Successfully inferred {Count} unique package dependencies.", allDependencies.Count);
            return allDependencies;
        }

        private async Task<Dictionary<string, string>> ParseProjectFileAsync(string projectPath)
        {
            var dependencies = new Dictionary<string, string>();
            _logger.LogDebug("Parsing dependencies from: {Path}", projectPath);

            var content = await File.ReadAllTextAsync(projectPath);
            var xml = XDocument.Parse(content);
            var ns = xml.Root.GetDefaultNamespace();

            var packageReferences = xml.Descendants("PackageReference");
            foreach (var reference in packageReferences)
            {
                var packageName = reference.Attribute("Include")?.Value;
                var packageVersion = reference.Attribute("Version")?.Value;

                if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                {
                    dependencies[packageName] = packageVersion;
                    _logger.LogDebug("  - Found Package: {Package} Version: {Version}", packageName, packageVersion);
                }
            }

            var projectReferences = xml.Descendants("ProjectReference");
            foreach (var reference in projectReferences)
            {
                var includePath = reference.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(includePath))
                {
                    var referencedProjectPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath), includePath));
                    _logger.LogDebug("  - Found Project Reference, recursively parsing: {Path}", referencedProjectPath);

                    // Recurse to find dependencies of referenced projects
                    var transitiveDependencies = await ParseProjectFileAsync(referencedProjectPath);
                    foreach (var dep in transitiveDependencies)
                    {
                        dependencies[dep.Key] = dep.Value;
                    }
                }
            }

            return dependencies;
        }
    }
}