using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    #region Interfaces

    public interface IDeploymentService
    {
        Task VerifyArtifactAsync(JsonElement deployable, string artifactPath);
        Task DeployAsync(JsonElement deployable, string artifactPath);
    }

    public interface ICloudProviderFactory
    {
        ICloudDeploymentProvider Create(string cloudProviderName);
    }

    public interface ICloudDeploymentProvider
    {
        Task DeployAsync(JsonElement deployable, string artifactPath, AssemblerConfiguration config);
        bool VerifyArtifact(string pattern, string artifactPath);
    }

    #endregion

    #region Core Deployment Service and Factory

    public class DeploymentService : IDeploymentService
    {
        private readonly ILogger<DeploymentService> _logger;
        private readonly AssemblerConfiguration _config;
        private readonly ICloudProviderFactory _cloudProviderFactory;
        private readonly IControlPointService _controlPointService;

        public DeploymentService(
            ILogger<DeploymentService> logger,
            AssemblerConfiguration config,
            ICloudProviderFactory cloudProviderFactory,
            IControlPointService controlPointService)
        {
            _logger = logger;
            _config = config;
            _cloudProviderFactory = cloudProviderFactory;
            _controlPointService = controlPointService;
        }

        public Task VerifyArtifactAsync(JsonElement deployable, string artifactPath)
        {
            var groupName = deployable.GetProperty("groupName").GetString();
            var cloud = deployable.GetProperty("cloud").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            _logger.LogInformation("Verifying artifact at '{ArtifactPath}' for group '{Group}' targeting {Cloud}/{Pattern}.", artifactPath, groupName, cloud, pattern);

            if (!File.Exists(artifactPath))
            {
                throw new AssemblerException(AssemblerExitCode.ArtifactVerificationFailure, $"Artifact file not found: {artifactPath}");
            }

            var provider = _cloudProviderFactory.Create(cloud!);
            var isVerified = provider.VerifyArtifact(pattern!, artifactPath);

            if (!isVerified)
            {
                throw new AssemblerException(AssemblerExitCode.ArtifactVerificationFailure, $"Artifact at '{artifactPath}' is not valid for the '{pattern}' deployment pattern on the '{cloud}' platform.");
            }

            _logger.LogInformation("✓ Artifact verification successful for group '{Group}'.", groupName);
            return Task.CompletedTask;
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            var groupName = deployable.GetProperty("groupName").GetString();

            var deploymentToolControlPoint = Environment.GetEnvironmentVariable("ASSEMBLER_CP_DEPLOYMENT_TOOL");

            if (!string.IsNullOrWhiteSpace(deploymentToolControlPoint))
            {
                _logger.LogInformation("Custom deployment tool specified. Invoking Control Point for group '{Group}'.", groupName);
                await InvokeDeploymentToolControlPoint(deploymentToolControlPoint, deployable, artifactPath);
            }
            else
            {
                _logger.LogInformation("Using built-in deployment provider for group '{Group}'.", groupName);
                await ExecuteBuiltInDeployment(deployable, artifactPath);
            }
        }

        private async Task ExecuteBuiltInDeployment(JsonElement deployable, string artifactPath)
        {
            var groupName = deployable.GetProperty("groupName").GetString();
            var cloud = deployable.GetProperty("cloud").GetString()?.ToLowerInvariant();

            _logger.LogInformation("Initiating deployment of artifact '{ArtifactPath}' for group '{Group}' to built-in provider '{Cloud}'.", artifactPath, groupName, cloud);

            var provider = _cloudProviderFactory.Create(cloud!);
            await provider.DeployAsync(deployable, artifactPath, _config);

            _logger.LogInformation("✓ Built-in deployment for group '{Group}' completed.", groupName);
        }

        private async Task InvokeDeploymentToolControlPoint(string endpointUrl, JsonElement deployable, string artifactPath)
        {
            var payload = new
            {
                deployable,
                artifactPath,
                configuration = _config
            };

            var response = await _controlPointService.InvokeBlockingRequestAsync(endpointUrl, "DEPLOYMENT_TOOL", payload);

            if (!response.IsSuccess)
            {
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"Custom Deployment Tool Control Point failed: {response.ResponseMessage}");
            }

            _logger.LogInformation("✓ Custom Deployment Tool Control Point for deployment completed successfully.");
        }
    }

    public class CloudProviderFactory : ICloudProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private static readonly string[] SupportedProviders = { "azure", "aws", "gcp", "oracle", "mulesoft", "ibmwebsphere", "ibmdatapower", "ibmapiconnect", "apachecamel", "redhatfuse", "tibcobusinessworks", "local" };

        public CloudProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ICloudDeploymentProvider Create(string cloudProviderName)
        {
            var providerKey = cloudProviderName.ToLowerInvariant();
            if (!SupportedProviders.Contains(providerKey))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Deployment to cloud provider '{cloudProviderName}' is not supported. Supported providers are: {string.Join(", ", SupportedProviders)}");
            }

            try
            {
                return providerKey switch
                {
                    "azure" => _serviceProvider.GetRequiredService<AzureDeploymentProvider>(),
                    "aws" => _serviceProvider.GetRequiredService<AwsDeploymentProvider>(),
                    "gcp" => _serviceProvider.GetRequiredService<GcpDeploymentProvider>(),
                    "oracle" => _serviceProvider.GetRequiredService<OracleDeploymentProvider>(),
                    "mulesoft" => _serviceProvider.GetRequiredService<MuleSoftDeploymentProvider>(),
                    "ibmwebsphere" => _serviceProvider.GetRequiredService<IbmWebSphereDeploymentProvider>(),
                    "ibmdatapower" => _serviceProvider.GetRequiredService<IbmDataPowerDeploymentProvider>(),
                    "ibmapiconnect" => _serviceProvider.GetRequiredService<IbmApiConnectDeploymentProvider>(),
                    "apachecamel" => _serviceProvider.GetRequiredService<ApacheCamelDeploymentProvider>(),
                    "redhatfuse" => _serviceProvider.GetRequiredService<RedHatFuseDeploymentProvider>(),
                    "tibcobusinessworks" => _serviceProvider.GetRequiredService<TibcoBusinessWorksDeploymentProvider>(),
                    "local" => _serviceProvider.GetRequiredService<LocalDeploymentProvider>(),
                    _ => throw new InvalidOperationException($"Internal error: No provider registered for '{providerKey}'.")
                };
            }
            catch (Exception ex)
            {
                throw new AssemblerException(AssemblerExitCode.UnhandledException, $"Failed to create cloud deployment provider for '{cloudProviderName}'. Check application startup configuration.", ex);
            }
        }
    }

    #endregion

    #region Base Provider & Cloud Implementations

    public abstract class BaseDeploymentProvider
    {
        protected readonly ILogger _logger;
        protected BaseDeploymentProvider(ILogger logger)
        {
            _logger = logger;
        }

        protected async Task<(bool Success, string Output, string Error)> ExecuteCommandLineProcessAsync(string command, string args, string workingDirectory = "")
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var processExitCompletionSource = new TaskCompletionSource<int>();
            process.Exited += (_, _) => processExitCompletionSource.SetResult(process.ExitCode);
            process.EnableRaisingEvents = true;

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            _logger.LogInformation("Executing command: {Command} {Args}", command, args);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            var exitCode = await processExitCompletionSource.Task;

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (exitCode != 0)
            {
                _logger.LogError("Command failed with exit code {ExitCode}.\nOutput:\n{Output}\nError:\n{Error}", exitCode, output, error);
            }
            else
            {
                _logger.LogInformation("Command executed successfully. Output:\n{Output}", output);
            }

            return (exitCode == 0, output, error);
        }
    }   

    #endregion
}