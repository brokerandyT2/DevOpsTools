using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IFileOutputService
    {
        Task GenerateOutputsAsync(
            DesignTokenConfiguration config,
            TokenCollection extractedTokens,
            GenerationResult generationResult,
            TagTemplateResult tagResult,
            LicenseSession? licenseSession);
    }

    public class FileOutputService : IFileOutputService
    {
        private readonly ILogger<FileOutputService> _logger;
        private readonly string _workingDirectory = "/src";

        public FileOutputService(ILogger<FileOutputService> logger)
        {
            _logger = logger;
        }

        public async Task GenerateOutputsAsync(
            DesignTokenConfiguration config,
            TokenCollection extractedTokens,
            GenerationResult generationResult,
            TagTemplateResult tagResult,
            LicenseSession? licenseSession)
        {
            try
            {
                _logger.LogInformation("Generating output files");

                var outputDir = Path.Combine(_workingDirectory, config.FileManagement.OutputDir);
                Directory.CreateDirectory(outputDir);

                // 1. Generate pipeline-tools.log entry
                await GeneratePipelineToolsLogAsync(config, extractedTokens, licenseSession);

                // 2. Generate token-analysis.json
                await GenerateTokenAnalysisAsync(config, extractedTokens, outputDir);

                // 3. Generate generation-report.json
                await GenerateGenerationReportAsync(config, generationResult, outputDir);

                // 4. Generate tag-patterns.json
                await GenerateTagPatternsAsync(tagResult, outputDir);

                // 5. Save processed tokens
                await SaveProcessedTokensAsync(extractedTokens, outputDir);

                // 6. Generate base and vertical token files
                await GenerateTokenFilesAsync(config, extractedTokens, outputDir);

                _logger.LogInformation("✓ All output files generated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate output files");
                throw new DesignTokenException(DesignTokenExitCode.FileSystemError,
                    $"Output file generation failed: {ex.Message}", ex);
            }
        }

        private async Task GeneratePipelineToolsLogAsync(
            DesignTokenConfiguration config,
            TokenCollection extractedTokens,
            LicenseSession? licenseSession)
        {
            try
            {
                var logEntry = new PipelineToolLogEntry
                {
                    ToolName = config.License.ToolName,
                    Version = extractedTokens.Version,
                    BurstMode = licenseSession?.BurstMode ?? false,
                    Timestamp = DateTime.UtcNow
                };

                var logLine = FormatPipelineToolLogEntry(logEntry);
                var logFilePath = Path.Combine(_workingDirectory, "pipeline-tools.log");

                // Append to log file (create if doesn't exist)
                await File.AppendAllTextAsync(logFilePath, logLine + Environment.NewLine);

                _logger.LogInformation("✓ Updated pipeline-tools.log");
                _logger.LogDebug("Log entry: {LogEntry}", logLine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate pipeline-tools.log");
                throw;
            }
        }

        private async Task GenerateTokenAnalysisAsync(
            DesignTokenConfiguration config,
            TokenCollection extractedTokens,
            string outputDir)
        {
            try
            {
                var analysis = new
                {
                    analysis_time = DateTime.UtcNow,
                    source = extractedTokens.Source,
                    design_platform = config.DesignPlatform.GetSelectedPlatform(),
                    target_platform = config.TargetPlatform.GetSelectedPlatform(),
                    token_summary = new
                    {
                        total_tokens = extractedTokens.Tokens.Count,
                        tokens_by_type = extractedTokens.Tokens
                            .GroupBy(t => t.Type)
                            .ToDictionary(g => g.Key, g => g.Count()),
                        tokens_by_category = extractedTokens.Tokens
                            .GroupBy(t => t.Category)
                            .ToDictionary(g => g.Key, g => g.Count())
                    },
                    extraction_metadata = extractedTokens.Metadata,
                    configuration = new
                    {
                        repository = config.RepoUrl,
                        branch = config.Branch,
                        mode = config.Operation.Mode,
                        preserve_custom = config.Operation.PreserveCustom,
                        merge_strategy = config.Operation.MergeStrategy
                    }
                };

                var analysisPath = Path.Combine(outputDir, "token-analysis.json");
                var json = JsonSerializer.Serialize(analysis, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                await File.WriteAllTextAsync(analysisPath, json);
                _logger.LogInformation("✓ Generated token-analysis.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate token analysis");
                throw;
            }
        }

        private async Task GenerateGenerationReportAsync(
            DesignTokenConfiguration config,
            GenerationResult generationResult,
            string outputDir)
        {
            try
            {
                var report = new
                {
                    generation_time = DateTime.UtcNow,
                    platform = generationResult.Platform,
                    success = generationResult.Success,
                    error_message = generationResult.ErrorMessage,
                    files_generated = generationResult.Files.Count,
                    files = generationResult.Files.Select(f => new
                    {
                        file_path = f.FilePath,
                        has_custom_sections = f.HasCustomSections,
                        custom_sections_count = f.PreservedSections.Count,
                        content_size = f.Content.Length
                    }).ToList(),
                    metadata = generationResult.Metadata,
                    configuration_used = new
                    {
                        target_platform = config.TargetPlatform.GetSelectedPlatform(),
                        output_directory = config.TargetPlatform.GetSelectedPlatform() switch
                        {
                            "android" => config.TargetPlatform.AndroidOutputDir,
                            "ios" => config.TargetPlatform.IosOutputDir,
                            "web" => config.TargetPlatform.WebOutputDir,
                            _ => "unknown"
                        }
                    }
                };

                var reportPath = Path.Combine(outputDir, "generation-report.json");
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                await File.WriteAllTextAsync(reportPath, json);
                _logger.LogInformation("✓ Generated generation-report.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate generation report");
                throw;
            }
        }

        private async Task GenerateTagPatternsAsync(TagTemplateResult tagResult, string outputDir)
        {
            try
            {
                var tagPatterns = new
                {
                    generated_tag = tagResult.GeneratedTag,
                    generated_at = DateTime.UtcNow,
                    token_values = tagResult.TokenValues,
                    template_used = ExtractTemplateFromTokens(tagResult.TokenValues)
                };

                var tagPatternsPath = Path.Combine(outputDir, "tag-patterns.json");
                var json = JsonSerializer.Serialize(tagPatterns, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                await File.WriteAllTextAsync(tagPatternsPath, json);
                _logger.LogInformation("✓ Generated tag-patterns.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate tag patterns");
                throw;
            }
        }

        private async Task SaveProcessedTokensAsync(TokenCollection tokens, string outputDir)
        {
            try
            {
                var generatedDir = Path.Combine(outputDir, "generated");
                Directory.CreateDirectory(generatedDir);

                var processedPath = Path.Combine(generatedDir, "processed.json");
                var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(processedPath, json);
                _logger.LogInformation("✓ Saved processed tokens: {ProcessedPath}", processedPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save processed tokens");
                throw;
            }
        }

        private async Task GenerateTokenFilesAsync(
            DesignTokenConfiguration config,
            TokenCollection tokens,
            string outputDir)
        {
            try
            {
                // Generate base tokens file
                await GenerateBaseTokensFileAsync(config, tokens, outputDir);

                // Generate vertical-specific tokens file if applicable
                await GenerateVerticalTokensFileAsync(config, tokens, outputDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate token files");
                throw;
            }
        }

        private async Task GenerateBaseTokensFileAsync(
            DesignTokenConfiguration config,
            TokenCollection tokens,
            string outputDir)
        {
            try
            {
                // Extract base tokens (tokens that don't have vertical-specific tags)
                var baseTokens = tokens.Tokens
                    .Where(t => !t.Tags.Any(tag => tag.Contains("vertical-") || tag.Contains("business-")))
                    .ToList();

                var baseTokenCollection = new
                {
                    name = "Base Design Tokens",
                    version = tokens.Version,
                    source = tokens.Source,
                    created_at = DateTime.UtcNow,
                    tokens = baseTokens.Select(t => new
                    {
                        name = t.Name,
                        type = t.Type,
                        category = t.Category,
                        value = t.Value,
                        description = t.Description,
                        tags = t.Tags,
                        attributes = t.Attributes
                    }).ToList()
                };

                var baseTokensPath = Path.Combine(outputDir, config.FileManagement.BaseTokensFile);
                var json = JsonSerializer.Serialize(baseTokenCollection, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                await File.WriteAllTextAsync(baseTokensPath, json);
                _logger.LogInformation("✓ Generated base tokens file: {FilePath}", baseTokensPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate base tokens file");
                throw;
            }
        }

        private async Task GenerateVerticalTokensFileAsync(
            DesignTokenConfiguration config,
            TokenCollection tokens,
            string outputDir)
        {
            try
            {
                // Extract vertical name from repository or configuration
                var verticalName = ExtractVerticalName(config);
                if (string.IsNullOrEmpty(verticalName))
                {
                    _logger.LogDebug("No vertical name detected, skipping vertical tokens file");
                    return;
                }

                // Extract vertical-specific tokens
                var verticalTokens = tokens.Tokens
                    .Where(t => t.Tags.Any(tag =>
                        tag.Contains($"vertical-{verticalName}") ||
                        tag.Contains($"business-{verticalName}") ||
                        t.Name.Contains(verticalName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (!verticalTokens.Any())
                {
                    _logger.LogDebug("No vertical-specific tokens found for: {Vertical}", verticalName);
                    return;
                }

                var verticalTokenCollection = new
                {
                    name = $"{verticalName} Design Tokens",
                    version = tokens.Version,
                    source = tokens.Source,
                    vertical = verticalName,
                    created_at = DateTime.UtcNow,
                    tokens = verticalTokens.Select(t => new
                    {
                        name = t.Name,
                        type = t.Type,
                        category = t.Category,
                        value = t.Value,
                        description = t.Description,
                        tags = t.Tags,
                        attributes = t.Attributes
                    }).ToList()
                };

                var verticalFileName = config.FileManagement.VerticalTokensFile.Replace("{vertical}", verticalName);
                var verticalTokensPath = Path.Combine(outputDir, verticalFileName);
                var json = JsonSerializer.Serialize(verticalTokenCollection, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                await File.WriteAllTextAsync(verticalTokensPath, json);
                _logger.LogInformation("✓ Generated vertical tokens file: {FilePath}", verticalTokensPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate vertical tokens file");
                throw;
            }
        }

        private string FormatPipelineToolLogEntry(PipelineToolLogEntry entry)
        {
            var burstIndicator = entry.BurstMode ? " (BURST)" : "";
            return $"{entry.ToolName}={entry.Version}{burstIndicator}";
        }

        private string ExtractTemplateFromTokens(Dictionary<string, string> tokenValues)
        {
            // Reconstruct template pattern from token values
            var templateParts = new List<string>();

            if (tokenValues.ContainsKey("branch"))
                templateParts.Add("{branch}");
            if (tokenValues.ContainsKey("repo"))
                templateParts.Add("{repo}");
            if (tokenValues.ContainsKey("design-platform"))
                templateParts.Add("{design-platform}");
            if (tokenValues.ContainsKey("platform"))
                templateParts.Add("{platform}");
            if (tokenValues.ContainsKey("version"))
                templateParts.Add("{version}");

            return string.Join("/", templateParts);
        }

        private string ExtractVerticalName(DesignTokenConfiguration config)
        {
            try
            {
                // Try to extract vertical from repository URL
                var repoUrl = config.RepoUrl;
                if (!string.IsNullOrEmpty(repoUrl))
                {
                    var uri = new Uri(repoUrl);
                    var pathParts = uri.AbsolutePath.Trim('/').Split('/');

                    // Look for common vertical indicators in repo name
                    var repoName = pathParts.LastOrDefault()?.Replace(".git", "");
                    if (!string.IsNullOrEmpty(repoName))
                    {
                        var verticals = new[] { "photography", "location", "navigation", "commerce", "health", "finance" };
                        foreach (var vertical in verticals)
                        {
                            if (repoName.Contains(vertical, StringComparison.OrdinalIgnoreCase))
                            {
                                return vertical.ToLowerInvariant();
                            }
                        }
                    }
                }

                // Try to extract from target repo
                if (!string.IsNullOrEmpty(config.TargetRepo))
                {
                    var verticals = new[] { "photography", "location", "navigation", "commerce", "health", "finance" };
                    foreach (var vertical in verticals)
                    {
                        if (config.TargetRepo.Contains(vertical, StringComparison.OrdinalIgnoreCase))
                        {
                            return vertical.ToLowerInvariant();
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract vertical name");
                return string.Empty;
            }
        }
    }
}