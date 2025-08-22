using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class IbmApiConnectDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public IbmApiConnectDeploymentProvider(ILogger<IbmApiConnectDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            // API Connect typically deploys a YAML file defining the API product.
            var extension = Path.GetExtension(artifactPath).ToLowerInvariant();
            return pattern.ToLowerInvariant() switch
            {
                "apic-product-yaml" => File.Exists(artifactPath) && (extension == ".yml" || extension == ".yaml"),
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific API Connect env vars
            var managementServer = Environment.GetEnvironmentVariable("ASSEMBLER_APIC_MANAGEMENT_SERVER");
            var username = Environment.GetEnvironmentVariable("ASSEMBLER_APIC_USERNAME");
            var password = Environment.GetEnvironmentVariable("ASSEMBLER_APIC_PASSWORD"); // Should be a secret
            var realm = Environment.GetEnvironmentVariable("ASSEMBLER_APIC_REALM") ?? "provider/default-idp-2";
            var org = deployable.GetProperty("organization").GetString();
            var catalog = deployable.GetProperty("catalog").GetString();

            if (string.IsNullOrWhiteSpace(managementServer) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(catalog))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_APIC_MANAGEMENT_SERVER, _USERNAME, _PASSWORD, and 'organization'/'catalog' in the manifest are required for API Connect deployments.");
            }

            // API Connect deployments are done via the `apic` CLI tool.
            var args = $"products:publish \"{artifactPath}\" --server {managementServer} --username {username} --password \"{password}\" --realm {realm} --org {org} --catalog {catalog}";

            var (command, execArgs) = pattern switch
            {
                "apic-product-yaml" => ("apic", args),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"IBM API Connect deployment pattern '{pattern}' is not supported."),
            };

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, execArgs);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"IBM API Connect deployment failed: {error}");
            }
        }
    }
}