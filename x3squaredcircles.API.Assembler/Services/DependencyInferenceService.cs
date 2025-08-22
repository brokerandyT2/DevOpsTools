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
    /// to discover existing dependencies and their versions, including transitive project references.
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
            var parsedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // For circular dependency protection

            var projectPaths = apisForGroup
                .Select(api => api.TryGetProperty("projectPath", out var pathProp) ? pathProp.GetString() : null)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!projectPaths.Any())
            {
                _logger.LogWarning("No source project paths were discovered in the API metadata. Cannot infer any dependencies.");
                return allDependencies;
            }

            foreach (var projectPath in projectPaths)
            {
                await ParseProjectFileRecursiveAsync(projectPath, allDependencies, parsedProjects);
            }

            _logger.LogInformation("✓ Successfully inferred {Count} unique package dependencies.", allDependencies.Count);
            return allDependencies;
        }

        private async Task ParseProjectFileRecursiveAsync(string projectPath, Dictionary<string, string> allDependencies, HashSet<string> parsedProjects)
        {
            var normalizedPath = Path.GetFullPath(projectPath);
            if (parsedProjects.Contains(normalizedPath))
            {
                _logger.LogDebug("Skipping already parsed project to prevent circular dependency: {Path}", normalizedPath);
                return;
            }

            if (!File.Exists(normalizedPath))
            {
                _logger.LogWarning("Project file specified in discovered API metadata not found, cannot infer dependencies: {Path}", normalizedPath);
                return;
            }

            parsedProjects.Add(normalizedPath);
            _logger.LogDebug("Parsing dependencies from: {Path}", normalizedPath);

            try
            {
                var doc = await XDocument.LoadAsync(File.OpenRead(normalizedPath), LoadOptions.None, CancellationToken.None);

                // Handle PackageReference
                var packageReferences = doc.Descendants("PackageReference");
                foreach (var reference in packageReferences)
                {
                    var packageName = reference.Attribute("Include")?.Value;
                    var packageVersion = reference.Attribute("Version")?.Value;

                    if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                    {
                        if (allDependencies.TryGetValue(packageName, out var existingVersion) && existingVersion != packageVersion)
                        {
                            _logger.LogWarning("Dependency conflict detected for package '{Package}'. Version '{Existing}' from one project and '{New}' from another. Using the latter.",
                                packageName, existingVersion, packageVersion);
                        }
                        allDependencies[packageName] = packageVersion;
                        _logger.LogDebug("  - Found Package: {Package} Version: {Version}", packageName, packageVersion);
                    }
                }

                // Handle ProjectReference (Recurse)
                var projectReferences = doc.Descendants("ProjectReference");
                foreach (var reference in projectReferences)
                {
                    var includePath = reference.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(includePath))
                    {
                        var referencedProjectPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(normalizedPath)!, includePath));
                        _logger.LogDebug("  - Found Project Reference, recursively parsing: {Path}", referencedProjectPath);
                        await ParseProjectFileRecursiveAsync(referencedProjectPath, allDependencies, parsedProjects);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse dependencies from project file: {Path}. This project's dependencies will be skipped.", normalizedPath);
                // We log the error but do not fail the entire run, allowing the process to be resilient.
            }
        }
    }
}