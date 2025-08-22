using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class AwsDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public AwsDeploymentProvider(ILogger<AwsDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            return pattern.ToLowerInvariant() switch
            {
                "lambda-zip" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".zip", System.StringComparison.OrdinalIgnoreCase),
                "s3-zip" => File.Exists(artifactPath) && Path.GetExtension(artifactPath).Equals(".zip", System.StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config)
        {
            var groupName = deployable.GetProperty("groupName").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            // These values would be sourced from the manifest or specific AWS env vars
            var region = Environment.GetEnvironmentVariable("ASSEMBLER_AWS_REGION") ?? "us-east-1";
            var functionName = $"func-{groupName}";
            var bucketName = $"bucket-{groupName}";
            var roleArn = Environment.GetEnvironmentVariable("ASSEMBLER_AWS_IAM_ROLE_ARN");

            var (command, args) = pattern switch
            {
                "lambda-zip" => ("aws", $"lambda update-function-code --function-name {functionName} --zip-file fileb://\"{artifactPath}\" --region {region}"),
                "s3-zip" => ("aws", $"s3 cp \"{artifactPath}\" s3://{bucketName}/ --region {region}"),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"AWS deployment pattern '{pattern}' is not supported."),
            };

            if (pattern == "lambda-zip" && string.IsNullOrWhiteSpace(roleArn))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "ASSEMBLER_AWS_IAM_ROLE_ARN must be set for Lambda deployments.");
            }

            var (success, _, error) = await ExecuteCommandLineProcessAsync(command, args);
            if (!success)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"AWS deployment failed: {error}");
            }
        }
    }
}