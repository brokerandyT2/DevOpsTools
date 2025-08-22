using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public interface IFileOutputService
    {
        Task WriteGenerationReceiptAsync(string managedWorkspacePath, JsonDocument manifest, JsonDocument discoveredApis, IEnumerable<GeneratedProject>? generatedProjects);
        Task<GenerationReceipt> ReadGenerationReceiptAsync(string managedWorkspacePath);
        Task WriteDeploymentReceiptAsync(string managedWorkspacePath, string groupName, string artifactPath);
        Task WriteBuildReceiptAsync(string managedWorkspacePath, string builtProjectPath, BuildResult buildResult);
    }

    public class FileOutputService : IFileOutputService
    {
        private const string GenerationReceiptFileName = "generation-receipt.json";

        private readonly ILogger<FileOutputService> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        public async Task WriteBuildReceiptAsync(string managedWorkspacePath, string builtProjectPath, BuildResult buildResult)
        {
            var projectName = Path.GetFileName(builtProjectPath);
            var receiptFileName = $"build-receipt.{projectName}.json";
            var receiptPath = Path.Combine(managedWorkspacePath, receiptFileName);
            _logger.LogInformation("Creating build receipt for project '{Project}' at: {Path}", projectName, receiptPath);

            try
            {
                var receipt = new
                {
                    generationId = Path.GetFileName(managedWorkspacePath),
                    buildTimestamp = DateTime.UtcNow,
                    builtProject = projectName,
                    buildSuccess = buildResult.Success,
                    artifactPath = buildResult.ArtifactPath,
                    artifactSha256 = await ComputeFileHashAsync(buildResult.ArtifactPath),
                    buildLog = buildResult.LogOutput
                };

                await using var fileStream = File.Create(receiptPath);
                await JsonSerializer.SerializeAsync(fileStream, receipt, _jsonOptions);
                _logger.LogInformation("✓ Successfully wrote build receipt for project '{Project}'.", projectName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write build receipt file for project '{Project}'.", projectName);
            }
        }
        public FileOutputService(ILogger<FileOutputService> logger)
        {
            _logger = logger;
        }

        public async Task WriteGenerationReceiptAsync(string managedWorkspacePath, JsonDocument manifest, JsonDocument discoveredApis, IEnumerable<GeneratedProject>? generatedProjects)
        {
            var receiptPath = Path.Combine(managedWorkspacePath, GenerationReceiptFileName);
            _logger.LogInformation("Creating final generation receipt at: {Path}", receiptPath);

            try
            {
                var receipt = new GenerationReceipt(
                    GenerationId: Path.GetFileName(managedWorkspacePath),
                    Timestamp: DateTime.UtcNow,
                    ToolVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                    Manifest: manifest.RootElement.Clone(),
                    DiscoveredApis: discoveredApis.RootElement.Clone(),
                    GeneratedProjects: generatedProjects?.ToList() ?? new List<GeneratedProject>()
                );

                await using var fileStream = File.Create(receiptPath);
                await JsonSerializer.SerializeAsync(fileStream, receipt, _jsonOptions);

                _logger.LogInformation("✓ Successfully wrote generation receipt.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write generation receipt file.");
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, "Could not write generation receipt.", ex);
            }
        }

        public async Task<GenerationReceipt> ReadGenerationReceiptAsync(string managedWorkspacePath)
        {
            var receiptPath = Path.Combine(managedWorkspacePath, GenerationReceiptFileName);
            _logger.LogDebug("Reading generation receipt from: {Path}", receiptPath);

            if (!File.Exists(receiptPath))
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Generation receipt not found at '{receiptPath}'. Cannot proceed with deployment.");
            }

            try
            {
                await using var fileStream = File.OpenRead(receiptPath);
                var receipt = await JsonSerializer.DeserializeAsync<GenerationReceipt>(fileStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (receipt == null)
                {
                    throw new AssemblerException(AssemblerExitCode.DeploymentFailure, "Failed to deserialize the generation receipt (result was null).");
                }

                _logger.LogInformation("✓ Successfully read and validated generation receipt.");
                return receipt;
            }
            catch (JsonException ex)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Error reading generation receipt: {ex.Message}. The file may be corrupt.", ex);
            }
        }

        public async Task WriteDeploymentReceiptAsync(string managedWorkspacePath, string groupName, string artifactPath)
        {
            var receiptFileName = $"deployment-receipt.{groupName}.json";
            var receiptPath = Path.Combine(managedWorkspacePath, receiptFileName);
            _logger.LogInformation("Creating deployment receipt for group '{Group}' at: {Path}", groupName, receiptPath);

            try
            {
                var artifactHash = await ComputeFileHashAsync(artifactPath);
                var receipt = new
                {
                    generationId = Path.GetFileName(managedWorkspacePath),
                    deploymentTimestamp = DateTime.UtcNow,
                    deployedGroup = groupName,
                    deployedArtifactPath = artifactPath,
                    deployedArtifactSha256 = artifactHash
                };

                await using var fileStream = File.Create(receiptPath);
                await JsonSerializer.SerializeAsync(fileStream, receipt, _jsonOptions);

                _logger.LogInformation("✓ Successfully wrote deployment receipt for group '{Group}'.", groupName);
            }
            catch (Exception ex)
            {
                // This is a non-critical operation. A failure to write this receipt should not fail the pipeline.
                _logger.LogError(ex, "Failed to write deployment receipt file for group '{Group}'. This will not fail the deployment.", groupName);
            }
        }

        private async Task<string> ComputeFileHashAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Cannot compute hash. Artifact file not found at: {FilePath}", filePath);
                return "file-not-found";
            }

            try
            {
                using var sha256 = SHA256.Create();
                await using var fileStream = File.OpenRead(filePath);
                var hash = await sha256.ComputeHashAsync(fileStream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not compute hash for artifact file: {FilePath}", filePath);
                return "hash-computation-failed";
            }
        }
    }

    /// <summary>
    /// Represents the complete, auditable record of a 'generate' command execution.
    /// Using a record for immutability and value-based equality.
    /// </summary>
    public record GenerationReceipt(
        string GenerationId,
        DateTime Timestamp,
        string? ToolVersion,
        JsonElement Manifest,
        JsonElement DiscoveredApis,
        List<GeneratedProject> GeneratedProjects)
    {
        public JsonElement GetDeploymentGroup(string groupName)
        {
            if (Manifest.TryGetProperty("groups", out var groups) &&
                groups.TryGetProperty(groupName, out var groupConfig) &&
                groupConfig.TryGetProperty("cloud", out var cloudElement) &&
                groupConfig.TryGetProperty("pattern", out var patternElement))
            {
                var deployable = new
                {
                    groupName,
                    cloud = cloudElement.GetString(),
                    pattern = patternElement.GetString()
                };
                return JsonDocument.Parse(JsonSerializer.Serialize(deployable)).RootElement;
            }

            throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Group '{groupName}' or its required 'cloud'/'pattern' properties not found in the generation receipt manifest.");
        }
    }
}