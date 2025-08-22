using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class IbmDataPowerDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public IbmDataPowerDeploymentProvider(ILogger<IbmDataPowerDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            // DataPower typically deploys a zip archive containing configuration and scripts.
            return pattern.ToLowerInvariant() switch
            {
                "datapower-zip" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".zip", System.StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific DataPower env vars
            var applianceHost = Environment.GetEnvironmentVariable("ASSEMBLER_DATAPOWER_HOST");
            var username = Environment.GetEnvironmentVariable("ASSEMBLER_DATAPOWER_USERNAME");
            var password = Environment.GetEnvironmentVariable("ASSEMBLER_DATAPOWER_PASSWORD"); // Should be a secret
            var domain = deployable.GetProperty("domain").GetString();
            var overwriteObjects = deployable.GetProperty("overwriteObjects").GetString() ?? "on";
            var overwriteFiles = deployable.GetProperty("overwriteFiles").GetString() ?? "on";

            if (string.IsNullOrWhiteSpace(applianceHost) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(domain))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_DATAPOWER_HOST, _USERNAME, _PASSWORD, and a 'domain' in the manifest are required for DataPower deployments.");
            }

            // DataPower deployments are often done via REST Management Interface or XML Management Interface.
            // This example simulates a command-line tool that wraps these APIs.
            var toolPath = "/app/tools/datapower-deploy-tool.sh";
            var args = $"--host {applianceHost} --user {username} --password \"{password}\" --domain {domain} --import-config \"{artifactPath}\" --overwrite-objects {overwriteObjects} --overwrite-files {overwriteFiles}";

            var (command, execArgs) = pattern switch
            {
                "datapower-zip" => (toolPath, args),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"IBM DataPower deployment pattern '{pattern}' is not supported."),
            };

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, execArgs);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"IBM DataPower deployment failed: {error}");
            }
        }
    }
}