using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;
using x3squaredcircles.DesignToken.Generator.Services;

namespace x3squaredcircles.DesignToken.Generator
{
    public static class Program
    {
        private static readonly string ToolName = Assembly.GetExecutingAssembly().GetName().Name?.ToString() ?? "pipeline-designtoken-generator";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        public static async Task<int> Main(string[] args)
        {
            WritePipelineToolsLogAsync();
            var host = CreateHostBuilder(args).Build();
            var logger = host.Services.GetRequiredService<IAppLogger>();

            try
            {
                logger.LogInfo("🎨 3SC Design Token Generator starting...");

                var orchestrator = host.Services.GetRequiredService<IDesignTokenOrchestrator>();
                var exitCode = await orchestrator.RunAsync();

                logger.LogInfo($"Design Token Generator completed with exit code: {(int)exitCode}");
                return (int)exitCode;
            }
            catch (DesignTokenException ex)
            {
                logger.LogCritical($"A known error occurred, terminating application. Error: {ex.Message}");
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                logger.LogCritical("An unexpected fatal error occurred during application execution.", ex);
                return (int)DesignTokenExitCode.UnhandledException;
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Core Services (Config, Logging, Control Points)
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton(sp => sp.GetRequiredService<IConfigurationService>().GetConfiguration());
                    services.AddSingleton<IAppLogger, Logger>();
                    services.AddHttpClient("ControlPointClient");
                    services.AddHttpClient("LoggingClient");
                    services.AddSingleton<IControlPointService, ControlPointService>();

                    // License and Security Services
                    services.AddHttpClient<ILicenseClientService, LicenseClientService>();
                    services.AddHttpClient<IKeyVaultService, KeyVaultService>();

                    // Git and File Services
                    services.AddSingleton<IGitOperationsService, GitOperationsService>();
                    services.AddSingleton<IFileOutputService, FileOutputService>();
                    services.AddSingleton<ITagTemplateService, TagTemplateService>();
                    services.AddSingleton<ICustomSectionService, CustomSectionService>();

                    // Design Platform Connectors (All are now registered)
                    services.AddHttpClient<IFigmaConnectorService, FigmaConnectorService>();
                    services.AddHttpClient<ISketchConnectorService, SketchConnectorService>();
                    services.AddHttpClient<IAdobeXdConnectorService, AdobeXdConnectorService>();
                    services.AddHttpClient<IZeplinConnectorService, ZeplinConnectorService>();
                    services.AddHttpClient<IAbstractConnectorService, AbstractConnectorService>();
                    services.AddHttpClient<IPenpotConnectorService, PenpotConnectorService>();
                    services.AddSingleton<IDesignPlatformFactory, DesignPlatformFactory>();

                    // Token Processing Services
                    services.AddSingleton<ITokenExtractionService, TokenExtractionService>();
                    services.AddSingleton<ITokenNormalizationService, TokenNormalizationService>();

                    // Platform Generators
                    services.AddSingleton<IAndroidGeneratorService, AndroidGeneratorService>();
                    services.AddSingleton<IIosGeneratorService, IosGeneratorService>();
                    services.AddSingleton<IWebGeneratorService, WebGeneratorService>();
                    services.AddSingleton<IPlatformGeneratorFactory, PlatformGeneratorFactory>();

                    // Main Orchestrator
                    services.AddSingleton<IDesignTokenOrchestrator, DesignTokenOrchestrator>();

                    // The embedded DX web server
                    services.AddHostedService<HttpServerService>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.Services.Clear(); // Suppress all default Microsoft logging.
                });
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