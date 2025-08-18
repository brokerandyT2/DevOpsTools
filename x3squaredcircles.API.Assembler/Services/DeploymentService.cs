using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
        Task DeployAsync(JsonElement deployable, string artifactPath);
        bool VerifyArtifact(string pattern, string artifactPath);
    }

    #endregion

    #region Core Deployment Service and Factory

    public class DeploymentService : IDeploymentService
    {
        private readonly ILogger<DeploymentService> _logger;
        private readonly ICloudProviderFactory _cloudProviderFactory;

        public DeploymentService(ILogger<DeploymentService> logger, ICloudProviderFactory cloudProviderFactory)
        {
            _logger = logger;
            _cloudProviderFactory = cloudProviderFactory;
        }

        public async Task VerifyArtifactAsync(JsonElement deployable, string artifactPath)
        {
            var groupName = deployable.GetProperty("groupName").GetString();
            var cloud = deployable.GetProperty("cloud").GetString()?.ToLowerInvariant();
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            _logger.LogInformation("Verifying artifact at '{ArtifactPath}' for group '{Group}' targeting {Cloud}/{Pattern}.", artifactPath, groupName, cloud, pattern);

            if (!Directory.Exists(artifactPath) && !File.Exists(artifactPath))
            {
                throw new AssemblerException(AssemblerExitCode.ArtifactVerificationFailure, $"Artifact path not found: {artifactPath}");
            }

            var provider = _cloudProviderFactory.Create(cloud);
            var isVerified = provider.VerifyArtifact(pattern, artifactPath);

            if (!isVerified)
            {
                throw new AssemblerException(AssemblerExitCode.ArtifactVerificationFailure, $"Artifact at '{artifactPath}' is not valid for the '{pattern}' deployment pattern on the '{cloud}' platform.");
            }

            _logger.LogInformation("✓ Artifact verification successful for group '{Group}'.", groupName);
            await Task.CompletedTask;
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            var groupName = deployable.GetProperty("groupName").GetString();
            var cloud = deployable.GetProperty("cloud").GetString()?.ToLowerInvariant();

            _logger.LogInformation("Initiating deployment of artifact '{ArtifactPath}' for group '{Group}' to cloud provider '{Cloud}'.", artifactPath, groupName, cloud);

            var provider = _cloudProviderFactory.Create(cloud);
            await provider.DeployAsync(deployable, artifactPath);

            _logger.LogInformation("✓ Deployment command execution for group '{Group}' completed.", groupName);
        }
    }

    public class CloudProviderFactory : ICloudProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;
        public CloudProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ICloudDeploymentProvider Create(string cloudProviderName)
        {
            return cloudProviderName.ToLowerInvariant() switch
            {
                "azure" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(AzureDeploymentProvider)),
                "aws" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(AwsDeploymentProvider)),
                "gcp" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(GcpDeploymentProvider)),
                "oracle" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(OracleDeploymentProvider)),
                "mulesoft" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(MuleSoftDeploymentProvider)),
                "ibmwebsphere" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(IbmWebSphereDeploymentProvider)),
                "ibmdatapower" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(IbmDataPowerDeploymentProvider)),
                "ibmapiconnect" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(IbmApiConnectDeploymentProvider)),
                "apachecamel" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(ApacheCamelDeploymentProvider)),
                "redhatfuse" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(RedHatFuseDeploymentProvider)),
                "tibcobusinessworks" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(TibcoBusinessWorksDeploymentProvider)),
                "local" => (ICloudDeploymentProvider)_serviceProvider.GetService(typeof(LocalDeploymentProvider)),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Deployment to cloud provider '{cloudProviderName}' is not supported."),
            };
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

        protected async Task ExecuteCommandLineProcessAsync(string command, string args, string workingDirectory = "")
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

            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            _logger.LogInformation("Executing command: {Command} {Args}", command, args);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("Deployment command failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error.ToString());
                throw new AssemblerException(AssemblerExitCode.DeploymentFailure, $"External command failed: {error}");
            }

            _logger.LogInformation("Deployment command executed successfully. Output: {Output}", output.ToString());
        }
    }

    public class AzureDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public AzureDeploymentProvider(ILogger<AzureDeploymentProvider> logger) : base(logger) { }

        public bool VerifyArtifact(string pattern, string artifactPath)
        {
            return pattern switch
            {
                "functions" => Directory.GetFiles(artifactPath, "host.json").Any() && Directory.GetFiles(artifactPath, "*.dll").Any(),
                "appservice" or "aspnetcore" or "webapi" => Directory.GetFiles(artifactPath, "*.dll").Any() && Directory.GetFiles(artifactPath, "web.config").Any(),
                "staticwebapps" => Directory.GetFiles(artifactPath, "index.html").Any(),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Azure deployment pattern '{pattern}' is not supported for verification."),
            };
        }

        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            // A real implementation would parse resource group, app name, etc. from a manifest
            var resourceGroup = "my-resource-group";
            var appName = $"app-{deployable.GetProperty("groupName").GetString().ToLower()}";
            var pattern = deployable.GetProperty("pattern").GetString()?.ToLowerInvariant();

            var (command, args) = pattern switch
            {
                "functions" => ("az", $"functionapp deployment source config-zip -g {resourceGroup} -n {appName} --src \"{artifactPath}\""),
                "appservice" or "aspnetcore" or "webapi" => ("az", $"webapp deployment source config-zip -g {resourceGroup} -n {appName} --src \"{artifactPath}\""),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Azure deployment pattern '{pattern}' is not supported."),
            };

            await ExecuteCommandLineProcessAsync(command, args);
        }
    }

    public class AwsDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public AwsDeploymentProvider(ILogger<AwsDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("AWS Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class GcpDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public GcpDeploymentProvider(ILogger<GcpDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("GCP Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class OracleDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public OracleDeploymentProvider(ILogger<OracleDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("Oracle Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class MuleSoftDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public MuleSoftDeploymentProvider(ILogger<MuleSoftDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("MuleSoft Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class IbmWebSphereDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public IbmWebSphereDeploymentProvider(ILogger<IbmWebSphereDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("IBM WebSphere Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class IbmDataPowerDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public IbmDataPowerDeploymentProvider(ILogger<IbmDataPowerDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("IBM DataPower Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class IbmApiConnectDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public IbmApiConnectDeploymentProvider(ILogger<IbmApiConnectDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("IBM API Connect Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class ApacheCamelDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public ApacheCamelDeploymentProvider(ILogger<ApacheCamelDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("Apache Camel Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class RedHatFuseDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public RedHatFuseDeploymentProvider(ILogger<RedHatFuseDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("Red Hat Fuse Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class TibcoBusinessWorksDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public TibcoBusinessWorksDeploymentProvider(ILogger<TibcoBusinessWorksDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogWarning("TIBCO BusinessWorks Deployment provider is not fully implemented.");
            await Task.CompletedTask;
        }
    }

    public class LocalDeploymentProvider : BaseDeploymentProvider, ICloudDeploymentProvider
    {
        public LocalDeploymentProvider(ILogger<LocalDeploymentProvider> logger) : base(logger) { }
        public bool VerifyArtifact(string pattern, string artifactPath) => true; // Placeholder
        public async Task DeployAsync(JsonElement deployable, string artifactPath)
        {
            _logger.LogInformation("Local deployment pattern does not execute a cloud deployment. Artifact is ready at: {Path}", artifactPath);
            await Task.CompletedTask;
        }
    }

    #endregion
}