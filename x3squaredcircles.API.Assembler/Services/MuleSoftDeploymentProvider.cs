using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class MuleSoftDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public MuleSoftDeploymentProvider(ILogger<MuleSoftDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            // MuleSoft deploys JAR files.
            return pattern.ToLowerInvariant() switch
            {
                "anypoint-jar" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".jar", System.StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific MuleSoft env vars
            var anypointEnv = Environment.GetEnvironmentVariable("ASSEMBLER_MULESOFT_ENV") ?? "Sandbox";
            var anypointUsername = Environment.GetEnvironmentVariable("ASSEMBLER_MULESOFT_USERNAME");
            var anypointPassword = Environment.GetEnvironmentVariable("ASSEMBLER_MULESOFT_PASSWORD"); // Should be a secret
            var applicationName = $"app-{groupName}";
            var muleVersion = deployable.GetProperty("muleVersion").GetString() ?? "4.4.0";


            if (string.IsNullOrWhiteSpace(anypointUsername) || string.IsNullOrWhiteSpace(anypointPassword))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_MULESOFT_USERNAME and ASSEMBLER_MULESOFT_PASSWORD must be set for MuleSoft deployments.");
            }

            var (command, args) = pattern switch
            {
                "anypoint-jar" => ("mvn", $"deploy -DmuleDeploy -DapplicationName={applicationName} -Dfile=\"{artifactPath}\" -Denvironment={anypointEnv} -DmuleVersion={muleVersion} -Dusername={anypointUsername} -Dpassword=\"{anypointPassword}\""),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"MuleSoft deployment pattern '{pattern}' is not supported."),
            };

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, args);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"MuleSoft deployment failed: {error}");
            }
        }
    }
}