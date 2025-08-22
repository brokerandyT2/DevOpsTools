using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class RedHatFuseDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public RedHatFuseDeploymentProvider(ILogger<RedHatFuseDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            // Red Hat Fuse (on OpenShift/Karaf) typically deploys JAR or KAR files.
            var extension = Path.GetExtension(artifactPath).ToLowerInvariant();
            return pattern.ToLowerInvariant() switch
            {
                "fuse-jar" => File.Exists(artifactPath) && extension == ".jar",
                "fuse-kar" => File.Exists(artifactPath) && extension == ".kar",
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific Fuse/OpenShift env vars
            var openShiftServer = Environment.GetEnvironmentVariable("ASSEMBLER_OPENSHIFT_SERVER");
            var openShiftToken = Environment.GetEnvironmentVariable("ASSEMBLER_OPENSHIFT_TOKEN"); // Should be a secret
            var openShiftProject = Environment.GetEnvironmentVariable("ASSEMBLER_OPENSHIFT_PROJECT");
            var deploymentName = $"app-{groupName}";

            if (string.IsNullOrWhiteSpace(openShiftServer) || string.IsNullOrWhiteSpace(openShiftToken) || string.IsNullOrWhiteSpace(openShiftProject))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_OPENSHIFT_SERVER, _TOKEN, and _PROJECT are required for Red Hat Fuse deployments.");
            }

            // This flow assumes a binary deployment to an existing deployment config on OpenShift.
            // 1. Log in to OpenShift
            var loginArgs = $"login {openShiftServer} --token=\"{openShiftToken}\"";
            var (loginSuccess, _, loginError) = await ExecuteCommandLineProcessAsync("oc", loginArgs);
            if (!loginSuccess)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Failed to log in to OpenShift: {loginError}");
            }

            // 2. Switch to the correct project
            var projectArgs = $"project {openShiftProject}";
            var (projectSuccess, _, projectError) = await ExecuteCommandLineProcessAsync("oc", projectArgs);
            if (!projectSuccess)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Failed to switch to OpenShift project '{openShiftProject}': {projectError}");
            }

            // 3. Start the binary build/deployment
            var deployArgs = $"start-build {deploymentName} --from-file=\"{artifactPath}\" --wait";

            if (pattern != "fuse-jar" && pattern != "fuse-kar")
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Red Hat Fuse deployment pattern '{pattern}' is not supported.");
            }

            var (deploySuccess, _, deployError) = await ExecuteCommandLineProcessAsync("oc", deployArgs);
            if (!deploySuccess)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Red Hat Fuse deployment failed: {deployError}");
            }
        }
    }
}