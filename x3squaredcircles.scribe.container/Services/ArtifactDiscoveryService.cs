using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Models.Artifacts;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the logic for discovering 3SC tool artifacts and the native pipeline
    /// definition file within the CI/CD workspace.
    /// </summary>
    public class ArtifactDiscoveryService : IArtifactDiscoveryService
    {
        private readonly ILogger<ArtifactDiscoveryService> _logger;

        // A dedicated set of known pipeline filenames to actively identify.
        private static readonly HashSet<string> PipelineFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "azure-pipelines.yml",
            "azure-pipelines.yaml",
            ".gitlab-ci.yml",
            "Jenkinsfile"
        };

        // A list of files to be excluded from the generic artifact collection.
        private static readonly HashSet<string> ExcludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pipeline-log.json"
        };

        private static readonly HashSet<string> ExcludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".md"
        };

        public ArtifactDiscoveryService(ILogger<ArtifactDiscoveryService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task<DiscoveryResult> DiscoverArtifactsAsync(string workspacePath)
        {
            _logger.LogInformation("Starting artifact discovery in workspace: {Path}", workspacePath);

            if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
            {
                _logger.LogError("Workspace path '{Path}' is invalid or does not exist.", workspacePath);
                throw new DirectoryNotFoundException($"The provided workspace path '{workspacePath}' is invalid or does not exist.");
            }

            try
            {
                var allFiles = Directory.EnumerateFiles(workspacePath, "*", SearchOption.TopDirectoryOnly).ToList();

                // Step 1: Specifically identify the pipeline definition file.
                var pipelineFilePath = allFiles.FirstOrDefault(f => PipelineFileNames.Contains(Path.GetFileName(f)));
                if (pipelineFilePath != null)
                {
                    _logger.LogInformation("Identified pipeline definition file: {FileName}", Path.GetFileName(pipelineFilePath));
                }
                else
                {
                    _logger.LogWarning("No known pipeline definition file was found in the workspace root.");
                }

                // Step 2: Filter the remaining files to get the generic tool artifacts.
                var artifactFiles = allFiles
                    .Where(file => file != pipelineFilePath && // Exclude the pipeline file we just found
                                   !ExcludedFileNames.Contains(Path.GetFileName(file)) &&
                                   !ExcludedExtensions.Contains(Path.GetExtension(file)))
                    .ToList();

                _logger.LogDebug("Found {TotalCount} total files. After filtering, {ArtifactCount} potential artifacts remain.", allFiles.Count(), artifactFiles.Count);

                var discoveredArtifacts = new List<ScribeArtifact>();
                if (artifactFiles.Any())
                {
                    artifactFiles.Sort(StringComparer.OrdinalIgnoreCase);

                    int pageIndex = 2; // Page 1 is always Work Items.
                    discoveredArtifacts.AddRange(artifactFiles.Select(filePath => new ScribeArtifact(
                        toolName: GenerateToolName(filePath),
                        sourceFilePath: filePath,
                        pageIndex: pageIndex++
                    )));

                    _logger.LogInformation("Successfully discovered and indexed {Count} generic artifacts.", discoveredArtifacts.Count);
                }
                else
                {
                    _logger.LogInformation("No generic artifacts discovered in the workspace.");
                }

                // Step 3: Construct and return the final result.
                var result = new DiscoveryResult(discoveredArtifacts, pipelineFilePath);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "A critical error occurred during artifact discovery in {Path}.", workspacePath);
                throw new IOException($"Failed during artifact discovery in '{workspacePath}'. See inner exception for details.", ex);
            }
        }

        private static string GenerateToolName(string filePath)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var spacedName = fileNameWithoutExtension.Replace('-', ' ').Replace('_', ' ');
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(spacedName);
        }
    }
}