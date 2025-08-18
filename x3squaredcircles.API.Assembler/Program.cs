using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;
using x3squaredcircles.API.Assembler.Services;

namespace x3squaredcircles.API.Assembler
{
    public class Program
    {
        private static readonly string ToolName = Assembly.GetExecutingAssembly().GetName().Name ?? "3sc-api-assembler";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public static async Task<int> Main(string[] args)
        {
            await WritePipelineToolsLogAsync();

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Register configuration and validation services
                    services.AddSingleton(provider => EnvironmentConfigurationLoader.LoadConfiguration());
                    services.AddSingleton<ConfigurationValidator>();

                    // Register core services
                    services.AddSingleton<ILicenseClientService, LicenseClientService>();
                    services.AddSingleton<IGitOperationsService, GitOperationsService>();
                    services.AddSingleton<ITagTemplateService, TagTemplateService>();
                    services.AddSingleton<IFileOutputService, FileOutputService>();
                    services.AddSingleton<IWorkspaceService, WorkspaceService>();
                    services.AddSingleton<IManifestGeneratorService, ManifestGeneratorService>();
                    services.AddSingleton<IDiscoveryService, DiscoveryService>();
                    services.AddSingleton<ICodeGenerationService, CodeGenerationService>();
                    services.AddSingleton<IDependencyInferenceService, DependencyInferenceService>();
                    services.AddSingleton<IDeploymentService, DeploymentService>();

                    // Register language-specific code generators and the factory
                    services.AddTransient<CSharpGenerator>();
                    services.AddTransient<JavaGenerator>();
                    services.AddTransient<PythonGenerator>();
                    services.AddTransient<TypeScriptGenerator>();
                    services.AddTransient<GoGenerator>();
                    services.AddTransient<JavaScriptGenerator>();
                    services.AddSingleton<ILanguageGeneratorFactory, LanguageGeneratorFactory>();

                    // Register cloud deployment providers and factory
                    services.AddTransient<AzureDeploymentProvider>();
                    services.AddTransient<AwsDeploymentProvider>();
                    services.AddTransient<GcpDeploymentProvider>();
                    services.AddTransient<OracleDeploymentProvider>();
                    services.AddTransient<MuleSoftDeploymentProvider>();
                    services.AddTransient<IbmWebSphereDeploymentProvider>();
                    services.AddTransient<IbmDataPowerDeploymentProvider>();
                    services.AddTransient<IbmApiConnectDeploymentProvider>();
                    services.AddTransient<ApacheCamelDeploymentProvider>();
                    services.AddTransient<RedHatFuseDeploymentProvider>();
                    services.AddTransient<TibcoBusinessWorksDeploymentProvider>();
                    services.AddTransient<LocalDeploymentProvider>();
                    services.AddSingleton<ICloudProviderFactory, CloudProviderFactory>();

                    // Register the embedded DX web server
                    services.AddHostedService<DxServerService>();

                    // Register the main orchestrator
                    services.AddSingleton<AssemblerOrchestrator>();

                    // Add HTTP client factory for services that need it
                    services.AddHttpClient();
                })
                .ConfigureLogging((hostContext, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();

                    // This service provider is used *only* during setup to get the config.
                    var tempServices = logging.Services.BuildServiceProvider();
                    var config = tempServices.GetRequiredService<AssemblerConfiguration>();
                    var httpClientFactory = tempServices.GetRequiredService<IHttpClientFactory>();

                    logging.SetMinimumLevel(config.Logging.LogLevel);

                    // Add the external HTTP logger provider IF it has been configured
                    if (!string.IsNullOrWhiteSpace(config.Logging.ExternalLogEndpoint) &&
                        (!string.IsNullOrWhiteSpace(config.Logging.ExternalLogToken) || !string.IsNullOrWhiteSpace(config.Logging.ExternalLogTokenVaultKey)))
                    {
                        logging.AddProvider(new ExternalHttpLoggerProvider(config, httpClientFactory));
                    }
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            try
            {
                await host.StartAsync();

                logger.LogInformation("🚀 {ToolName} v{ToolVersion} starting main execution...", ToolName, ToolVersion);

                var orchestrator = host.Services.GetRequiredService<AssemblerOrchestrator>();
                var exitCode = await orchestrator.RunAsync(args);

                if (exitCode == 0)
                {
                    logger.LogInformation("✅ {ToolName} completed successfully.", ToolName);
                }

                await host.StopAsync();
                return exitCode;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "[FATAL] An unhandled exception occurred during application startup or shutdown.");
                if (host != null)
                {
                    await host.StopAsync();
                }
                return (int)AssemblerExitCode.UnhandledException;
            }
        }

        private static async Task WritePipelineToolsLogAsync()
        {
            try
            {
                var logEntry = $"{ToolName}={ToolVersion}{Environment.NewLine}";
                var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), "pipeline-tools.log");
                await File.AppendAllTextAsync(logFilePath, logEntry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not write to pipeline-tools.log: {ex.Message}");
            }
        }
    }
}