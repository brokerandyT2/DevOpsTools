using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class AzureDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public AzureDeploymentProvider(ILogger<AzureDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            return pattern.ToLowerInvariant() switch
            {
                // For a zip deployment, just verify the zip file exists and is not empty.
                "functions-zip" or "appservice-zip" => File.Exists(artifactPath) && new FileInfo(artifactPath).Length > 0,
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // Source configuration from harmonized variables
            var resourceGroup = Environment.GetEnvironmentVariable("ASSEMBLER_AZURE_RESOURCE_GROUP");
            var appName = deployable.GetProperty("appName").GetString() ?? $"app-{groupName}"; // Prefer manifest, fallback to convention

            if (string.IsNullOrWhiteSpace(resourceGroup))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "Required environment variable 'ASSEMBLER_AZURE_RESOURCE_GROUP' is not set for Azure deployment.");
            }

            // Using 'az' CLI for deployment. Assumes the user is already logged in.
            var (command, args) = pattern switch
            {
                "functions-zip" => ("az", $"functionapp deployment source config-zip -g {resourceGroup} -n {appName} --src \"{artifactPath}\""),
                "appservice-zip" => ("az", $"webapp deployment source config-zip -g {resourceGroup} -n {appName} --src \"{artifactPath}\""),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Azure deployment pattern '{pattern}' is not supported."),
            };

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, args);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Azure deployment failed: {error}");
            }
        }
    }
}