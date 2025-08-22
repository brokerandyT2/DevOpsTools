using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class ApacheCamelDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public ApacheCamelDeploymentProvider(ILogger<ApacheCamelDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            // Apache Camel routes are often packaged as JARs for deployment in runtimes like Karaf or Spring Boot.
            var extension = Path.GetExtension(artifactPath).ToLowerInvariant();
            return pattern.ToLowerInvariant() switch
            {
                "camel-jar" => File.Exists(artifactPath) && extension == ".jar",
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific env vars
            var deploymentTarget = Environment.GetEnvironmentVariable("ASSEMBLER_CAMEL_TARGET_RUNTIME"); // e.g., karaf, spring-boot
            var karafSshHost = Environment.GetEnvironmentVariable("ASSEMBLER_KARAF_SSH_HOST");
            var karafSshUser = Environment.GetEnvironmentVariable("ASSEMBLER_KARAF_SSH_USER");
            var springBootHost = Environment.GetEnvironmentVariable("ASSEMBLER_SPRINGBOOT_HOST");

            if (string.IsNullOrWhiteSpace(deploymentTarget))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_CAMEL_TARGET_RUNTIME must be set for Apache Camel deployments (e.g., 'karaf', 'spring-boot').");
            }

            string command;
            string execArgs;

            switch (deploymentTarget.ToLowerInvariant())
            {
                case "karaf":
                    if (string.IsNullOrWhiteSpace(karafSshHost) || string.IsNullOrWhiteSpace(karafSshUser))
                    {
                        throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_KARAF_SSH_HOST and _USER are required for Karaf deployments.");
                    }
                    // This simulates using ssh/scp to deploy to a Karaf 'deploy' folder.
                    command = "scp";
                    execArgs = $"\"{artifactPath}\" {karafSshUser}@{karafSshHost}:/opt/karaf/deploy/";
                    break;

                case "spring-boot":
                    if (string.IsNullOrWhiteSpace(springBootHost))
                    {
                        throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_SPRINGBOOT_HOST is required for Spring Boot deployments.");
                    }
                    // This simulates copying the artifact and restarting a remote service.
                    command = "bash";
                    execArgs = $"-c 'scp \"{artifactPath}\" user@{springBootHost}:/app/app.jar && ssh user@{springBootHost} \"sudo systemctl restart my-camel-app\"'";
                    break;

                default:
                    throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Apache Camel deployment target '{deploymentTarget}' is not supported.");
            }

            if (pattern != "camel-jar")
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Apache Camel deployment pattern '{pattern}' is not supported.");
            }

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, execArgs);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Apache Camel deployment failed: {error}");
            }
        }
    }
}