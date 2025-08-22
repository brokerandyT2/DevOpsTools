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
            // This is the first action, logging local tool execution for forensic analysis.
            await WritePipelineToolsLogAsync();

            var host = CreateHostBuilder(args).Build();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var controlPointService = host.Services.GetRequiredService<IControlPointService>();

            try
            {
                logger.LogInformation("🚀 {ToolName} v{ToolVersion} starting...", ToolName, ToolVersion);

                // OnStartup is the very first action after DI setup.
                await controlPointService.InvokeOnStartupAsync();

                // Start any long-running background services like the DX Server.
                await host.StartAsync();

                // Execute the main application logic.
                var orchestrator = host.Services.GetRequiredService<AssemblerOrchestrator>();
                var exitCode = await orchestrator.RunAsync(args);

                // Stop background services.
                await host.StopAsync();

                // OnSuccess is the very last action on a clean exit.
                await controlPointService.InvokeOnSuccessAsync();

                logger.LogInformation("✅ {ToolName} completed successfully.", ToolName);
                return exitCode;

            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "[FATAL] An unhandled exception occurred during execution.");

                // OnFailure is the very last action on a catastrophic failure.
                await controlPointService.InvokeOnFailureAsync(ex);

                if (host != null)
                {
                    await host.StopAsync();
                }

                return (ex is AssemblerException ae) ? (int)ae.ExitCode : (int)AssemblerExitCode.UnhandledException;
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(provider => EnvironmentConfigurationLoader.LoadConfiguration());
                    services.AddSingleton<ConfigurationValidator>();
                    ConfigureAppServices(services);
                })
                .ConfigureLogging((hostContext, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();

                    var sp = logging.Services.BuildServiceProvider();
                    var config = sp.GetRequiredService<AssemblerConfiguration>();

                    logging.SetMinimumLevel(config.Logging.LogLevel);

                    if (!string.IsNullOrWhiteSpace(config.Logging.ExternalLogEndpoint))
                    {
                        logging.AddProvider(sp.GetRequiredService<ExternalHttpLoggerProvider>());
                    }
                });

        private static void ConfigureAppServices(IServiceCollection services)
        {
            // Register core services
            services.AddHttpClient(); // Add a general HttpClientFactory
            services.AddSingleton<IControlPointService, ControlPointService>();
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

            // Register language-specific code generators for the factory
            services.AddTransient<CSharpGenerator>();
            services.AddTransient<JavaGenerator>();
            services.AddTransient<PythonGenerator>();
            services.AddTransient<TypeScriptGenerator>();
            services.AddTransient<GoGenerator>();
            services.AddTransient<JavaScriptGenerator>();
            services.AddSingleton<ILanguageGeneratorFactory, LanguageGeneratorFactory>();

            // Register cloud deployment providers for the factory
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

            // Register the embedded DX web server, the main orchestrator, and the external logger provider
            services.AddHostedService<DxServerService>();
            services.AddSingleton<AssemblerOrchestrator>();
            services.AddSingleton<ExternalHttpLoggerProvider>();
        }

        /// <summary>
        /// Appends a log entry to a local file to track which 3SC tools have been executed.
        /// This provides a simple, local forensic trail.
        /// </summary>
        private static async Task WritePipelineToolsLogAsync()
        {
            try
            {
                ForensicLogger.WriteForensicLogEntryAsync(ToolName, ToolVersion);
            }
            catch (Exception ex)
            {
                // This is a non-critical operation; log to console and continue.
                Console.WriteLine($"[WARN] Could not write to pipeline-tools.log: {ex.Message}");
            }
        }
    }
}