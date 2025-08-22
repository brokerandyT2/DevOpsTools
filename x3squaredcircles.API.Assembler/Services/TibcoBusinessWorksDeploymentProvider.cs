using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class TibcoBusinessWorksDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public TibcoBusinessWorksDeploymentProvider(ILogger<TibcoBusinessWorksDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            // TIBCO BusinessWorks deploys Enterprise Archive (EAR) files.
            return pattern.ToLowerInvariant() switch
            {
                "bw-ear" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".ear", System.StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific TIBCO env vars
            var bwadminPath = Environment.GetEnvironmentVariable("ASSEMBLER_TIBCO_BWADMIN_PATH"); // e.g., /opt/tibco/bw/6.x/bin
            var domain = Environment.GetEnvironmentVariable("ASSEMBLER_TIBCO_DOMAIN");
            var appSpace = Environment.GetEnvironmentVariable("ASSEMBLER_TIBCO_APPSPACE");
            var profile = deployable.GetProperty("profile").GetString();
            var applicationName = $"app-{groupName}";

            if (string.IsNullOrWhiteSpace(bwadminPath) || string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(appSpace) || string.IsNullOrWhiteSpace(profile))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_TIBCO_BWADMIN_PATH, _DOMAIN, _APPSPACE, and a 'profile' in the manifest are required for TIBCO BW deployments.");
            }

            var bwadminExecutable = Path.Combine(bwadminPath, "bwadmin");

            // This flow uses the bwadmin CLI to upload, deploy, and start an application.
            // 1. Upload the application archive
            var uploadArgs = $"-d {domain} -a {appSpace} upload -replace \"{artifactPath}\"";
            var (uploadSuccess, _, uploadError) = await ExecuteCommandLineProcessAsync(bwadminExecutable, uploadArgs);
            if (!uploadSuccess)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"TIBCO BW: Failed to upload application: {uploadError}");
            }

            // 2. Deploy the application
            var deployArgs = $"-d {domain} -a {appSpace} deploy -p {profile} {applicationName}";
            var (deploySuccess, _, deployError) = await ExecuteCommandLineProcessAsync(bwadminExecutable, deployArgs);
            if (!deploySuccess)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"TIBCO BW: Failed to deploy application: {deployError}");
            }

            // 3. Start the application
            var startArgs = $"-d {domain} -a {appSpace} start application {applicationName}";

            if (pattern != "bw-ear")
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"TIBCO BusinessWorks deployment pattern '{pattern}' is not supported.");
            }

            var (startSuccess, _, startError) = await ExecuteCommandLineProcessAsync(bwadminExecutable, startArgs);
            if (!startSuccess)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"TIBCO BW: Failed to start application: {startError}");
            }
        }
    }
}