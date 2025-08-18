using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public interface IFileOutputService
    {
        Task WriteGenerationReceiptAsync(string managedWorkspacePath, JsonDocument manifest, JsonDocument discoveredApis, IEnumerable<GeneratedProject> generatedProjects);
        Task<GenerationReceipt> ReadGenerationReceiptAsync(string managedWorkspacePath);
        Task WriteDeploymentReceiptAsync(string managedWorkspacePath, string groupName, string artifactPath);
    }

    public class FileOutputService : IFileOutputService
    {
        private const string GenerationReceiptFileName = "generation-receipt.json";

        private readonly ILogger<FileOutputService> _logger;

        public FileOutputService(ILogger<FileOutputService> logger)
        {
            _logger = logger;
        }

        public async Task WriteGenerationReceiptAsync(string managedWorkspacePath, JsonDocument manifest, JsonDocument discoveredApis, IEnumerable<GeneratedProject> generatedProjects)
        {
            var receiptPath = Path.Combine(managedWorkspacePath, GenerationReceiptFileName);
            _logger.LogInformation("Creating final generation receipt at: {Path}", receiptPath);

            try
            {
                var receipt = new GenerationReceipt
                {
                    GenerationId = Path.GetFileName(managedWorkspacePath),
                    Timestamp = DateTime.UtcNow,
                    ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                    Manifest = manifest.RootElement.Clone(),
                    DiscoveredApis = discoveredApis.RootElement.Clone(),
                    GeneratedProjects = generatedProjects?.ToList()
                };

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var jsonContent = JsonSerializer.Serialize(receipt, jsonOptions);

                await File.WriteAllTextAsync(receiptPath, jsonContent);
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
                var jsonContent = await File.ReadAllTextAsync(receiptPath);
                var receipt = JsonSerializer.Deserialize<GenerationReceipt>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (receipt == null)
                {
                    throw new AssemblerException(AssemblerExitCode.DeploymentFailure, "Failed to deserialize the generation receipt.");
                }
                return receipt;
            }
            catch (JsonException ex)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Error reading generation receipt: {ex.Message}", ex);
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

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var jsonContent = JsonSerializer.Serialize(receipt, jsonOptions);
                await File.WriteAllTextAsync(receiptPath, jsonContent);
                _logger.LogInformation("✓ Successfully wrote deployment receipt for group '{Group}'.", groupName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write deployment receipt file for group '{Group}'.", groupName);
            }
        }

        private async Task<string> ComputeFileHashAsync(string filePath)
        {
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

    public class GenerationReceipt
    {
        public string GenerationId { get; set; }
        public DateTime Timestamp { get; set; }
        public string ToolVersion { get; set; }
        public JsonElement Manifest { get; set; }
        public JsonElement DiscoveredApis { get; set; }
        public List<GeneratedProject> GeneratedProjects { get; set; }

        public JsonElement GetDeployable(string groupName)
        {
            if (Manifest.TryGetProperty("groups", out var groups))
            {
                if (groups.TryGetProperty(groupName, out var groupConfig))
                {
                    var deployable = new
                    {
                        groupName = groupName,
                        cloud = groupConfig.GetProperty("cloud").GetString(),
                        pattern = groupConfig.GetProperty("pattern").GetString()
                    };
                    return JsonDocument.Parse(JsonSerializer.Serialize(deployable)).RootElement;
                }
            }
            throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Group '{groupName}' not found in the generation receipt manifest.");
        }
    }
}