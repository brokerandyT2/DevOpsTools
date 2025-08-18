using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface IOutputService
    {
        Task GenerateOutputsAsync(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            TagTemplateResult tagResult,
            GitAnalysisResult gitAnalysis,
            LicenseSession? licenseSession);
    }

    public class OutputService : IOutputService
    {
        private readonly ILogger<OutputService> _logger;
        private readonly string _outputDirectory = "/src"; // Container mount point

        public OutputService(ILogger<OutputService> logger)
        {
            _logger = logger;
        }

        public async Task GenerateOutputsAsync(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            TagTemplateResult tagResult,
            GitAnalysisResult gitAnalysis,
            LicenseSession? licenseSession)
        {
            try
            {
                _logger.LogInformation("Generating output files to: {OutputDirectory}", _outputDirectory);

                // 1. Generate version-metadata.json
                await GenerateVersionMetadataAsync(config, versionResult, tagResult, gitAnalysis, licenseSession);

                // 2. Generate tag-patterns.json
                await GenerateTagPatternsAsync(tagResult);

                _logger.LogInformation("✓ All output files generated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate output files");
                throw new VersionDetectiveException(VersionDetectiveExitCode.InvalidConfiguration,
                    $"Output generation failed: {ex.Message}", ex);
            }
        }

        private async Task GenerateVersionMetadataAsync(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            TagTemplateResult tagResult,
            GitAnalysisResult gitAnalysis,
            LicenseSession? licenseSession)
        {
            try
            {
                var metadata = new VersionMetadata
                {
                    ToolName = config.License.ToolName,
                    ToolVersion = "1.0.0", // Could be made configurable
                    ExecutionTime = DateTime.UtcNow,
                    Language = config.Language.GetSelectedLanguage(),
                    Repository = config.RepoUrl,
                    Branch = config.Branch,
                    CurrentCommit = gitAnalysis.CurrentCommit,
                    BaselineCommit = gitAnalysis.BaselineCommit,
                    VersionCalculation = versionResult,
                    TagTemplates = tagResult,
                    LicenseUsed = licenseSession != null,
                    BurstModeUsed = licenseSession?.BurstMode ?? false,
                    Mode = config.Analysis.Mode
                };

                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    WriteIndented = true
                });

                var metadataFilePath = Path.Combine(_outputDirectory, "version-metadata.json");
                await File.WriteAllTextAsync(metadataFilePath, json);

                _logger.LogInformation("✓ Generated version-metadata.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate version-metadata.json");
                throw;
            }
        }

        private async Task GenerateTagPatternsAsync(TagTemplateResult tagResult)
        {
            try
            {
                var tagPatterns = new
                {
                    semantic_tag = tagResult.SemanticTag,
                    marketing_tag = tagResult.MarketingTag,
                    generated_at = DateTime.UtcNow,
                    token_values = tagResult.TokenValues
                };

                var json = JsonSerializer.Serialize(tagPatterns, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    WriteIndented = true
                });

                var tagPatternsFilePath = Path.Combine(_outputDirectory, "tag-patterns.json");
                await File.WriteAllTextAsync(tagPatternsFilePath, json);

                _logger.LogInformation("✓ Generated tag-patterns.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate tag-patterns.json");
                throw;
            }
        }
    }
}