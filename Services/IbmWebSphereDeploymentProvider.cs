using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class IbmWebSphereDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public IbmWebSphereDeploymentProvider(ILogger<IbmWebSphereDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            // WebSphere typically deploys EAR or WAR files.
            var extension = Path.GetExtension(artifactPath).ToLowerInvariant();
            return pattern.ToLowerInvariant() switch
            {
                "websphere-ear" => File.Exists(artifactPath) && extension == ".ear",
                "websphere-war" => File.Exists(artifactPath) && extension == ".war",
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific WebSphere env vars
            var adminHost = Environment.GetEnvironmentVariable("ASSEMBLER_WEBSPHERE_ADMIN_HOST");
            var adminPort = Environment.GetEnvironmentVariable("ASSEMBLER_WEBSPHERE_ADMIN_PORT") ?? "9060";
            var username = Environment.GetEnvironmentVariable("ASSEMBLER_WEBSPHERE_USERNAME");
            var password = Environment.GetEnvironmentVariable("ASSEMBLER_WEBSPHERE_PASSWORD"); // Should be a secret
            var server = deployable.GetProperty("server").GetString() ?? "server1";
            var node = deployable.GetProperty("node").GetString();
            var applicationName = $"app-{groupName}";

            if (string.IsNullOrWhiteSpace(adminHost) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(node))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_WEBSPHERE_ADMIN_HOST, _USERNAME, _PASSWORD, and a 'node' in the manifest are required for WebSphere deployments.");
            }

            // WebSphere deployments are often done via wsadmin scripts.
            // This example assumes a Jython script is available to perform the deployment.
            var scriptPath = "/app/scripts/deploy-websphere.py"; // Path inside the container
            var args = $"{scriptPath} --host {adminHost} --port {adminPort} --user {username} --password \"{password}\" --server {server} --node {node} --appName {applicationName} --artifactPath \"{artifactPath}\"";

            var (command, execArgs) = pattern switch
            {
                "websphere-ear" or "websphere-war" => ("wsadmin.sh", $"-lang jython -f {args}"),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"IBM WebSphere deployment pattern '{pattern}' is not supported."),
            };

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, execArgs);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"IBM WebSphere deployment failed: {error}");
            }
        }
    }
}