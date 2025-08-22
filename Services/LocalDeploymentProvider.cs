using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class LocalDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public LocalDeploymentProvider(ILogger<LocalDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            // For a local "deployment", we just ensure the artifact exists.
            return File.Exists(artifactPath);
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var targetDir = deployable.GetProperty("targetDirectory").GetString();

            if (string.IsNullOrWhiteSpace(targetDir))
            {
                // If no target directory is specified in the manifest, do nothing.
                _logger.LogInformation("Local deployment pattern for group '{Group}' selected. No target directory specified. Artifact is ready at: {Path}", groupName, artifactPath);
                await Task.CompletedTask;
                return;
            }

            _logger.LogInformation("Local deployment pattern for group '{Group}' selected. Copying artifact to: {TargetDir}", groupName, targetDir);

            try
            {
                Directory.CreateDirectory(targetDir);
                var destFileName = Path.Combine(targetDir, Path.GetFileName(artifactPath));
                File.Copy(artifactPath, destFileName, true); // Overwrite if it exists
                _logger.LogInformation("✓ Successfully copied artifact to '{Destination}'.", destFileName);
            }
            catch (Exception ex)
            {
                var error = $"Failed to copy artifact for local deployment: {ex.Message}";
                _logger.LogError(ex, error);
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, error);
            }

            await Task.CompletedTask;
        }
    }
}