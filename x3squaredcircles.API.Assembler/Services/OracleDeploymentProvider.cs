using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class OracleDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public OracleDeploymentProvider(ILogger<OracleDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            return pattern.ToLowerInvariant() switch
            {
                "functions-zip" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".zip", System.StringComparison.OrdinalIgnoreCase),
                "objectstorage-zip" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".zip", System.StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific OCI env vars
            var compartmentId = Environment.GetEnvironmentVariable("ASSEMBLER_OCI_COMPARTMENT_ID");
            var applicationName = $"app-{groupName}";
            var functionName = $"func-{groupName}";
            var bucketName = $"bucket-{groupName}";

            if (string.IsNullOrWhiteSpace(compartmentId))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_OCI_COMPARTMENT_ID must be set for Oracle Cloud deployments.");
            }

            var (command, args) = pattern switch
            {
                "functions-zip" => ("oci", $"fn deploy --app {applicationName} --function {functionName} --zip-file \"{artifactPath}\""),
                "objectstorage-zip" => ("oci", $"os object put -bn {bucketName} --file \"{artifactPath}\" --name {Path.GetFileName(artifactPath)}"),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Oracle Cloud (OCI) deployment pattern '{pattern}' is not supported."),
            };

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, args);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Oracle Cloud deployment failed: {error}");
            }
        }
    }
}