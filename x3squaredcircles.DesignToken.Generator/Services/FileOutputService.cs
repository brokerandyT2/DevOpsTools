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
            TokensConfiguration config,
            TokenCollection extractedTokens,
            GenerationResult generationResult,
            TagTemplateResult tagResult,
            LicenseSession? licenseSession);
    }

    public class FileOutputService : IFileOutputService
    {
        private readonly IAppLogger _logger;
        private readonly string _workingDirectory = "/src";

        public FileOutputService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task GenerateOutputsAsync(
            TokensConfiguration config,
            TokenCollection extractedTokens,
            GenerationResult generationResult,
            TagTemplateResult tagResult,
            LicenseSession? licenseSession)
        {
            try
            {
                _logger.LogStartPhase("Generating Output Files");

                var outputDir = Path.Combine(_workingDirectory, config.FileManagement.OutputDir);
                var generatedDir = Path.Combine(outputDir, config.FileManagement.GeneratedDir);
                Directory.CreateDirectory(generatedDir);

                await WriteJsonAsync(Path.Combine(generatedDir, "token-analysis.json"), new
                {
                    analysisTime = DateTime.UtcNow,
                    source = extractedTokens.Source,
                    tokenSummary = new { totalTokens = extractedTokens.Tokens.Count }
                });

                await WriteJsonAsync(Path.Combine(generatedDir, "generation-report.json"), new
                {
                    generationTime = DateTime.UtcNow,
                    platform = generationResult.Platform,
                    success = generationResult.Success,
                    filesGenerated = generationResult.Files.Count
                });

                await WriteJsonAsync(Path.Combine(generatedDir, "tag-patterns.json"), tagResult);

                await WriteJsonAsync(Path.Combine(generatedDir, "processed.json"), extractedTokens);

                await WritePipelineToolsLogAsync(config, licenseSession);

                _logger.LogEndPhase("Generating Output Files", true);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to generate output files.", ex);
                throw new DesignTokenException(DesignTokenExitCode.UnhandledException, $"Output file generation failed: {ex.Message}", ex);
            }
        }

        private async Task WriteJsonAsync(string filePath, object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await File.WriteAllTextAsync(filePath, json);
                _logger.LogInfo($"✓ Generated output file: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to write JSON file to {filePath}", ex);
                throw;
            }
        }

        private async Task WritePipelineToolsLogAsync(TokensConfiguration config, LicenseSession? licenseSession)
        {
            var logFilePath = Path.Combine(_workingDirectory, "pipeline-tools.log");
            var toolName = "3SC-DesignToken-Generator";
            var version = "1.0.0"; // Should be dynamic
            var burstIndicator = licenseSession?.BurstMode == true ? " (BURST)" : "";
            var logLine = $"{toolName}={version}{burstIndicator}";

            await File.AppendAllTextAsync(logFilePath, logLine + Environment.NewLine);
            _logger.LogDebug("Updated pipeline-tools.log");
        }
    }
}