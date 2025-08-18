using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
//using x3squaredcircles.DesignToken.Generator.Models;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Discovery;
using x3squaredcircles.MobileAdapter.Generator.Models;
using x3squaredcircles.MobileAdapter.Generator.TypeMapping;

namespace x3squaredcircles.MobileAdapter.Generator.Services
{
    /// <summary>
    /// Defines the contract for a service that generates all output files and reports.
    /// </summary>
    public interface IFileOutputService
    {
        Task GenerateOutputsAsync(GenerationResult result, TagTemplateResult tagResult);
    }

    /// <summary>
    /// Responsible for writing all forensic and informational output files to the workspace.
    /// This includes analysis reports, type mappings, and generation summaries.
    /// </summary>
    public class FileOutputService : IFileOutputService
    {
        private readonly ILogger<FileOutputService> _logger;
        private readonly GeneratorConfiguration _config;
        private readonly string _outputDirectory = "/src"; // Assumes running in a container

        public FileOutputService(ILogger<FileOutputService> logger, GeneratorConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Generates all standard output files for the generation process.
        /// </summary>
        /// <param name="result">The final result from the AdapterGeneratorEngine.</param>
        /// <param name="tagResult">The result from the TagTemplateService.</param>
        public async Task GenerateOutputsAsync(GenerationResult result, TagTemplateResult tagResult)
        {
            try
            {
                _logger.LogInformation("Generating all output files...");

                // Ensure the base output directory exists
                Directory.CreateDirectory(_outputDirectory);

                // Generate the core reports
                await WriteJsonReportAsync("adapter-analysis.json", CreateAnalysisReport(result));
                await WriteJsonReportAsync("type-mappings.json", CreateTypeMappingReport(result));
                await WriteJsonReportAsync("generation-report.json", CreateGenerationReport(result, tagResult));
                await WriteJsonReportAsync("tag-patterns.json", CreateTagPatternsReport(tagResult));

                _logger.LogInformation("✓ All output files generated successfully in '{Directory}'.", _outputDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate one or more output files.");
                throw new MobileAdapterException(MobileAdapterExitCode.FileWriteFailure, "Failed to write output files.", ex);
            }
        }

        private object CreateAnalysisReport(GenerationResult result)
        {
            return new
            {
                AnalyzedAt = DateTime.UtcNow,
                SourceLanguage = _config.GetSelectedLanguage().ToString(),
                DiscoveryMethod = GetDiscoveryMethod(),
                ClassCount = result.DiscoveredClasses.Count,
                Classes = result.DiscoveredClasses
            };
        }

        private object CreateTypeMappingReport(GenerationResult result)
        {
            return new
            {
                MappedAt = DateTime.UtcNow,
                TargetPlatform = _config.GetSelectedPlatform().ToString(),
                MappingCount = result.TypeMappings.Count,
                Mappings = result.TypeMappings
            };
        }

        private object CreateGenerationReport(GenerationResult result, TagTemplateResult tagResult)
        {
            return new
            {
                GeneratedAt = DateTime.UtcNow,
                GeneratedTag = tagResult.GeneratedTag,
                Configuration = new
                {
                    _config.RepoUrl,
                    _config.Branch,
                    Language = _config.GetSelectedLanguage().ToString(),
                    Platform = _config.GetSelectedPlatform().ToString(),
                    Mode = _config.Mode.ToString(),
                    _config.DryRun
                },
                Summary = new
                {
                    DiscoveredClasses = result.DiscoveredClasses.Count,
                    MappedTypes = result.TypeMappings.Count,
                    GeneratedFiles = result.GeneratedFiles.Count
                },
                result.GeneratedFiles
            };
        }

        private object CreateTagPatternsReport(TagTemplateResult tagResult)
        {
            return new
            {
                tagResult.GenerationTime,
                tagResult.Template,
                tagResult.GeneratedTag,
                tagResult.TokenValues
            };
        }

        private string GetDiscoveryMethod()
        {
            if (!string.IsNullOrEmpty(_config.TrackAttribute)) return $"Attribute: {_config.TrackAttribute}";
            if (!string.IsNullOrEmpty(_config.TrackPattern)) return $"Pattern: {_config.TrackPattern}";
            if (!string.IsNullOrEmpty(_config.TrackNamespace)) return $"Namespace: {_config.TrackNamespace}";
            if (!string.IsNullOrEmpty(_config.TrackFilePattern)) return $"FilePattern: {_config.TrackFilePattern}";
            return "Unknown";
        }

        private async Task WriteJsonReportAsync(string fileName, object data)
        {
            var filePath = Path.Combine(_outputDirectory, fileName);
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var jsonContent = JsonSerializer.Serialize(data, options);
                await File.WriteAllTextAsync(filePath, jsonContent);
                _logger.LogDebug("Successfully wrote report to '{FilePath}'.", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write JSON report to '{FilePath}'.", filePath);
                // Allow the main exception handler to catch this after logging specifics.
                throw;
            }
        }
    }
}