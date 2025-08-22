using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class GcpDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public GcpDeploymentProvider(ILogger<GcpDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            return pattern.ToLowerInvariant() switch
            {
                "cloudfunction-zip" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".zip", System.StringComparison.OrdinalIgnoreCase),
                "cloudstorage-zip" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".zip", System.StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific GCP env vars
            var region = Environment.GetEnvironmentVariable("ASSEMBLER_GCP_REGION") ?? "us-central1";
            var project = Environment.GetEnvironmentVariable("ASSEMBLER_GCP_PROJECT_ID");
            var functionName = $"func-{groupName}";
            var entryPoint = deployable.GetProperty("entryPoint").GetString(); // e.g., 'HandleRequest'
            var runtime = deployable.GetProperty("runtime").GetString(); // e.g., 'dotnet8'
            var bucketName = $"gs://bucket-{groupName}";

            if (string.IsNullOrWhiteSpace(project))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_GCP_PROJECT_ID must be set for GCP deployments.");
            }

            var (command, args) = pattern switch
            {
                "cloudfunction-zip" => ("gcloud", $"functions deploy {functionName} --project={project} --region={region} --source=\"{artifactPath}\" --entry-point={entryPoint} --runtime={runtime} --trigger-http --allow-unauthenticated"),
                "cloudstorage-zip" => ("gcloud", $"storage cp \"{artifactPath}\" {bucketName} --project={project}"),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"GCP deployment pattern '{pattern}' is not supported."),
            };

            if (pattern == "cloudfunction-zip" && (string.IsNullOrWhiteSpace(entryPoint) || string.IsNullOrWhiteSpace(runtime)))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "An 'entryPoint' and 'runtime' must be defined in the manifest for GCP Cloud Function deployments.");
            }

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, args);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"GCP deployment failed: {error}");
            }
        }
    }
}